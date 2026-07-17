namespace StockSharp.CapitalFutures;

public partial class CapitalFuturesMessageAdapter
{
	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
		=> SerializeOrderAsync(() => RegisterOrderCoreAsync(regMsg, cancellationToken), cancellationToken);

	private async ValueTask RegisterOrderCoreAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingEnabled();
		var instrument = await ResolveInstrumentAsync(regMsg.SecurityId,
			regMsg.SecurityType, cancellationToken);
		if (instrument.SecurityType is not SecurityTypes.Future and not SecurityTypes.Option)
			throw new NotSupportedException("Capital Futures routes domestic futures and options only.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException("Capital Futures supports limit and market orders through this adapter.");
		var condition = regMsg.Condition as CapitalFuturesOrderCondition ?? new();
		if (condition.PriceType is CapitalFuturesPriceTypes.Market or CapitalFuturesPriceTypes.MarketWithProtection)
			orderType = OrderTypes.Market;
		if (orderType == OrderTypes.Limit && condition.PriceType == CapitalFuturesPriceTypes.Auto &&
			regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price,
				"A positive price is required for a Capital Futures limit order.");
		var volume = ToNativeVolume(regMsg.Volume, nameof(regMsg.Volume));
		var account = ResolveAccount(regMsg.PortfolioName);
		var timeInForce = regMsg.TimeInForce ?? TimeInForce.PutInQueue;
		_ = timeInForce.ToTradeType();
		if (orderType == OrderTypes.Market && timeInForce == TimeInForce.PutInQueue)
			throw new InvalidOperationException(
				"Capital Futures market and market-with-protection orders require IOC or FOK.");

		var response = await _client.PlaceOrderAsync(new()
		{
			TransactionId = regMsg.TransactionId,
			Account = account,
			Symbol = instrument.Symbol,
			SecurityType = instrument.SecurityType,
			Side = regMsg.Side,
			OrderType = orderType,
			TimeInForce = timeInForce,
			Price = regMsg.Price,
			Volume = volume,
			PositionEffect = condition.PositionEffect,
			PriceType = condition.PriceType,
			IsDayTrade = condition.IsDayTrade,
			IsPreOrder = condition.IsPreOrder,
		}, cancellationToken);

		var orderId = response.SequenceId.ThrowIfEmpty(nameof(response.SequenceId));
		var tracker = new CapitalTrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			OrderId = orderId,
			Account = account,
			SecurityId = instrument.ToSecurityId(),
			SecurityType = instrument.SecurityType,
			Side = regMsg.Side,
			OrderType = orderType,
			TimeInForce = timeInForce,
			Price = regMsg.Price,
			Volume = volume,
			Balance = volume,
			State = OrderStates.Pending,
			ServerTime = response.ServerTime,
			Condition = condition,
		};
		CacheOrder(orderId, tracker);
		_transactionOrders[regMsg.TransactionId] = orderId;
		await SendTrackedOrder(tracker, regMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> SerializeOrderAsync(() => ReplaceOrderCoreAsync(replaceMsg, cancellationToken), cancellationToken);

	private async ValueTask ReplaceOrderCoreAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingEnabled();
		_ = ResolveAccount(replaceMsg.PortfolioName);
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId);
		if (!_orders.TryGetValue(orderId, out var tracker))
			throw new InvalidOperationException($"Capital Futures order '{orderId}' is not present in the local order cache.");

		var newPrice = replaceMsg.Price > 0 ? replaceMsg.Price : tracker.Price;
		var hasPriceChange = newPrice != tracker.Price;
		var hasVolumeChange = replaceMsg.Volume > 0 && replaceMsg.Volume != tracker.Volume;
		if (!hasPriceChange && !hasVolumeChange)
			throw new InvalidOperationException("Capital Futures replacement must change the price or quantity.");

		var decrease = 0;
		if (hasVolumeChange)
		{
			var newVolume = ToNativeVolume(replaceMsg.Volume, nameof(replaceMsg.Volume));
			var filled = tracker.Volume - tracker.Balance;
			if (newVolume > tracker.Volume)
				throw new NotSupportedException("Capital Futures can decrease an existing order quantity but cannot increase it.");
			if (newVolume < filled)
				throw new InvalidOperationException(
					$"Replacement quantity {newVolume} is below already filled quantity {filled}.");
			decrease = decimal.ToInt32(tracker.Volume - newVolume);
		}

		if (hasPriceChange)
		{
			TrackOrderCommand(tracker, replaceMsg.TransactionId);
			try
			{
				await _client.ReplacePriceAsync(tracker.Account, orderId, newPrice,
					replaceMsg.TimeInForce ?? tracker.TimeInForce, cancellationToken);
			}
			catch
			{
				UntrackOrderCommand(tracker, replaceMsg.TransactionId);
				throw;
			}
		}

		if (decrease > 0)
		{
			TrackOrderCommand(tracker, replaceMsg.TransactionId);
			try
			{
				await _client.DecreaseOrderAsync(tracker.Account, orderId, decrease, cancellationToken);
			}
			catch
			{
				UntrackOrderCommand(tracker, replaceMsg.TransactionId);
				throw;
			}
		}

		tracker.State = OrderStates.Pending;
		tracker.ServerTime = CurrentTime;
		_transactionOrders[replaceMsg.TransactionId] = orderId;
		await SendTrackedOrder(tracker, replaceMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
		=> SerializeOrderAsync(() => CancelOrderCoreAsync(cancelMsg, cancellationToken), cancellationToken);

	private async ValueTask CancelOrderCoreAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingEnabled();
		var orderId = ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OriginalTransactionId);
		if (!_orders.TryGetValue(orderId, out var tracker))
			throw new InvalidOperationException($"Capital Futures order '{orderId}' is not present in the local order cache.");
		var account = ResolveAccount(cancelMsg.PortfolioName);
		TrackOrderCommand(tracker, cancelMsg.TransactionId);
		try
		{
			await _client.CancelOrderAsync(account, orderId, cancellationToken);
		}
		catch
		{
			UntrackOrderCommand(tracker, cancelMsg.TransactionId);
			throw;
		}
		_transactionOrders[cancelMsg.TransactionId] = orderId;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingEnabled();
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		var left = statusMsg.Count ?? long.MaxValue;
		foreach (var tracker in _orders.Values.Distinct().OrderBy(item => item.ServerTime))
		{
			if (!IsOrderMatch(tracker, statusMsg))
				continue;
			await SendTrackedOrder(tracker, statusMsg.TransactionId, true, cancellationToken);
			if (--left <= 0)
				break;
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
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingEnabled();
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				_portfolioFilter = null;
			}
			return;
		}

		await SendPortfolioSnapshot(lookupMsg.TransactionId, lookupMsg.PortfolioName, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_portfolioFilter = lookupMsg.PortfolioName;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private ValueTask OnOrder(CapitalOrderReport report, CancellationToken cancellationToken)
		=> SerializeOrderAsync(() => report.ReportType == CapitalReportTypes.Trade
			? ProcessTrade(report, cancellationToken)
			: ProcessOrder(report, cancellationToken), cancellationToken);

	private async ValueTask ProcessOrder(CapitalOrderReport report,
		CancellationToken cancellationToken)
	{
		var tracker = ResolveTracker(report);
		if (tracker == null)
			return;
		if (report.PositionEffect != CapitalFuturesPositionEffects.Auto)
			tracker.Condition.PositionEffect = report.PositionEffect;
		tracker.Condition.PriceType = report.PriceType;
		tracker.Condition.IsDayTrade = report.IsDayTrade;
		tracker.Condition.IsPreOrder = report.IsPreOrder;
		tracker.TimeInForce = report.TimeInForce;

		switch (report.ReportType)
		{
			case CapitalReportTypes.New:
				if (report.Volume > 0)
				{
					tracker.Volume = report.Volume;
					tracker.Balance = report.Volume;
				}
				tracker.State = report.IsError ? OrderStates.Failed : OrderStates.Active;
				break;
			case CapitalReportTypes.Cancel:
			case CapitalReportTypes.ExchangeCancel:
				if (!report.IsError)
					tracker.Balance = 0;
				tracker.State = report.IsError ? tracker.State : OrderStates.Done;
				break;
			case CapitalReportTypes.Decrease:
				if (!report.IsError && report.Volume > 0)
				{
					tracker.Volume = Math.Max(0, tracker.Volume - report.Volume);
					tracker.Balance = Math.Max(0, tracker.Balance - report.Volume);
				}
				tracker.State = OrderStates.Active;
				break;
			case CapitalReportTypes.Replace:
				if (!report.IsError && report.Price > 0)
					tracker.Price = report.Price;
				tracker.State = OrderStates.Active;
				break;
			case CapitalReportTypes.ReplaceAndDecrease:
				if (!report.IsError)
				{
					if (report.Price > 0)
						tracker.Price = report.Price;
					if (report.Volume > 0)
					{
						tracker.Volume = Math.Max(0, tracker.Volume - report.Volume);
						tracker.Balance = Math.Max(0, tracker.Balance - report.Volume);
					}
				}
				tracker.State = OrderStates.Active;
				break;
			default:
				tracker.State = report.IsError ? OrderStates.Failed : tracker.State;
				break;
		}
		tracker.ServerTime = report.ServerTime;
		await SendTrackedOrder(tracker,
			ResolveOrderReportOrigin(tracker, report.ReportType),
			false, cancellationToken, report.Error, report.IsError);
	}

	private async ValueTask ProcessTrade(CapitalOrderReport report,
		CancellationToken cancellationToken)
	{
		var tracker = ResolveTracker(report);
		if (tracker == null || report.Volume <= 0)
			return;
		var tradeId = report.TradeId.IsEmpty(
			$"{report.OrderId}:{report.ServerTime.Ticks}:{report.Price}:{report.Volume}");
		if (!_tradeIds.TryAdd($"{report.OrderId}|{tradeId}"))
			return;

		tracker.Balance = Math.Max(0, tracker.Balance - report.Volume);
		tracker.State = tracker.Balance == 0 ? OrderStates.Done : OrderStates.Active;
		tracker.ServerTime = report.ServerTime;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = tracker.TransactionId != 0
				? tracker.TransactionId
				: _orderStatusSubscriptionId,
			OrderStringId = tracker.OrderId,
			TradeStringId = tradeId,
			SecurityId = tracker.SecurityId,
			PortfolioName = tracker.Account,
			Side = tracker.Side,
			TradePrice = report.Price,
			TradeVolume = report.Volume,
			ServerTime = report.ServerTime == default ? CurrentTime : report.ServerTime,
		}, cancellationToken);

		await SendTrackedOrder(tracker,
			tracker.TransactionId != 0 ? tracker.TransactionId : _orderStatusSubscriptionId,
			false, cancellationToken);
	}

	private CapitalTrackedOrder ResolveTracker(CapitalOrderReport report)
	{
		if (report == null || report.OrderId.IsEmpty())
			return null;
		if (!_orders.TryGetValue(report.OrderId, out var tracker) &&
			!report.KeyNumber.IsEmpty())
			_orders.TryGetValue(report.KeyNumber, out tracker);
		if (tracker == null && !report.SequenceId.IsEmpty())
			_orders.TryGetValue(report.SequenceId, out tracker);

		if (tracker == null)
		{
			tracker = new()
			{
				OrderId = report.OrderId,
				Account = report.Account,
				SecurityId = new SecurityId { SecurityCode = report.Symbol, BoardCode = "TAIFEX" },
				SecurityType = report.SecurityType,
				Side = report.Side,
				OrderType = report.OrderType,
				TimeInForce = report.TimeInForce,
				Price = report.Price,
				Volume = report.Volume,
				Balance = report.Volume,
				State = OrderStates.Pending,
				ServerTime = report.ServerTime,
				Condition = new()
				{
					PositionEffect = report.PositionEffect,
					PriceType = report.PriceType,
					IsDayTrade = report.IsDayTrade,
					IsPreOrder = report.IsPreOrder,
				},
			};
		}

		CacheOrder(report.OrderId, tracker);
		if (!report.KeyNumber.IsEmpty())
			CacheOrder(report.KeyNumber, tracker);
		if (!report.SequenceId.IsEmpty())
			CacheOrder(report.SequenceId, tracker);
		return tracker;
	}

	private ValueTask SendTrackedOrder(CapitalTrackedOrder tracker, long originId,
		bool isLookup, CancellationToken cancellationToken, string error = null, bool isError = false)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = isLookup ? tracker.TransactionId : 0,
			OrderStringId = tracker.OrderId,
			SecurityId = tracker.SecurityId,
			PortfolioName = tracker.Account,
			OrderType = tracker.OrderType,
			Side = tracker.Side,
			TimeInForce = tracker.TimeInForce,
			OrderPrice = tracker.Price,
			OrderVolume = tracker.Volume,
			Balance = tracker.Balance,
			OrderState = tracker.State,
			ServerTime = tracker.ServerTime == default ? CurrentTime : tracker.ServerTime,
			Condition = tracker.Condition,
			Error = isError ? new InvalidOperationException(error.IsEmpty("Capital Futures rejected the order.")) : null,
		}, cancellationToken);

	private async ValueTask SendPortfolioSnapshot(long originId, string portfolioName,
		CancellationToken cancellationToken)
	{
		var account = ResolveAccount(portfolioName);
		var snapshot = await _client.GetPortfolioAsync(cancellationToken);
		var portfolio = snapshot.Portfolio;
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originId,
			PortfolioName = account,
			BoardCode = "TAIFEX",
			Currency = portfolio.Currency.ToCurrency() ?? CurrencyTypes.TWD,
		}, cancellationToken);

		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originId,
			PortfolioName = account,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, portfolio.Available ?? portfolio.Equity, true)
		.TryAdd(PositionChangeTypes.BlockedValue, portfolio.InitialMargin, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, portfolio.RealizedPnL, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, portfolio.UnrealizedPnL, true), cancellationToken);

		foreach (var position in snapshot.Positions.Where(item =>
			item.Account.IsEmpty() || item.Account.EqualsIgnoreCase(account)))
		{
			SecurityId securityId;
			try
			{
				securityId = (await ResolveInstrumentAsync(
					new SecurityId { SecurityCode = position.Symbol, BoardCode = "TAIFEX" },
					null, cancellationToken)).ToSecurityId();
			}
			catch (Exception error)
			{
				this.AddWarningLog("Capital position instrument {0} lookup failed: {1}",
					position.Symbol, error.Message);
				securityId = new() { SecurityCode = position.Symbol, BoardCode = "TAIFEX" };
			}

			var currentValue = position.Side == Sides.Sell
				? -position.CurrentValue
				: position.CurrentValue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originId,
				PortfolioName = account,
				SecurityId = securityId,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, currentValue, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true), cancellationToken);
		}
		_lastPortfolioRefresh = CurrentTime;
	}

	private void CacheOrder(string orderId, CapitalTrackedOrder tracker)
	{
		if (!orderId.IsEmpty() && tracker != null)
			_orders[orderId] = tracker;
	}

	private static void TrackOrderCommand(CapitalTrackedOrder tracker, long transactionId)
	{
		lock (tracker.PendingCommandTransactionIds)
			tracker.PendingCommandTransactionIds.Add(transactionId);
	}

	private async ValueTask SerializeOrderAsync(Func<ValueTask> action,
		CancellationToken cancellationToken)
	{
		await _orderSync.WaitAsync(cancellationToken);
		try
		{
			await action();
		}
		finally
		{
			_orderSync.Release();
		}
	}

	private static void UntrackOrderCommand(CapitalTrackedOrder tracker, long transactionId)
	{
		lock (tracker.PendingCommandTransactionIds)
		{
			var index = tracker.PendingCommandTransactionIds.LastIndexOf(transactionId);
			if (index >= 0)
				tracker.PendingCommandTransactionIds.RemoveAt(index);
		}
	}

	private long ResolveOrderReportOrigin(CapitalTrackedOrder tracker, CapitalReportTypes reportType)
	{
		if (reportType is CapitalReportTypes.Cancel or CapitalReportTypes.ExchangeCancel or
			CapitalReportTypes.Decrease or CapitalReportTypes.Replace or CapitalReportTypes.ReplaceAndDecrease)
		{
			lock (tracker.PendingCommandTransactionIds)
			{
				if (tracker.PendingCommandTransactionIds.Count > 0)
				{
					var transactionId = tracker.PendingCommandTransactionIds[0];
					tracker.PendingCommandTransactionIds.RemoveAt(0);
					return transactionId;
				}
			}
		}

		return tracker.TransactionId != 0 ? tracker.TransactionId : _orderStatusSubscriptionId;
	}

	private string ResolveOrderId(string orderId, long transactionId)
	{
		if (!orderId.IsEmpty())
			return orderId;
		if (_transactionOrders.TryGetValue(transactionId, out orderId) && !orderId.IsEmpty())
			return orderId;
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
	}

	private string ResolveAccount(string portfolioName)
	{
		var account = _client?.Account?.FullAccount
			?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(account))
			throw new InvalidOperationException(
				$"Capital Futures account '{portfolioName}' is not the configured account '{account}'.");
		return account;
	}

	private void EnsureTradingEnabled()
	{
		if (_client?.IsTradingEnabled != true)
			throw new InvalidOperationException("Capital Futures trading services are disabled by configuration.");
	}

	private static int ToNativeVolume(decimal volume, string parameterName)
	{
		if (volume <= 0 || volume != decimal.Truncate(volume) || volume > int.MaxValue)
			throw new ArgumentOutOfRangeException(parameterName, volume,
				"Capital Futures quantities must be positive whole numbers no greater than Int32.MaxValue.");
		return decimal.ToInt32(volume);
	}

	private static bool IsOrderMatch(CapitalTrackedOrder tracker, OrderStatusMessage filter)
	{
		if (filter.From is DateTime from && tracker.ServerTime < NormalizeUtc(from))
			return false;
		if (filter.To is DateTime to && tracker.ServerTime > NormalizeUtc(to))
			return false;
		return filter.PortfolioName.IsEmpty() || tracker.Account.EqualsIgnoreCase(filter.PortfolioName);
	}

	private static DateTime NormalizeUtc(DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
}
