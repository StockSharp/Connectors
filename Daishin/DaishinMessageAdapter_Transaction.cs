namespace StockSharp.Daishin;

public partial class DaishinMessageAdapter
{
	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
		=> SerializeOrderAsync(() => RegisterOrderCoreAsync(regMsg, cancellationToken), cancellationToken);

	private async ValueTask RegisterOrderCoreAsync(OrderRegisterMessage message,
		CancellationToken cancellationToken)
	{
		EnsureTradingEnabled();
		var security = await ResolveSecurityAsync(message.SecurityId, message.SecurityType, cancellationToken);
		var orderType = message.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException("Daishin CYBOS Plus supports limit and market orders through this adapter.");
		if (orderType == OrderTypes.Limit && message.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Price), message.Price,
				"A positive price is required for a Daishin limit order.");
		var volume = ToNativeVolume(message.Volume, nameof(message.Volume));
		var timeInForce = message.TimeInForce ?? TimeInForce.PutInQueue;
		_ = timeInForce.ToNativeTimeInForce();
		var condition = message.Condition as DaishinOrderCondition ?? new();
		var account = ResolveAccount(message.PortfolioName, security.SecurityType);
		var product = GetProduct(account, security.SecurityType);
		var request = new DaishinOrderRequest
		{
			TransactionId = message.TransactionId,
			Account = account.Account,
			Product = product,
			Code = security.Code,
			SecurityType = security.SecurityType,
			Side = message.Side,
			OrderType = orderType,
			TimeInForce = timeInForce,
			Price = message.Price,
			Volume = volume,
			StockOrderMarket = condition.Market.ToNativeOrderMarket(Market),
		};
		var response = await _client.PlaceOrderAsync(request, cancellationToken);
		var orderId = response.OrderId.ThrowIfEmpty(nameof(response.OrderId));
		var tracker = new DaishinTrackedOrder
		{
			TransactionId = message.TransactionId,
			OrderId = orderId,
			Account = account.Account,
			Product = product,
			SecurityId = security.ToSecurityId(),
			SecurityType = security.SecurityType,
			Side = message.Side,
			OrderType = orderType,
			TimeInForce = timeInForce,
			Price = message.Price,
			Volume = volume,
			Balance = volume,
			State = OrderStates.Pending,
			ServerTime = response.ServerTime,
			Condition = condition,
		};
		CacheOrder(orderId, tracker);
		_transactionOrders[message.TransactionId] = orderId;
		await SendTrackedOrder(tracker, message.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> SerializeOrderAsync(() => ReplaceOrderCoreAsync(replaceMsg, cancellationToken), cancellationToken);

	private async ValueTask ReplaceOrderCoreAsync(OrderReplaceMessage message,
		CancellationToken cancellationToken)
	{
		EnsureTradingEnabled();
		var orderId = ResolveOrderId(message.OldOrderStringId, message.OriginalTransactionId);
		if (!_orders.TryGetValue(orderId, out var tracker))
			throw new InvalidOperationException($"Daishin order '{orderId}' is not present in the local order cache.");
		if (!message.PortfolioName.IsEmpty() && !message.PortfolioName.EqualsIgnoreCase(tracker.Account))
			throw new InvalidOperationException(
				$"Daishin order '{orderId}' belongs to account '{tracker.Account}', not '{message.PortfolioName}'.");

		var price = message.Price > 0 ? message.Price : tracker.Price;
		var volume = message.Volume > 0
			? ToNativeVolume(message.Volume, nameof(message.Volume))
			: ToNativeVolume(tracker.Balance, nameof(message.Volume));
		var timeInForce = message.TimeInForce ?? tracker.TimeInForce;
		_ = timeInForce.ToNativeTimeInForce();
		var request = CreateOrderRequest(message.TransactionId, tracker, price, volume, timeInForce);
		TrackOrderCommand(tracker, message.TransactionId);
		try
		{
			var response = await _client.ReplaceOrderAsync(orderId, request, cancellationToken);
			if (!response.OrderId.IsEmpty() && response.OrderId != "0")
			{
				tracker.OrderId = response.OrderId;
				CacheOrder(response.OrderId, tracker);
			}
		}
		catch
		{
			UntrackOrderCommand(tracker, message.TransactionId);
			throw;
		}

		tracker.Price = price;
		tracker.Volume = volume;
		tracker.Balance = Math.Min(tracker.Balance, volume);
		tracker.TimeInForce = timeInForce;
		tracker.State = OrderStates.Pending;
		tracker.ServerTime = CurrentTime;
		_transactionOrders[message.TransactionId] = tracker.OrderId;
		await SendTrackedOrder(tracker, message.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
		=> SerializeOrderAsync(() => CancelOrderCoreAsync(cancelMsg, cancellationToken), cancellationToken);

	private async ValueTask CancelOrderCoreAsync(OrderCancelMessage message,
		CancellationToken cancellationToken)
	{
		EnsureTradingEnabled();
		var orderId = ResolveOrderId(message.OrderStringId, message.OriginalTransactionId);
		if (!_orders.TryGetValue(orderId, out var tracker))
			throw new InvalidOperationException($"Daishin order '{orderId}' is not present in the local order cache.");
		if (!message.PortfolioName.IsEmpty() && !message.PortfolioName.EqualsIgnoreCase(tracker.Account))
			throw new InvalidOperationException(
				$"Daishin order '{orderId}' belongs to account '{tracker.Account}', not '{message.PortfolioName}'.");

		TrackOrderCommand(tracker, message.TransactionId);
		try
		{
			await _client.CancelOrderAsync(orderId,
				CreateOrderRequest(message.TransactionId, tracker, tracker.Price,
					ToNativeVolume(tracker.Balance, nameof(tracker.Balance)), tracker.TimeInForce),
				cancellationToken);
		}
		catch
		{
			UntrackOrderCommand(tracker, message.TransactionId);
			throw;
		}
		_transactionOrders[message.TransactionId] = orderId;
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

		await SendOrderSnapshot(statusMsg.TransactionId, statusMsg, cancellationToken);
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

	private ValueTask OnOrder(DaishinOrderUpdate update, CancellationToken cancellationToken)
		=> SerializeOrderAsync(() => ProcessOrderUpdate(update, cancellationToken), cancellationToken);

	private async ValueTask ProcessOrderUpdate(DaishinOrderUpdate update,
		CancellationToken cancellationToken)
	{
		var tracker = ResolveTracker(update);
		if (tracker == null)
			return;
		var originId = ResolveOrderEventOrigin(tracker, update.Event);

		if (update.Event == DaishinOrderEvents.Filled &&
			update.TradeVolume is decimal tradeVolume && tradeVolume > 0)
		{
			var tradeId = update.TradeId.IsEmpty(
				$"{tracker.OrderId}:{update.ServerTime.Ticks}:{update.TradePrice}:{tradeVolume}");
			if (_tradeIds.TryAdd($"{tracker.OrderId}|{tradeId}"))
			{
				tracker.Balance = Math.Max(0, tracker.Balance - tradeVolume);
				tracker.State = tracker.Balance == 0 ? OrderStates.Done : OrderStates.Active;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = originId,
					OrderStringId = tracker.OrderId,
					TradeStringId = tradeId,
					SecurityId = tracker.SecurityId,
					PortfolioName = tracker.Account,
					Side = tracker.Side,
					TradePrice = update.TradePrice,
					TradeVolume = tradeVolume,
					ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
				}, cancellationToken);
			}
		}
		else
		{
			if (update.Price > 0)
				tracker.Price = update.Price;
			if (update.Volume > 0)
				tracker.Volume = update.Volume;
			if (update.Balance is decimal balance)
				tracker.Balance = Math.Max(0, balance);
			tracker.OrderType = update.OrderType;
			tracker.TimeInForce = update.TimeInForce;
			tracker.State = update.Event switch
			{
				DaishinOrderEvents.Accepted => OrderStates.Active,
				DaishinOrderEvents.Replaced => OrderStates.Active,
				DaishinOrderEvents.Canceled => OrderStates.Done,
				DaishinOrderEvents.Rejected => OrderStates.Failed,
				_ => tracker.State,
			};
			if (update.Event == DaishinOrderEvents.Canceled)
				tracker.Balance = 0;
		}
		tracker.ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime;
		await SendTrackedOrder(tracker, originId, false, cancellationToken,
			update.Event == DaishinOrderEvents.Rejected ? update.Error : null);
	}

	private DaishinTrackedOrder ResolveTracker(DaishinOrderUpdate update)
	{
		if (update == null || update.OrderId.IsEmpty())
			return null;
		if (!_orders.TryGetValue(update.OrderId, out var tracker) && !update.OriginalOrderId.IsEmpty())
			_orders.TryGetValue(update.OriginalOrderId, out tracker);
		if (tracker == null)
		{
			tracker = new()
			{
				OrderId = update.OrderId,
				Account = update.Account,
				Product = update.Product,
				SecurityId = new() { SecurityCode = update.Code, BoardCode = "KRX" },
				SecurityType = update.SecurityType,
				Side = update.Side,
				OrderType = update.OrderType,
				TimeInForce = update.TimeInForce,
				Price = update.Price,
				Volume = update.Volume,
				Balance = update.Balance ?? update.Volume,
				State = OrderStates.Pending,
				ServerTime = update.ServerTime,
				Condition = new(),
			};
		}
		tracker.OrderId = update.OrderId;
		CacheOrder(update.OrderId, tracker);
		if (!update.OriginalOrderId.IsEmpty())
			CacheOrder(update.OriginalOrderId, tracker);
		return tracker;
	}

	private async ValueTask SendOrderSnapshot(long originId, OrderStatusMessage filter,
		CancellationToken cancellationToken)
	{
		var left = filter?.Count ?? long.MaxValue;
		foreach (var account in ResolveAccounts(filter?.PortfolioName))
		{
			foreach (var update in await _client.GetOpenOrdersAsync(account.Account, cancellationToken))
			{
				var tracker = ResolveTracker(update);
				if (tracker == null || filter != null && !IsOrderMatch(tracker, filter))
					continue;
				tracker.State = OrderStates.Active;
				tracker.ServerTime = update.ServerTime;
				await SendTrackedOrder(tracker, originId, true, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
				break;
		}
		_lastOrderRefresh = CurrentTime;
	}

	private ValueTask SendTrackedOrder(DaishinTrackedOrder tracker, long originId,
		bool isLookup, CancellationToken cancellationToken, string error = null)
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
			Error = error.IsEmpty() ? null : new InvalidOperationException(error),
		}, cancellationToken);

	private async ValueTask SendPortfolioSnapshot(long originId, string portfolioName,
		CancellationToken cancellationToken)
	{
		foreach (var account in ResolveAccounts(portfolioName))
		{
			var snapshot = await _client.GetPortfolioAsync(account.Account, cancellationToken);
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = originId,
				PortfolioName = account.Account,
				BoardCode = "KRX",
				Currency = CurrencyTypes.KRW,
			}, cancellationToken);

			var portfolio = snapshot.Portfolio;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originId,
				PortfolioName = account.Account,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, portfolio.CurrentValue, true)
			.TryAdd(PositionChangeTypes.BlockedValue, portfolio.BlockedValue, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, portfolio.RealizedPnL, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, portfolio.UnrealizedPnL, true), cancellationToken);

			foreach (var position in snapshot.Positions)
			{
				SecurityId securityId;
				try
				{
					securityId = (await ResolveSecurityAsync(
						new() { SecurityCode = position.Code, BoardCode = "KRX" },
						position.SecurityType, cancellationToken)).ToSecurityId();
				}
				catch (Exception lookupError)
				{
					this.AddWarningLog("Daishin position security {0} lookup failed: {1}",
						position.Code, lookupError.Message);
					securityId = new() { SecurityCode = position.Code, BoardCode = "KRX" };
				}
				var blocked = position.AvailableValue is decimal available
					? Math.Max(0, Math.Abs(position.CurrentValue) - available)
					: (decimal?)null;
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originId,
					PortfolioName = account.Account,
					SecurityId = securityId,
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, position.CurrentValue, true)
				.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
				.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true), cancellationToken);
			}
		}
		_lastPortfolioRefresh = CurrentTime;
	}

	private DaishinOrderRequest CreateOrderRequest(long transactionId,
		DaishinTrackedOrder tracker, decimal price, int volume, TimeInForce timeInForce)
		=> new()
		{
			TransactionId = transactionId,
			Account = tracker.Account,
			Product = tracker.Product,
			Code = tracker.SecurityId.SecurityCode,
			SecurityType = tracker.SecurityType,
			Side = tracker.Side,
			OrderType = tracker.OrderType,
			TimeInForce = timeInForce,
			Price = price,
			Volume = volume,
			StockOrderMarket = tracker.Condition.Market.ToNativeOrderMarket(Market),
		};

	private DaishinAccountInfo ResolveAccount(string account, SecurityTypes securityType)
	{
		var result = account.IsEmpty()
			? _client.Accounts.FirstOrDefault(item => Supports(item, securityType))
			: _client.Accounts.FirstOrDefault(item => item.Account.EqualsIgnoreCase(account));
		if (result == null)
			throw new InvalidOperationException(account.IsEmpty()
				? "Daishin CYBOS Plus returned no compatible trading account."
				: $"Daishin CYBOS Plus account '{account}' is not available.");
		_ = GetProduct(result, securityType);
		return result;
	}

	private IEnumerable<DaishinAccountInfo> ResolveAccounts(string account)
	{
		if (account.IsEmpty())
			return _client.Accounts;
		var result = _client.Accounts.FirstOrDefault(item => item.Account.EqualsIgnoreCase(account));
		return result == null
			? throw new InvalidOperationException($"Daishin CYBOS Plus account '{account}' is not available.")
			: [result];
	}

	private static bool Supports(DaishinAccountInfo account, SecurityTypes securityType)
		=> securityType is SecurityTypes.Stock or SecurityTypes.Etf
			? account.IsStockEnabled
			: account.IsDerivativesEnabled;

	private static string GetProduct(DaishinAccountInfo account, SecurityTypes securityType)
	{
		var product = securityType is SecurityTypes.Stock or SecurityTypes.Etf
			? account.StockProduct
			: account.DerivativesProduct;
		return product.ThrowIfEmpty(securityType is SecurityTypes.Stock or SecurityTypes.Etf
			? nameof(account.StockProduct)
			: nameof(account.DerivativesProduct));
	}

	private static int ToNativeVolume(decimal volume, string parameterName)
	{
		if (volume <= 0 || volume != decimal.Truncate(volume) || volume > int.MaxValue)
			throw new ArgumentOutOfRangeException(parameterName, volume,
				"Daishin quantities must be positive whole numbers no greater than Int32.MaxValue.");
		return decimal.ToInt32(volume);
	}

	private void CacheOrder(string orderId, DaishinTrackedOrder tracker)
	{
		if (!orderId.IsEmpty() && tracker != null)
			_orders[orderId] = tracker;
	}

	private static void TrackOrderCommand(DaishinTrackedOrder tracker, long transactionId)
	{
		lock (tracker.PendingCommandTransactionIds)
			tracker.PendingCommandTransactionIds.Add(transactionId);
	}

	private static void UntrackOrderCommand(DaishinTrackedOrder tracker, long transactionId)
	{
		lock (tracker.PendingCommandTransactionIds)
			tracker.PendingCommandTransactionIds.Remove(transactionId);
	}

	private long ResolveOrderEventOrigin(DaishinTrackedOrder tracker, DaishinOrderEvents orderEvent)
	{
		if (orderEvent is DaishinOrderEvents.Replaced or DaishinOrderEvents.Canceled or DaishinOrderEvents.Rejected)
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

	private void EnsureTradingEnabled()
	{
		if (_client?.IsTradingEnabled != true)
			throw new InvalidOperationException("Daishin CYBOS Plus trading services are disabled by configuration.");
	}

	private static bool IsOrderMatch(DaishinTrackedOrder tracker, OrderStatusMessage filter)
	{
		if (filter.From is DateTime from && tracker.ServerTime < NormalizeUtc(from))
			return false;
		if (filter.To is DateTime to && tracker.ServerTime > NormalizeUtc(to))
			return false;
		return filter.PortfolioName.IsEmpty() || tracker.Account.EqualsIgnoreCase(filter.PortfolioName);
	}
}
