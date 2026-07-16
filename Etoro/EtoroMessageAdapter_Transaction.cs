namespace StockSharp.Etoro;

public partial class EtoroMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePortfolio(regMsg.PortfolioName);
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume, "eToro order volume must be positive.");

		var condition = regMsg.Condition as EtoroOrderCondition ?? new();
		if (condition.PositionId is <= 0)
			throw new InvalidOperationException("eToro position id must be positive when specified.");
		var isClose = condition.PositionId is > 0;
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit))
			throw new NotSupportedException($"eToro does not support StockSharp order type '{orderType}'.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("eToro market-if-touched orders require a positive trigger price.");
		if (!isClose && condition.Leverage <= 0)
			throw new InvalidOperationException("eToro leverage must be positive.");
		if (condition.StopLossRate is <= 0)
			throw new InvalidOperationException("eToro stop-loss rate must be positive.");
		if (condition.TakeProfitRate is <= 0)
			throw new InvalidOperationException("eToro take-profit rate must be positive.");
		if (condition.AdditionalMargin is < 0)
			throw new InvalidOperationException("eToro additional margin cannot be negative.");

		EtoroInstrument instrument = null;
		if (!isClose)
			instrument = await ResolveInstrument(regMsg.SecurityId, cancellationToken);

		var request = new EtoroUnifiedOrderRequest
		{
			Action = isClose ? EtoroOrderActions.Close : EtoroOrderActions.Open,
			Transaction = isClose
				? regMsg.Side == Sides.Buy ? EtoroTransactionTypes.BuyToCover : EtoroTransactionTypes.Sell
				: regMsg.Side == Sides.Buy ? EtoroTransactionTypes.Buy : EtoroTransactionTypes.SellShort,
			InstrumentId = isClose ? null : instrument.InstrumentId,
			SettlementType = isClose ? null : condition.SettlementType,
			OrderType = orderType == OrderTypes.Market ? EtoroNativeOrderTypes.Market : EtoroNativeOrderTypes.MarketIfTouched,
			TriggerRate = orderType == OrderTypes.Limit ? regMsg.Price : null,
			Leverage = isClose ? null : condition.Leverage,
			StopLossRate = isClose ? null : condition.StopLossRate,
			TakeProfitRate = isClose ? null : condition.TakeProfitRate,
			StopLossType = !isClose && condition.StopLossRate != null
				? condition.IsTrailingStopLoss ? EtoroStopLossTypes.Trailing : EtoroStopLossTypes.Fixed
				: null,
			AdditionalMargin = isClose ? null : condition.AdditionalMargin,
			PositionIds = isClose ? [condition.PositionId.Value] : null,
		};

		switch (condition.VolumeMode)
		{
			case EtoroVolumeModes.Units:
				request.Units = regMsg.Volume;
				break;
			case EtoroVolumeModes.Amount:
				request.Amount = regMsg.Volume;
				request.OrderCurrency = condition.OrderCurrency.ThrowIfEmpty(nameof(condition.OrderCurrency)).ToLowerInvariant();
				break;
			case EtoroVolumeModes.Contracts:
				request.Contracts = regMsg.Volume;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(condition.VolumeMode), condition.VolumeMode, null);
		}

		var result = await _rest.PlaceOrder(IsDemo, request, cancellationToken);
		if (result.OrderId <= 0)
			throw new InvalidOperationException("eToro order placement returned no order id.");

		_orders[result.OrderId] = new()
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = orderType,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
			Condition = condition,
		};

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderId = result.OrderId,
			OrderStringId = result.OrderId.ToString(CultureInfo.InvariantCulture),
			SecurityId = regMsg.SecurityId,
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = orderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePortfolio(cancelMsg.PortfolioName);
		await _rest.CancelOrder(IsDemo, GetOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		EnsurePortfolio(statusMsg.PortfolioName);
		var portfolio = (await _rest.GetPortfolio(IsDemo, cancellationToken)).ClientPortfolio;
		await ProcessPortfolioOrders(portfolio, statusMsg.TransactionId, cancellationToken);

		if (statusMsg.From != null || statusMsg.To != null || statusMsg.Count != null || statusMsg.IsHistoryOnly())
		{
			var from = statusMsg.From?.UtcKind() ?? DateTime.UtcNow.AddDays(-30);
			var to = statusMsg.To?.UtcKind();
			var left = statusMsg.Count ?? long.MaxValue;
			var page = 1;
			var pageSize = (int)Math.Min(1000, left);

			while (left > 0)
			{
				var history = await _rest.GetTradeHistory(IsDemo, from, page, pageSize, cancellationToken);
				await EnsureInstruments((history ?? []).Where(t => t != null).Select(t => t.InstrumentId),
					cancellationToken);
				foreach (var trade in history ?? [])
				{
					if (to != null && trade.CloseTimestamp.UtcKind() > to.Value)
						continue;
					if (await ProcessTradeHistory(trade, statusMsg.TransactionId, cancellationToken) && --left <= 0)
						break;
				}
				if (history == null || history.Length < pageSize)
					break;
				page++;
				await IterationInterval.Delay(cancellationToken);
			}
		}

		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		EnsurePortfolio(lookupMsg.PortfolioName);
		await ProcessPortfolio((await _rest.GetPortfolio(IsDemo, cancellationToken)).ClientPortfolio,
			lookupMsg.TransactionId, true, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private async ValueTask ProcessPrivateUpdate(string eventType, EtoroPrivateUpdate update,
		CancellationToken cancellationToken)
	{
		if (update.OrderId > 0 && _orders.ContainsKey(update.OrderId))
		{
			await ProcessPrivateOrder(eventType, update, cancellationToken);
		}
		else if (update.OrderId > 0 && _orderStatusSubscriptionId != 0)
		{
			try
			{
				var info = await _rest.GetOrderInfo(IsDemo, update.OrderId, cancellationToken);
				await ProcessOrderInfo(info, 0, cancellationToken);
			}
			catch (HttpRequestException ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
		}

		if (_portfolioSubscriptionId != 0)
		{
			try
			{
				var portfolio = (await _rest.GetPortfolio(IsDemo, cancellationToken)).ClientPortfolio;
				await ProcessPortfolio(portfolio, _portfolioSubscriptionId, false, cancellationToken);
			}
			catch (HttpRequestException ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessOrderInfo(EtoroOrderInfoResponse info, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (info == null || info.OrderId <= 0)
			return;

		_orders.TryGetValue(info.OrderId, out var tracker);
		var origin = originalTransactionId != 0 ? originalTransactionId : tracker?.TransactionId ?? _orderStatusSubscriptionId;
		if (origin == 0)
			return;

		var state = info.Status == null
			? OrderStates.Pending
			: info.Status.Id.ToOrderState(info.Status.ErrorCode);
		if (tracker == null && (info.Action == null || info.Transaction == null || info.Asset == null ||
			(info.Asset.InstrumentId <= 0 && info.Asset.Symbol.IsEmpty())))
			return;

		var volumeMode = tracker?.Condition?.VolumeMode ?? GetVolumeMode(info);
		var volume = volumeMode switch
		{
			EtoroVolumeModes.Amount => info.RequestedAmount ?? tracker?.Volume ?? 0,
			EtoroVolumeModes.Contracts => info.RequestedContracts ?? tracker?.Volume ?? 0,
			_ => info.RequestedUnits ?? tracker?.Volume ?? 0,
		};
		var executedVolume = (info.PositionExecutions ?? [])
			.Where(e => e?.OpeningData != null)
			.Sum(e => volumeMode == EtoroVolumeModes.Contracts
				? e.OpeningData.Contracts ?? e.OpeningData.Units ?? 0
				: e.OpeningData.Units ?? e.OpeningData.Contracts ?? 0);
		var securityId = tracker?.SecurityId ?? GetSecurityId(info.Asset.InstrumentId, info.Asset.Symbol);
		var side = tracker?.Side ?? info.Transaction.Value.ToSide();
		OrderTypes? orderType = tracker?.OrderType ?? info.Type switch
		{
			EtoroNativeOrderTypes.Market => OrderTypes.Market,
			EtoroNativeOrderTypes.MarketIfTouched => OrderTypes.Limit,
			_ => null,
		};

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = originalTransactionId == 0 ? tracker?.TransactionId ?? 0 : 0,
			OrderId = info.OrderId,
			OrderStringId = info.OrderId.ToString(CultureInfo.InvariantCulture),
			SecurityId = securityId,
			PortfolioName = tracker?.PortfolioName ?? PortfolioName,
			Side = side,
			OrderType = orderType,
			OrderPrice = tracker?.Price ?? info.RequestedTriggerRate ?? 0,
			OrderVolume = volume,
			Balance = state is OrderStates.Active or OrderStates.Pending
				? volumeMode == EtoroVolumeModes.Amount ? volume : Math.Max(0, volume - executedVolume)
				: 0,
			OrderState = state,
			ServerTime = (info.LastUpdate == default ? info.RequestTime : info.LastUpdate).UtcKind(),
			Condition = tracker?.Condition ?? ToOrderCondition(info),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(info.Status?.ErrorMessage.IsEmpty($"eToro order {info.OrderId} failed."))
				: null,
		}, cancellationToken);

		var isOpenOrder = info.Action switch
		{
			EtoroOrderActions.Open => true,
			EtoroOrderActions.Close => false,
			_ => !(tracker?.Condition?.PositionId is > 0),
		};
		if (!isOpenOrder)
			return;

		foreach (var execution in info.PositionExecutions ?? [])
		{
			var data = execution.OpeningData;
			if (data == null || data.AveragePrice <= 0)
				continue;
			var executed = volumeMode == EtoroVolumeModes.Contracts
				? data.Contracts ?? data.Units ?? 0
				: data.Units ?? data.Contracts ?? 0;
			if (executed <= 0)
				continue;
			var executionKey = $"{info.OrderId.ToString(CultureInfo.InvariantCulture)}:" +
				execution.PositionId.ToString(CultureInfo.InvariantCulture);
			var previous = _executionStates.TryGetValue(executionKey, out var known) ? known : default;
			if (executed <= previous.volume)
				continue;
			var commission = data.Fees + data.Taxes;
			_executionStates[executionKey] = (executed, commission, 0);
			var tradeVolume = executed - previous.volume;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = origin,
				OrderId = info.OrderId,
				OrderStringId = info.OrderId.ToString(CultureInfo.InvariantCulture),
				TradeId = execution.PositionId,
				TradeStringId = data.PriceId > 0
					? data.PriceId.ToString(CultureInfo.InvariantCulture)
					: execution.PositionId.ToString(CultureInfo.InvariantCulture),
				SecurityId = securityId,
				PortfolioName = tracker?.PortfolioName ?? PortfolioName,
				Side = side,
				TradePrice = data.AveragePrice,
				TradeVolume = tradeVolume,
				Commission = commission - previous.commission,
				ServerTime = (data.ExecutionTime == default ? data.OpenTime : data.ExecutionTime).UtcKind(),
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessPrivateOrder(string eventType, EtoroPrivateUpdate update,
		CancellationToken cancellationToken)
	{
		if (!_orders.TryGetValue(update.OrderId, out var tracker))
			return;
		var origin = tracker.TransactionId;

		var volumeMode = tracker.Condition?.VolumeMode ?? EtoroVolumeModes.Units;
		var requested = volumeMode == EtoroVolumeModes.Contracts ? update.RequestedLots : update.RequestedUnits;
		var executed = volumeMode == EtoroVolumeModes.Contracts ? update.ExecutedLots : update.ExecutedUnits;
		var failed = update.ErrorCode != 0;
		var state = update.StatusId == EtoroOrderStatusIds.Unknown
			? failed ? OrderStates.Failed : executed > 0 && (requested <= 0 || executed >= requested)
				? OrderStates.Done : OrderStates.Active
			: update.StatusId.ToOrderState(update.ErrorCode);
		var serverTime = (update.RequestOccurred == default ? update.OpenDateTime : update.RequestOccurred).UtcKind();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = tracker.TransactionId,
			OrderId = update.OrderId,
			OrderStringId = update.OrderId.ToString(CultureInfo.InvariantCulture),
			SecurityId = tracker.SecurityId,
			PortfolioName = tracker.PortfolioName,
			Side = tracker.Side,
			OrderType = tracker.OrderType,
			OrderPrice = tracker.Price,
			OrderVolume = tracker.Volume,
			Balance = state is OrderStates.Active or OrderStates.Pending
				? volumeMode == EtoroVolumeModes.Amount ? tracker.Volume : Math.Max(0, tracker.Volume - executed)
				: 0,
			OrderState = state,
			ServerTime = serverTime,
			Condition = tracker.Condition,
			Error = failed ? new InvalidOperationException(update.ErrorMessage.IsEmpty(
				$"eToro private event {eventType} failed with code {update.ErrorCode}.")) : null,
		}, cancellationToken);

		if (executed <= 0 || update.EndRate <= 0)
			return;
		var positionId = update.PositionId > 0
			? update.PositionId
			: update.PendingClosePositionIds?.Length == 1 ? update.PendingClosePositionIds[0] : 0;
		var executionKey = $"{update.OrderId.ToString(CultureInfo.InvariantCulture)}:" +
			positionId.ToString(CultureInfo.InvariantCulture);
		var previous = _executionStates.TryGetValue(executionKey, out var known) ? known : default;
		if (executed <= previous.volume)
			return;
		var commission = update.TotalExternalFees + update.TotalExternalTaxes;
		_executionStates[executionKey] = (executed, commission, update.NetProfit);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = origin,
			OrderId = update.OrderId,
			OrderStringId = update.OrderId.ToString(CultureInfo.InvariantCulture),
			TradeId = positionId > 0 ? positionId : null,
			TradeStringId = update.RequestGuid?.ToString(),
			SecurityId = tracker.SecurityId,
			PortfolioName = tracker.PortfolioName,
			Side = tracker.Side,
			TradePrice = update.EndRate,
			TradeVolume = executed - previous.volume,
			Commission = commission - previous.commission,
			PnL = update.NetProfit - previous.pnl,
			ServerTime = serverTime,
		}, cancellationToken);
	}

	private async ValueTask ProcessPortfolio(EtoroClientPortfolio portfolio, long originalTransactionId,
		bool isPortfolioMessage, CancellationToken cancellationToken)
	{
		if (portfolio == null)
			return;
		await EnsureInstruments((portfolio.Positions ?? []).Where(p => p != null).Select(p => p.InstrumentId),
			cancellationToken);
		if (isPortfolioMessage)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				BoardCode = EtoroExtensions.BoardCode,
			}, cancellationToken);
		}

		var blocked = (portfolio.Orders ?? []).Where(o => o != null).Sum(o => o.Amount) +
			(portfolio.OrdersForOpen ?? []).Where(o => o != null).Sum(o => o.Amount);
		var unrealized = portfolio.UnrealizedPnL != 0
			? portfolio.UnrealizedPnL
			: (portfolio.Positions ?? []).Where(p => p != null).Sum(p => p.UnrealizedPnl?.PnL ?? 0);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, portfolio.Credit + blocked, true)
		.TryAdd(PositionChangeTypes.CurrentValue, portfolio.Credit, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, unrealized, true)
		.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.USD), cancellationToken);

		var positionGroups = (portfolio.Positions ?? [])
			.Where(p => p != null && p.PositionId > 0 && p.InstrumentId > 0)
			.GroupBy(p => p.InstrumentId)
			.ToArray();
		var currentInstruments = positionGroups.Select(g => g.Key).ToHashSet();
		var previousInstruments = _portfolioInstruments.CopyAndClear();
		foreach (var instrumentId in currentInstruments)
			_portfolioInstruments.Add(instrumentId);

		foreach (var instrumentId in previousInstruments.Where(id => !currentInstruments.Contains(id)))
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = GetSecurityId(instrumentId),
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, 0m, true)
			.TryAdd(PositionChangeTypes.AveragePrice, 0m, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, 0m, true), cancellationToken);
		}

		foreach (var positions in positionGroups)
		{
			await ProcessPosition(positions, originalTransactionId, cancellationToken);
		}
	}

	private ValueTask ProcessPosition(IEnumerable<EtoroPosition> positions, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var items = positions?.Where(p => p != null).ToArray() ?? [];
		if (items.Length == 0)
			return default;
		var units = items.Sum(p => p.IsBuy ? p.Units : -p.Units);
		var cost = items.Sum(p => (p.IsBuy ? p.Units : -p.Units) * p.OpenRate);
		var averagePrice = units == 0 ? (decimal?)null : cost / units;
		var currentPrice = items.Select(p => p.UnrealizedPnl?.CloseRate).FirstOrDefault(p => p != null);
		var serverTime = items.Max(p => p.UnrealizedPnl?.Timestamp ?? default);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			SecurityId = GetSecurityId(items[0].InstrumentId),
			ServerTime = serverTime == default ? DateTime.UtcNow : serverTime.UtcKind(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, units, true)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, currentPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, items.Sum(p => p.UnrealizedPnl?.PnL ?? 0), true), cancellationToken);
	}

	private async ValueTask ProcessPortfolioOrders(EtoroClientPortfolio portfolio, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (portfolio == null)
			return;
		await EnsureInstruments(
			(portfolio.Orders ?? []).Where(o => o != null).Select(o => o.InstrumentId)
				.Concat((portfolio.OrdersForOpen ?? []).Where(o => o != null).Select(o => o.InstrumentId))
				.Concat((portfolio.OrdersForClose ?? []).Where(o => o != null).Select(o => o.InstrumentId))
				.Concat((portfolio.OrdersForCloseMultiple ?? []).Where(o => o != null).Select(o => o.InstrumentId))
				.Concat((portfolio.Positions ?? []).Where(p => p != null).Select(p => p.InstrumentId)),
			cancellationToken);
		var sent = new HashSet<long>();
		foreach (var order in (portfolio.Orders ?? []).Where(o => o != null))
		{
			if (sent.Add(order.OrderId))
				await ProcessWorkingOrder(order, originalTransactionId, cancellationToken);
		}
		foreach (var order in (portfolio.OrdersForOpen ?? []).Where(o => o != null))
		{
			if (sent.Add(order.OrderId))
				await ProcessPendingOpenOrder(order, originalTransactionId, cancellationToken);
		}
		var positions = (portfolio.Positions ?? []).Where(p => p != null).ToDictionary(p => p.PositionId);
		foreach (var order in (portfolio.OrdersForClose ?? []).Where(o => o != null))
		{
			if (sent.Add(order.OrderId))
				await ProcessPendingCloseOrder(order,
					positions.TryGetValue(order.PositionId, out var position) ? [position] : [],
					originalTransactionId, cancellationToken);
		}
		foreach (var order in (portfolio.OrdersForCloseMultiple ?? []).Where(o => o != null))
		{
			if (sent.Add(order.OrderId))
			{
				var affected = order.PendingClosePositionIds?
					.Select(positions.GetValueOrDefault)
					.Where(p => p != null)
					.ToArray() ?? [];
				await ProcessPendingCloseOrder(order, affected, originalTransactionId, cancellationToken);
			}
		}
		foreach (var position in (portfolio.Positions ?? []).Where(p => p != null))
		{
			if (position.PositionId > 0)
				await ProcessPositionOrder(position, position.OrderId > 0 && sent.Add(position.OrderId),
					originalTransactionId, cancellationToken);
		}
	}

	private ValueTask ProcessWorkingOrder(EtoroWorkingOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOrder(originalTransactionId, order.OrderId, GetSecurityId(order.InstrumentId),
			order.IsBuy ? Sides.Buy : Sides.Sell, OrderTypes.Limit, order.Rate,
			order.Units > 0 ? order.Units : order.Amount, OrderStates.Active, order.OpenDateTime,
			new()
			{
				Leverage = Math.Max(1, order.Leverage),
				VolumeMode = order.Units > 0 ? EtoroVolumeModes.Units : EtoroVolumeModes.Amount,
				StopLossRate = order.StopLossRate,
				TakeProfitRate = order.TakeProfitRate,
				IsTrailingStopLoss = order.IsTslEnabled,
			}, cancellationToken);

	private ValueTask ProcessPendingOpenOrder(EtoroOrderForOpen order, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOrder(originalTransactionId, order.OrderId, GetSecurityId(order.InstrumentId),
			order.IsBuy ? Sides.Buy : Sides.Sell, OrderTypes.Market, 0,
			order.AmountInUnits > 0 ? order.AmountInUnits : order.Amount, OrderStates.Active,
			order.LastUpdate == default ? order.OpenDateTime : order.LastUpdate,
			new()
			{
				Leverage = Math.Max(1, order.Leverage),
				VolumeMode = order.AmountInUnits > 0 ? EtoroVolumeModes.Units : EtoroVolumeModes.Amount,
				StopLossRate = order.StopLossRate,
				TakeProfitRate = order.TakeProfitRate,
				IsTrailingStopLoss = order.IsTslEnabled,
			}, cancellationToken);

	private ValueTask ProcessPendingCloseOrder(EtoroPendingOrder order, IEnumerable<EtoroPosition> positions,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var affected = positions?.Where(p => p != null).ToArray() ?? [];
		if (affected.Length == 0 || affected.Any(p => p.IsBuy != affected[0].IsBuy))
			return default;
		var volume = order is EtoroOrderForClose close ? close.UnitsToDeduct :
			order is EtoroOrderForCloseMultiple multiple ? multiple.UnitsToDeduct : 0;
		if (volume <= 0)
			volume = affected.Sum(p => p.Units);
		var instrumentId = order.InstrumentId > 0 ? order.InstrumentId : affected[0].InstrumentId;
		return SendOrder(originalTransactionId, order.OrderId, GetSecurityId(instrumentId),
			affected[0].IsBuy ? Sides.Sell : Sides.Buy, OrderTypes.Market, 0, volume,
			OrderStates.Active, order.LastUpdate == default ? order.OpenDateTime : order.LastUpdate,
			new() { PositionId = affected.Length == 1 ? affected[0].PositionId : null }, cancellationToken);
	}

	private async ValueTask ProcessPositionOrder(EtoroPosition position, bool sendOrderInfo,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var securityId = GetSecurityId(position.InstrumentId);
		var side = position.IsBuy ? Sides.Buy : Sides.Sell;
		var serverTime = position.OpenDateTime == default ? DateTime.UtcNow : position.OpenDateTime.UtcKind();
		if (sendOrderInfo)
		{
			await SendOrder(originalTransactionId, position.OrderId, securityId, side, OrderTypes.Market,
				position.OpenRate, position.Units, OrderStates.Done, serverTime,
				new()
				{
					Leverage = Math.Max(1, position.Leverage),
					StopLossRate = position.StopLossRate,
					TakeProfitRate = position.TakeProfitRate,
					IsTrailingStopLoss = position.IsTslEnabled,
				}, cancellationToken);
		}

		if (position.PositionId <= 0 || securityId.SecurityCode.IsEmpty() || position.Units <= 0 || position.OpenRate <= 0)
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			OrderId = position.OrderId > 0 ? position.OrderId : null,
			OrderStringId = position.OrderId > 0 ? position.OrderId.ToString(CultureInfo.InvariantCulture) : null,
			TradeId = position.PositionId,
			TradeStringId = position.PositionId.ToString(CultureInfo.InvariantCulture),
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = side,
			TradePrice = position.OpenRate,
			TradeVolume = position.Units,
			Commission = position.TotalFees,
			ServerTime = serverTime,
		}, cancellationToken);
	}

	private ValueTask SendOrder(long originalTransactionId, long orderId, SecurityId securityId, Sides side,
		OrderTypes orderType, decimal price, decimal volume, OrderStates state, DateTime serverTime,
		EtoroOrderCondition condition, CancellationToken cancellationToken)
	{
		if (orderId <= 0 || securityId.SecurityCode.IsEmpty())
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			OrderId = orderId,
			OrderStringId = orderId.ToString(CultureInfo.InvariantCulture),
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = side,
			OrderType = orderType,
			OrderPrice = price,
			OrderVolume = volume,
			Balance = state == OrderStates.Active ? volume : 0,
			OrderState = state,
			ServerTime = serverTime == default ? DateTime.UtcNow : serverTime.UtcKind(),
			Condition = condition,
		}, cancellationToken);
	}

	private async ValueTask<bool> ProcessTradeHistory(EtoroTradeHistoryItem trade, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (trade == null || trade.PositionId <= 0 || trade.InstrumentId <= 0 ||
			trade.CloseRate <= 0 || trade.Units <= 0)
			return false;
		var key = $"history:{trade.PositionId.ToString(CultureInfo.InvariantCulture)}:{trade.CloseTimestamp:o}:" +
			$"{trade.Units.ToString(CultureInfo.InvariantCulture)}:{trade.CloseRate.ToString(CultureInfo.InvariantCulture)}";
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			OrderId = trade.OrderId > 0 ? trade.OrderId : null,
			OrderStringId = trade.OrderId > 0 ? trade.OrderId.ToString(CultureInfo.InvariantCulture) : null,
			TradeId = trade.PositionId,
			TradeStringId = key,
			SecurityId = GetSecurityId(trade.InstrumentId),
			PortfolioName = PortfolioName,
			Side = trade.IsBuy ? Sides.Sell : Sides.Buy,
			TradePrice = trade.CloseRate,
			TradeVolume = trade.Units,
			Commission = trade.Fees,
			PnL = trade.NetProfit,
			ServerTime = trade.CloseTimestamp.UtcKind(),
		}, cancellationToken);
		return true;
	}

	private static EtoroOrderCondition ToOrderCondition(EtoroOrderInfoResponse info)
		=> new()
		{
			SettlementType = info.Asset?.SettlementType ?? EtoroSettlementTypes.Real,
			Leverage = Math.Max(1, info.Asset?.Leverage ?? 1),
			VolumeMode = GetVolumeMode(info),
			OrderCurrency = info.OrderCurrency.IsEmpty("USD"),
			StopLossRate = info.OpenStopLossRate,
			TakeProfitRate = info.OpenTakeProfitRate,
			IsTrailingStopLoss = info.StopLossType == EtoroStopLossTypes.Trailing,
			PositionId = info.PositionsToClose?.FirstOrDefault(),
		};

	private static EtoroVolumeModes GetVolumeMode(EtoroOrderInfoResponse info)
		=> info.RequestedAmount != null ? EtoroVolumeModes.Amount :
			info.RequestedContracts != null ? EtoroVolumeModes.Contracts : EtoroVolumeModes.Units;

	private void EnsurePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException($"eToro adapter is connected to '{PortfolioName}', not '{portfolioName}'.");
	}

	private static long GetOrderId(long? numericId, string stringId, long transactionId)
	{
		if (numericId is > 0)
			return numericId.Value;
		if (long.TryParse(stringId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
			return parsed;
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
	}
}
