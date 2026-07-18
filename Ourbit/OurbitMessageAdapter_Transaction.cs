namespace StockSharp.Ourbit;

public partial class OurbitMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		var section = ResolveSection(regMsg.SecurityId);
		EnsurePrivateReady(section);
		var symbol = GetSymbol(regMsg.SecurityId, section);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("A positive limit price is required.");
		var condition = regMsg.Condition as OurbitOrderCondition ?? new OurbitOrderCondition();
		var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);

		string orderId;
		DateTime serverTime;
		if (section == OurbitSections.Spot)
		{
			var nativeType = regMsg.PostOnly == true ? "LIMIT_MAKER" : orderType switch
			{
				OrderTypes.Market => "MARKET",
				_ when regMsg.TimeInForce == TimeInForce.CancelBalance => "IMMEDIATE_OR_CANCEL",
				_ when regMsg.TimeInForce == TimeInForce.MatchOrCancel => "FILL_OR_KILL",
				_ => "LIMIT",
			};
			var result = await SpotRestClient.PlaceOrderAsync(new()
			{
				Symbol = symbol,
				Side = regMsg.Side == Sides.Buy ? "BUY" : "SELL",
				Type = nativeType,
				Quantity = volume.ToWire(),
				Price = orderType == OrderTypes.Market ? null : regMsg.Price.ToWire(),
				ClientOrderId = clientOrderId,
			}, cancellationToken);
			orderId = result?.OrderId.ThrowIfEmpty("Ourbit spot order ID");
			serverTime = result.TransactionTime > 0
				? result.TransactionTime.FromMilliseconds()
				: CurrentTime;
		}
		else
		{
			OurbitFuturesProduct product;
			using (_sync.EnterScope())
				_futuresProducts.TryGetValue(symbol, out product);
			if (product is null)
				throw new InvalidOperationException($"Unknown Ourbit futures symbol '{symbol}'.");
			if (condition.Leverage < product.MinimumLeverage || condition.Leverage > product.MaximumLeverage)
				throw new ArgumentOutOfRangeException(nameof(condition.Leverage), condition.Leverage,
					$"Leverage must be between {product.MinimumLeverage} and {product.MaximumLeverage}.");
			var isClose = condition.IsReduceOnly || regMsg.PositionEffect == OrderPositionEffects.CloseOnly;
			var side = (regMsg.Side, isClose) switch
			{
				(Sides.Buy, false) => 1,
				(Sides.Buy, true) => 2,
				(Sides.Sell, false) => 3,
				(Sides.Sell, true) => 4,
				_ => throw new ArgumentOutOfRangeException(nameof(regMsg.Side), regMsg.Side, null),
			};
			var nativeType = regMsg.PostOnly == true ? 2 : orderType switch
			{
				OrderTypes.Market => 5,
				_ when regMsg.TimeInForce == TimeInForce.CancelBalance => 3,
				_ when regMsg.TimeInForce == TimeInForce.MatchOrCancel => 4,
				_ => 1,
			};
			orderId = await FuturesRestClient.PlaceOrderAsync(new()
			{
				Symbol = symbol,
				Price = orderType == OrderTypes.Market ? 0m : regMsg.Price,
				Volume = volume,
				Leverage = condition.Leverage,
				Side = side,
				Type = nativeType,
				OpenType = condition.MarginMode == MarginModes.Isolated ? 1 : 2,
				ExternalOrderId = clientOrderId,
			}, cancellationToken);
			orderId.ThrowIfEmpty("Ourbit futures order ID");
			serverTime = CurrentTime;
		}

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			PortfolioName = GetPortfolioName(section),
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = orderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderType = orderType,
			OrderState = OrderStates.Active,
			OrderStringId = orderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = regMsg.PostOnly,
			PositionEffect = regMsg.PositionEffect,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> throw new NotSupportedException(
			"Ourbit spot and futures APIs do not expose atomic order replacement.");

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var section = ResolveSection(cancelMsg.SecurityId);
		EnsurePrivateReady(section);
		var symbol = GetSymbol(cancelMsg.SecurityId, section);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() && cancelMsg.OrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("Ourbit cancellation requires an exchange order ID.");
		if (section == OurbitSections.Spot)
			await SpotRestClient.CancelOrderAsync(new()
			{
				Symbol = symbol,
				OrderId = orderId,
			}, cancellationToken);
		else
			await FuturesRestClient.CancelOrderAsync(new() { OrderId = orderId }, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(section),
			OrderStringId = orderId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException("Ourbit does not expose a close-all-positions command.");
		var hasSecurity = !cancelMsg.SecurityId.SecurityCode.IsEmpty();
		var requestedSection = !cancelMsg.SecurityId.BoardCode.IsEmpty()
			? cancelMsg.SecurityId.BoardCode.ToSection()
			: (OurbitSections?)null;

		foreach (var section in Sections.Where(section => requestedSection is null || requestedSection == section))
		{
			EnsurePrivateReady(section);
			var symbol = hasSecurity ? GetSymbol(cancelMsg.SecurityId, section) : null;
			if (section == OurbitSections.Spot)
			{
				if (!symbol.IsEmpty() && cancelMsg.Side is null)
				{
					await SpotRestClient.CancelAllOrdersAsync(symbol, cancellationToken);
					continue;
				}
				var orders = await SpotRestClient.GetOpenOrdersAsync(symbol, cancellationToken) ?? [];
				foreach (var group in orders
					.Where(order => cancelMsg.Side is null ||
						order.Side.EqualsIgnoreCase(cancelMsg.Side == Sides.Buy ? "BUY" : "SELL"))
					.GroupBy(static order => order.Symbol))
				{
					if (cancelMsg.Side is null)
						await SpotRestClient.CancelAllOrdersAsync(group.Key, cancellationToken);
					else
					{
						foreach (var order in group)
							await SpotRestClient.CancelOrderAsync(new()
							{
								Symbol = order.Symbol,
								OrderId = order.OrderId,
							}, cancellationToken);
					}
				}
			}
			else
			{
				if (!symbol.IsEmpty() && cancelMsg.Side is null)
				{
					await FuturesRestClient.CancelAllOrdersAsync(new() { Symbol = symbol }, cancellationToken);
					continue;
				}
				var orders = await FuturesRestClient.GetOpenOrdersAsync(symbol, cancellationToken) ?? [];
				foreach (var group in orders
					.Where(order => cancelMsg.Side is null || order.Side.ToSide() == cancelMsg.Side)
					.GroupBy(static order => order.Symbol))
				{
					if (cancelMsg.Side is null)
						await FuturesRestClient.CancelAllOrdersAsync(new() { Symbol = group.Key }, cancellationToken);
					else
					{
						foreach (var order in group)
							await FuturesRestClient.CancelOrderAsync(new() { OrderId = order.OrderId },
								cancellationToken);
					}
				}
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
			return;
		}
		if (_spotRestClient?.IsCredentialsAvailable != true &&
			_futuresRestClient?.IsCredentialsAvailable != true)
			throw new InvalidOperationException("Ourbit API key and secret are required for portfolio data.");
		foreach (var section in Sections)
		{
			var available = section == OurbitSections.Spot
				? _spotRestClient?.IsCredentialsAvailable == true
				: _futuresRestClient?.IsCredentialsAvailable == true;
			if (!available)
				continue;
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = GetPortfolioName(section),
				BoardCode = section.ToBoardCode(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
			return;
		}
		if (_spotRestClient?.IsCredentialsAvailable != true &&
			_futuresRestClient?.IsCredentialsAvailable != true)
			throw new InvalidOperationException("Ourbit API key and secret are required for order data.");
		OurbitSections? section = statusMsg.SecurityId.BoardCode.IsEmpty()
			? null
			: statusMsg.SecurityId.BoardCode.ToSection();
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId, section ?? ResolveSection(statusMsg.SecurityId));
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, statusMsg.From, statusMsg.To,
			cancellationToken, section);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (!statusMsg.IsHistoryOnly())
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (_spotRestClient?.IsCredentialsAvailable == true)
		{
			var account = await SpotRestClient.GetAccountAsync(cancellationToken);
			foreach (var balance in account?.Balances ?? [])
				await SendSpotBalanceAsync(balance, account.UpdateTime ?? 0,
					originalTransactionId, cancellationToken);
		}
		if (_futuresRestClient?.IsCredentialsAvailable == true)
		{
			foreach (var balance in await FuturesRestClient.GetBalancesAsync(cancellationToken) ?? [])
				await SendFuturesBalanceAsync(balance, 0, originalTransactionId, cancellationToken);
			foreach (var position in await FuturesRestClient.GetPositionsAsync(cancellationToken) ?? [])
				await SendFuturesPositionAsync(position, 0, originalTransactionId, cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, string symbol,
		DateTime? from, DateTime? to, CancellationToken cancellationToken,
		OurbitSections? requestedSection = null)
	{
		var startTime = from?.ToUniversalTime();
		var endTime = to?.ToUniversalTime();
		if ((requestedSection is null or OurbitSections.Spot) &&
			_spotRestClient?.IsCredentialsAvailable == true)
		{
			var orders = new List<OurbitSpotOrder>(
				await SpotRestClient.GetOpenOrdersAsync(symbol, cancellationToken) ?? []);
			if (!symbol.IsEmpty())
				orders.AddRange(await SpotRestClient.GetAllOrdersAsync(symbol,
					startTime is null ? null : new DateTimeOffset(startTime.Value).ToUnixTimeMilliseconds(),
					endTime is null ? null : new DateTimeOffset(endTime.Value).ToUnixTimeMilliseconds(),
					1000, cancellationToken) ?? []);
			foreach (var order in orders.Where(static order => order?.OrderId.IsEmpty() == false)
				.GroupBy(static order => order.OrderId).Select(static group => group.First())
				.OrderBy(static order => order.Time))
				await SendSpotOrderAsync(order, originalTransactionId, cancellationToken);
			if (!symbol.IsEmpty())
			{
				foreach (var fill in await SpotRestClient.GetFillsAsync(symbol,
					startTime is null ? null : new DateTimeOffset(startTime.Value).ToUnixTimeMilliseconds(),
					endTime is null ? null : new DateTimeOffset(endTime.Value).ToUnixTimeMilliseconds(),
					1000, cancellationToken) ?? [])
					await SendSpotFillAsync(fill, originalTransactionId, cancellationToken);
			}
		}

		if ((requestedSection is null or OurbitSections.Futures) &&
			_futuresRestClient?.IsCredentialsAvailable == true)
		{
			var orders = new List<OurbitFuturesOrder>(
				await FuturesRestClient.GetOpenOrdersAsync(symbol, cancellationToken) ?? []);
			var historyRequest = new OurbitFuturesHistoryRequest
			{
				Symbol = symbol,
				StartTime = startTime is null ? null : new DateTimeOffset(startTime.Value).ToUnixTimeMilliseconds(),
				EndTime = endTime is null ? null : new DateTimeOffset(endTime.Value).ToUnixTimeMilliseconds(),
				PageNumber = 1,
				PageSize = 100,
			};
			orders.AddRange(await FuturesRestClient.GetOrderHistoryAsync(historyRequest,
				cancellationToken) ?? []);
			foreach (var order in orders.Where(static order => order?.OrderId.IsEmpty() == false)
				.GroupBy(static order => order.OrderId).Select(static group => group.First())
				.OrderBy(static order => order.CreateTime))
				await SendFuturesOrderAsync(order, originalTransactionId, cancellationToken);
			foreach (var fill in await FuturesRestClient.GetFillsAsync(historyRequest,
				cancellationToken) ?? [])
				await SendFuturesFillAsync(fill, originalTransactionId, cancellationToken);
		}
	}

	private ValueTask OnSpotAccountAsync(OurbitSpotWsPrivateAccount balance,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(OurbitSections.Spot),
			SecurityId = balance.Asset.ToStockSharp(OurbitSections.Spot),
			ServerTime = balance.Time > 0 ? balance.Time.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = _portfolioSubscriptionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, balance.Balance, true)
		 .TryAdd(PositionChangeTypes.BlockedValue, balance.Frozen, true), cancellationToken);

	private ValueTask OnSpotOrderAsync(string symbol, OurbitSpotWsPrivateOrder order, long time,
		CancellationToken cancellationToken)
	{
		symbol = symbol.IsEmpty(order.Symbol);
		if (symbol.IsEmpty())
			return default;
		var state = order.Status switch
		{
			1 or 3 => OrderStates.Active,
			2 or 4 or 5 => OrderStates.Done,
			_ => OrderStates.None,
		};
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = order.CreateTime > 0 ? order.CreateTime.FromMilliseconds() :
				time > 0 ? time.FromMilliseconds() : CurrentTime,
			PortfolioName = GetPortfolioName(OurbitSections.Spot),
			OrderStringId = order.OrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = _orderStatusSubscriptionId,
			OrderPrice = order.Price,
			OrderVolume = order.Volume,
			Balance = order.RemainingVolume,
			Side = order.Side == 1 ? Sides.Buy : Sides.Sell,
			OrderType = order.OrderType == 2 ? OrderTypes.Market : OrderTypes.Limit,
			OrderState = state,
			AveragePrice = order.AveragePrice,
		}, cancellationToken);
	}

	private ValueTask OnSpotFillAsync(string symbol, OurbitSpotWsPrivateFill fill,
		CancellationToken cancellationToken)
	{
		symbol = symbol.IsEmpty(fill.Symbol);
		if (symbol.IsEmpty())
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = fill.Time > 0 ? fill.Time.FromMilliseconds() : CurrentTime,
			PortfolioName = GetPortfolioName(OurbitSections.Spot),
			OrderStringId = fill.OrderId,
			TradeStringId = fill.TradeId,
			TransactionId = ParseTransactionId(fill.ClientOrderId),
			OriginalTransactionId = _orderStatusSubscriptionId,
			TradePrice = fill.Price,
			TradeVolume = fill.Volume,
			Side = fill.Side == 1 ? Sides.Buy : Sides.Sell,
			Commission = fill.Commission,
			CommissionCurrency = fill.CommissionAsset,
		}, cancellationToken);
	}

	private ValueTask OnFuturesOrderAsync(OurbitFuturesOrder order,
		CancellationToken cancellationToken)
		=> SendFuturesOrderAsync(order, _orderStatusSubscriptionId, cancellationToken);

	private ValueTask OnFuturesFillAsync(OurbitFuturesFill fill,
		CancellationToken cancellationToken)
		=> SendFuturesFillAsync(fill, _orderStatusSubscriptionId, cancellationToken);

	private ValueTask OnFuturesBalanceAsync(OurbitFuturesBalance balance, long time,
		CancellationToken cancellationToken)
		=> SendFuturesBalanceAsync(balance, time, _portfolioSubscriptionId, cancellationToken);

	private ValueTask OnFuturesPositionAsync(OurbitFuturesPosition position, long time,
		CancellationToken cancellationToken)
		=> SendFuturesPositionAsync(position, time, _portfolioSubscriptionId, cancellationToken);

	private ValueTask SendSpotBalanceAsync(OurbitSpotBalance balance, long time,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(OurbitSections.Spot),
			SecurityId = balance.Asset.ToStockSharp(OurbitSections.Spot),
			ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, balance.Available, true)
		 .TryAdd(PositionChangeTypes.BlockedValue, balance.Locked, true), cancellationToken);

	private ValueTask SendFuturesBalanceAsync(OurbitFuturesBalance balance, long time,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var current = balance.Equity != 0 ? balance.Equity : balance.CashBalance;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(OurbitSections.Futures),
			SecurityId = balance.Currency.ToStockSharp(OurbitSections.Futures),
			ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, current, true)
		 .TryAdd(PositionChangeTypes.BlockedValue, balance.FrozenBalance, true)
		 .TryAdd(PositionChangeTypes.UnrealizedPnL, balance.UnrealizedPnl, true), cancellationToken);
	}

	private ValueTask SendFuturesPositionAsync(OurbitFuturesPosition position, long time,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var volume = position.PositionType == 2 ? -position.Volume : position.Volume;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(OurbitSections.Futures),
			SecurityId = position.Symbol.ToStockSharp(OurbitSections.Futures),
			ServerTime = position.UpdateTime > 0 ? position.UpdateTime.FromMilliseconds() :
				time > 0 ? time.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, volume, true)
		 .TryAdd(PositionChangeTypes.AveragePrice,
			position.AveragePrice != 0 ? position.AveragePrice : position.OpenAveragePrice, true)
		 .TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnl, true)
		 .TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnl, true)
		 .TryAdd(PositionChangeTypes.Leverage, position.Leverage, true), cancellationToken);
	}

	private ValueTask SendSpotOrderAsync(OurbitSpotOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = (order.UpdateTime > 0 ? order.UpdateTime : order.Time).FromMilliseconds(),
			PortfolioName = GetPortfolioName(OurbitSections.Spot),
			OrderStringId = order.OrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			OrderPrice = order.Price,
			OrderVolume = order.OriginalVolume,
			Balance = order.OriginalVolume - order.ExecutedVolume,
			Side = order.Side.EqualsIgnoreCase("BUY") ? Sides.Buy : Sides.Sell,
			OrderType = order.Type.ToOrderType(),
			OrderState = order.Status.ToOrderState(),
			AveragePrice = order.ExecutedVolume > 0
				? order.CumulativeQuoteVolume / order.ExecutedVolume
				: null,
		}, cancellationToken);

	private ValueTask SendSpotFillAsync(OurbitSpotFill fill, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = fill.Time.FromMilliseconds(),
			PortfolioName = GetPortfolioName(OurbitSections.Spot),
			OrderStringId = fill.OrderId,
			TradeStringId = fill.TradeId,
			TransactionId = ParseTransactionId(fill.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TradePrice = fill.Price,
			TradeVolume = fill.Volume,
			Side = fill.IsBuyer ? Sides.Buy : Sides.Sell,
			IsMarketMaker = fill.IsMaker,
			Commission = fill.Commission,
			CommissionCurrency = fill.CommissionAsset,
		}, cancellationToken);

	private ValueTask SendFuturesOrderAsync(OurbitFuturesOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(OurbitSections.Futures),
			ServerTime = (order.UpdateTime > 0 ? order.UpdateTime : order.CreateTime) > 0
				? (order.UpdateTime > 0 ? order.UpdateTime : order.CreateTime).FromMilliseconds()
				: CurrentTime,
			PortfolioName = GetPortfolioName(OurbitSections.Futures),
			OrderStringId = order.OrderId,
			TransactionId = ParseTransactionId(order.ExternalOrderId),
			OriginalTransactionId = originalTransactionId,
			OrderPrice = order.Price,
			OrderVolume = order.Volume,
			Balance = order.Volume - order.FilledVolume,
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderState = order.State.ToOrderState(),
			AveragePrice = order.AveragePrice,
			TimeInForce = order.OrderType.ToTimeInForce(),
			PositionEffect = order.Side is 2 or 4 ? OrderPositionEffects.CloseOnly : null,
			Commission = order.MakerFee + order.TakerFee,
			CommissionCurrency = order.FeeCurrency,
		}, cancellationToken);

	private ValueTask SendFuturesFillAsync(OurbitFuturesFill fill, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Symbol.ToStockSharp(OurbitSections.Futures),
			ServerTime = fill.Timestamp > 0 ? fill.Timestamp.FromMilliseconds() : CurrentTime,
			PortfolioName = GetPortfolioName(OurbitSections.Futures),
			OrderStringId = fill.OrderId,
			TradeId = Interlocked.Increment(ref _tradeIdSeed),
			OriginalTransactionId = originalTransactionId,
			TradePrice = fill.Price,
			TradeVolume = fill.Volume,
			Side = fill.Side.ToSide(),
			IsMarketMaker = !fill.IsTaker,
			Commission = fill.Fee,
			CommissionCurrency = fill.FeeCurrency,
		}, cancellationToken);
}
