namespace StockSharp.Shioaji;

public partial class ShioajiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		var contract = await ResolveContract(regMsg.SecurityId, regMsg.SecurityType, cancellationToken);
		var securityType = contract.ToSecurityType();
		if (securityType is not SecurityTypes.Stock and not SecurityTypes.Warrant and
			not SecurityTypes.Future and not SecurityTypes.Option)
			throw new NotSupportedException($"Shioaji cannot submit {securityType} orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException("Shioaji supports limit, market, and range-market orders.");

		var quantity = ToOrderQuantity(regMsg.Volume, nameof(regMsg.Volume));
		var condition = regMsg.Condition as ShioajiOrderCondition;
		if (condition?.CustomField?.Length > 6)
			throw new ArgumentOutOfRangeException(nameof(condition.CustomField), condition.CustomField,
				"Shioaji custom fields are limited to six characters.");
		var account = GetAccount(regMsg.PortfolioName, securityType);
		var accountSelector = new ShioajiAccountSelector
		{
			BrokerId = account.BrokerId,
			AccountId = account.AccountId,
		};
		var priceType = ToPriceType(orderType, condition?.PriceType);
		if (securityType is SecurityTypes.Stock or SecurityTypes.Warrant && priceType == "MKP")
			throw new NotSupportedException("Shioaji range-market (MKP) orders are available for futures/options only.");
		if (priceType == "LMT" && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price,
				"A positive limit price is required.");
		var timeInForce = regMsg.TimeInForce ?? TimeInForce.PutInQueue;
		if (securityType is SecurityTypes.Future or SecurityTypes.Option &&
			priceType == "MKT" && timeInForce == TimeInForce.PutInQueue)
			throw new NotSupportedException("Shioaji futures/options market orders require IOC or FOK; MKT+ROD is rejected upstream.");

		var request = new ShioajiPlaceOrderRequest { Contract = contract };
		if (securityType is SecurityTypes.Stock or SecurityTypes.Warrant)
		{
			request.StockOrder = new()
			{
				Price = priceType == "LMT" ? regMsg.Price : 0,
				Quantity = quantity,
				Action = regMsg.Side.ToShioajiAction(),
				PriceType = priceType,
				OrderType = timeInForce.ToShioajiTimeInForce(),
				OrderLot = (condition?.StockOrderLot ?? ShioajiStockOrderLots.Common).ToString(),
				OrderCondition = (condition?.StockOrderCondition ?? ShioajiStockOrderConditions.Cash).ToString(),
				CustomField = condition?.CustomField,
				Account = accountSelector,
			};
		}
		else
		{
			request.FuturesOrder = new()
			{
				Price = priceType == "LMT" ? regMsg.Price : 0,
				Quantity = quantity,
				Action = regMsg.Side.ToShioajiAction(),
				PriceType = priceType,
				OrderType = timeInForce.ToShioajiTimeInForce(),
				OpenCloseType = (condition?.FuturesOpenCloseType ?? ShioajiFuturesOpenCloseTypes.Auto).ToString(),
				CustomField = condition?.CustomField,
				Account = accountSelector,
			};
		}

		var trade = await _rest.PlaceOrder(request, cancellationToken);
		var orderId = trade?.Order?.Id;
		orderId.ThrowIfEmpty(nameof(trade.Order.Id));
		_orders[orderId] = new()
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = account.PortfolioName,
			Side = regMsg.Side,
			OrderType = priceType == "LMT" ? OrderTypes.Limit : OrderTypes.Market,
			TimeInForce = timeInForce,
			Condition = condition,
		};
		_transactionOrders[regMsg.TransactionId] = orderId;
		await ProcessTrade(trade, regMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		var orderId = await ResolveOrderId(replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId,
			replaceMsg.PortfolioName, cancellationToken);
		var current = await FindTrade(orderId, replaceMsg.PortfolioName, cancellationToken)
			?? throw new InvalidOperationException($"Shioaji order '{orderId}' was not found.");
		var quantity = ToOrderQuantity(replaceMsg.Volume, nameof(replaceMsg.Volume));
		var changed = false;
		ShioajiTrade result = current;

		if (replaceMsg.Price > 0 && current.Order.Price != replaceMsg.Price)
		{
			result = await _rest.UpdatePrice(orderId, replaceMsg.Price, cancellationToken);
			changed = true;
		}
		if (current.Order.Quantity != quantity)
		{
			if (quantity > current.Order.Quantity)
				throw new NotSupportedException("Shioaji quantity updates can only reduce an order quantity.");
			result = await _rest.UpdateQuantity(orderId, quantity, cancellationToken);
			changed = true;
		}

		if (_orders.TryGetValue(orderId, out var tracker))
		{
			_orders[orderId] = new()
			{
				TransactionId = replaceMsg.TransactionId,
				SecurityId = tracker.SecurityId,
				PortfolioName = tracker.PortfolioName,
				Side = tracker.Side,
				OrderType = replaceMsg.OrderType ?? tracker.OrderType,
				TimeInForce = replaceMsg.TimeInForce ?? tracker.TimeInForce,
				Condition = replaceMsg.Condition as ShioajiOrderCondition ?? tracker.Condition,
			};
		}
		_transactionOrders[replaceMsg.TransactionId] = orderId;
		if (changed)
			await ProcessTrade(result, replaceMsg.TransactionId, false, cancellationToken);
		else
			await ProcessTrade(current, replaceMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var orderId = await ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OriginalTransactionId,
			cancelMsg.PortfolioName, cancellationToken);
		var result = await _rest.CancelOrder(orderId, cancellationToken);
		await ProcessTrade(result, cancelMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		var left = statusMsg.Count ?? long.MaxValue;
		foreach (var account in FilterAccounts(statusMsg.PortfolioName))
		{
			var trades = (await _rest.GetTrades(account, cancellationToken))
				.Where(trade => trade?.Order != null)
				.OrderBy(GetOrderTime);
			foreach (var trade in trades)
			{
				var time = GetOrderTime(trade);
				if (statusMsg.From is DateTime from && time < from.NormalizeUtc())
					continue;
				if (statusMsg.To is DateTime to && time > to.NormalizeUtc())
					continue;
				await ProcessTrade(trade, statusMsg.TransactionId, true, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
				break;
		}
		_lastOrderRefresh = CurrentTime;

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

	private async ValueTask SendOrderSnapshot(long originId, CancellationToken cancellationToken)
	{
		foreach (var account in _accounts.Where(account => account.IsSigned))
		{
			foreach (var trade in await _rest.GetTrades(account, cancellationToken))
				await ProcessTrade(trade, originId, true, cancellationToken);
		}
		_lastOrderRefresh = CurrentTime;
	}

	private async ValueTask SendPortfolioSnapshot(long originId, string portfolioName,
		CancellationToken cancellationToken)
	{
		foreach (var account in FilterAccounts(portfolioName))
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = originId,
				PortfolioName = account.PortfolioName,
				BoardCode = account.AccountType.EqualsIgnoreCase("F") ? "TAIFEX" : "TWSE",
			}, cancellationToken);

			if (account.AccountType.EqualsIgnoreCase("F"))
			{
				var margin = await _rest.GetMargin(account, cancellationToken);
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originId,
					PortfolioName = account.PortfolioName,
					SecurityId = SecurityId.Money,
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, margin.EquityAmount != 0 ? margin.EquityAmount : margin.Equity, true)
				.TryAdd(PositionChangeTypes.BlockedValue, margin.InitialMargin, true)
				.TryAdd(PositionChangeTypes.VariationMargin, margin.AvailableMargin, true), cancellationToken);
			}
			else
			{
				var balance = await _rest.GetAccountBalance(account, cancellationToken);
				if (!balance.ErrorMessage.IsEmpty())
					this.AddWarningLog("Shioaji account {0} balance warning: {1}", account.PortfolioName, balance.ErrorMessage);
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originId,
					PortfolioName = account.PortfolioName,
					SecurityId = SecurityId.Money,
					ServerTime = balance.Date.ParseTaiwanTime() ?? CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, balance.AccountBalance, true), cancellationToken);
			}

			foreach (var position in await _rest.GetPositions(account, cancellationToken))
			{
				if (position?.Code.IsEmpty() != false)
					continue;
				var contract = await ResolvePositionContract(position.Code, account, cancellationToken);
				var quantity = position.Quantity;
				if (position.Direction.EqualsIgnoreCase("Sell") || position.Direction.EqualsIgnoreCase("Short"))
					quantity = -quantity;
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originId,
					PortfolioName = account.PortfolioName,
					SecurityId = contract.ToSecurityId(),
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
				.TryAdd(PositionChangeTypes.AveragePrice, position.Price, true)
				.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice, true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, position.ProfitLoss, true), cancellationToken);
			}
		}
		_lastPortfolioRefresh = CurrentTime;
	}

	private async ValueTask ProcessOrderEvent(string json, CancellationToken cancellationToken)
	{
		var envelope = Deserialize<ShioajiOrderEvent>(json);
		if (envelope.Data != null)
		{
			if (envelope.Data.StockOrder != null)
				await ProcessOrderReport(envelope.Data.StockOrder, cancellationToken);
			if (envelope.Data.FuturesOrder != null)
				await ProcessOrderReport(envelope.Data.FuturesOrder, cancellationToken);
			if (envelope.Data.StockDeal != null)
				await ProcessDealReport(envelope.Data.StockDeal, cancellationToken);
			if (envelope.Data.FuturesDeal != null)
				await ProcessDealReport(envelope.Data.FuturesDeal, cancellationToken);
			return;
		}

		var report = Deserialize<ShioajiOrderReport>(json);
		if (report.Order != null || report.Status != null)
			await ProcessOrderReport(report, cancellationToken);
	}

	private ValueTask ProcessOrderReport(ShioajiOrderReport report, CancellationToken cancellationToken)
		=> ProcessTrade(new()
		{
			Contract = report.Contract,
			Order = report.Order,
			Status = report.Status,
		}, 0, false, cancellationToken, report.Operation);

	private async ValueTask ProcessTrade(ShioajiTrade trade, long originId, bool isLookup,
		CancellationToken cancellationToken, ShioajiOperation operation = null)
	{
		if (trade?.Order?.Id.IsEmpty() != false)
			return;

		var orderId = trade.Order.Id;
		_orders.TryGetValue(orderId, out var tracker);
		if (tracker != null)
			_transactionOrders[tracker.TransactionId] = orderId;
		if (trade.Contract != null)
			CacheContract(trade.Contract);
		var contract = trade.Contract ?? await ResolveOrderContract(trade.Order, tracker, cancellationToken);
		var securityId = tracker?.SecurityId ?? contract.ToSecurityId();
		var status = trade.Status;
		var state = status?.Status.ToOrderState() ?? OrderStates.Active;
		var quantity = status?.OrderQuantity is > 0 ? status.OrderQuantity.Value : trade.Order.Quantity;
		var dealt = status?.DealQuantity ?? status?.Deals?.Sum(deal => deal.Quantity) ?? 0;
		var cancelled = status?.CancelQuantity ?? 0;
		var averagePrice = status?.Deals is { Length: > 0 } deals && deals.Sum(deal => deal.Quantity) > 0
			? deals.Sum(deal => deal.Price * deal.Quantity) / deals.Sum(deal => deal.Quantity)
			: (decimal?)null;
		var transactionId = tracker?.TransactionId ?? 0;
		var messageOrigin = isLookup ? originId : originId != 0 ? originId : transactionId != 0
			? transactionId
			: _orderStatusSubscriptionId;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = messageOrigin,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = orderId,
			SecurityId = securityId,
			PortfolioName = tracker?.PortfolioName.IsEmpty(trade.Order.Account?.PortfolioName),
			OrderType = tracker?.OrderType ?? trade.Order.PriceType.ToOrderType(),
			Side = tracker?.Side ?? trade.Order.Action.ToSide(),
			TimeInForce = tracker?.TimeInForce ?? trade.Order.OrderType.ToTimeInForce(),
			OrderPrice = trade.Order.Price,
			OrderVolume = quantity,
			Balance = Math.Max(0, quantity - dealt - cancelled),
			AveragePrice = averagePrice,
			OrderState = state,
			ServerTime = GetOrderTime(trade),
			Condition = tracker?.Condition ?? CreateCondition(trade.Order),
			Error = state == OrderStates.Failed || operation?.OperationCode is { Length: > 0 } code && code != "00"
				? CreateOrderError(operation, status)
				: null,
		}, cancellationToken);

		foreach (var deal in status?.Deals ?? [])
			await ProcessDeal(orderId, deal, securityId, tracker?.PortfolioName.IsEmpty(trade.Order.Account?.PortfolioName),
				tracker?.Side ?? trade.Order.Action.ToSide(), messageOrigin, cancellationToken);
	}

	private async ValueTask ProcessDeal(string orderId, ShioajiDeal deal, SecurityId securityId,
		string portfolioName, Sides side, long originId, CancellationToken cancellationToken)
	{
		if (deal == null)
			return;
		var fillId = deal.Sequence.IsEmpty($"{orderId}:{deal.Timestamp}:{deal.Price}:{deal.Quantity}");
		if (!_tradeIds.TryAdd($"{orderId}:{fillId}"))
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderStringId = orderId,
			TradeStringId = fillId,
			SecurityId = securityId,
			PortfolioName = portfolioName,
			Side = side,
			TradePrice = deal.Price,
			TradeVolume = deal.Quantity,
			ServerTime = deal.Timestamp.ParseUnixTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask ProcessDealReport(ShioajiDealReport deal, CancellationToken cancellationToken)
	{
		if (deal?.Code.IsEmpty() != false)
			return;
		var orderId = deal.TradeId.IsEmpty(deal.OrderNumber).IsEmpty(deal.SequenceNumber);
		var fillId = deal.ExchangeSequence.IsEmpty(
			$"{orderId}:{deal.Timestamp}:{deal.Price}:{deal.Quantity}");
		if (!_tradeIds.TryAdd($"{orderId}:{fillId}"))
			return;
		var securityType = deal.SecurityType.ToSecurityType();
		var contract = await _rest.GetContract(deal.Code, deal.SecurityType, cancellationToken)
			?? new ShioajiContract
			{
				SecurityType = deal.SecurityType,
				Region = "TW",
				Exchange = securityType is SecurityTypes.Future or SecurityTypes.Option ? "TAIFEX" : "TSE",
				Code = deal.Code,
			};
		_orders.TryGetValue(orderId, out var tracker);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = tracker?.TransactionId ?? _orderStatusSubscriptionId,
			OrderStringId = orderId,
			TradeStringId = fillId,
			SecurityId = tracker?.SecurityId ?? contract.ToSecurityId(),
			PortfolioName = tracker?.PortfolioName.IsEmpty($"{deal.BrokerId}-{deal.AccountId}"),
			Side = tracker?.Side ?? deal.Action.ToSide(),
			TradePrice = deal.Price.ToDecimalValue() ?? 0,
			TradeVolume = deal.Quantity,
			ServerTime = deal.Timestamp.ParseUnixTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async Task<string> ResolveOrderId(string orderId, long originalTransactionId,
		string portfolioName, CancellationToken cancellationToken)
	{
		if (orderId.IsEmpty())
			_transactionOrders.TryGetValue(originalTransactionId, out orderId);
		if (!orderId.IsEmpty())
			return orderId;

		foreach (var account in FilterAccounts(portfolioName))
		{
			foreach (var trade in await _rest.GetTrades(account, cancellationToken))
			{
				if (trade?.Order?.Id.IsEmpty() == false && _orders.TryGetValue(trade.Order.Id, out var tracker) &&
					tracker.TransactionId == originalTransactionId)
					return trade.Order.Id;
			}
		}
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
	}

	private async Task<ShioajiTrade> FindTrade(string orderId, string portfolioName,
		CancellationToken cancellationToken)
	{
		foreach (var account in FilterAccounts(portfolioName))
		{
			var trade = (await _rest.GetTrades(account, cancellationToken))
				.FirstOrDefault(item => item?.Order?.Id.EqualsIgnoreCase(orderId) == true);
			if (trade != null)
				return trade;
		}
		return null;
	}

	private IEnumerable<ShioajiAccount> FilterAccounts(string portfolioName)
	{
		var accounts = _accounts.Where(account => account.IsSigned);
		if (!portfolioName.IsEmpty())
			accounts = accounts.Where(account => account.PortfolioName.EqualsIgnoreCase(portfolioName));
		var result = accounts.ToArray();
		if (!portfolioName.IsEmpty() && result.Length == 0)
			throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		return result;
	}

	private async Task<ShioajiContract> ResolvePositionContract(string code, ShioajiAccount account,
		CancellationToken cancellationToken)
	{
		var contract = await _rest.GetContract(code, null, cancellationToken);
		if (contract != null)
		{
			CacheContract(contract);
			return contract;
		}
		return new()
		{
			SecurityType = account.AccountType.EqualsIgnoreCase("F") ? "FUT" : "STK",
			Region = "TW",
			Exchange = account.AccountType.EqualsIgnoreCase("F") ? "TAIFEX" : "TSE",
			Code = code,
		};
	}

	private async Task<ShioajiContract> ResolveOrderContract(ShioajiOrder order, OrderTracker tracker,
		CancellationToken cancellationToken)
	{
		if (tracker != null)
			return await ResolveContract(tracker.SecurityId, null, cancellationToken);
		throw new InvalidDataException($"Shioaji order '{order.Id}' has no contract identity.");
	}

	private static DateTime GetOrderTime(ShioajiTrade trade)
		=> trade.Status?.ModifiedTimestamp.ParseUnixTime() ?? trade.Status?.OrderTimestamp.ParseUnixTime() ??
			trade.Status?.ModifiedTime.ParseOffsetTime() ?? trade.Status?.OrderDateTime.ParseOffsetTime() ?? DateTime.UtcNow;

	private static int ToOrderQuantity(decimal quantity, string parameterName)
	{
		if (quantity <= 0 || quantity != decimal.Truncate(quantity) || quantity > int.MaxValue)
			throw new ArgumentOutOfRangeException(parameterName, quantity,
				"Shioaji order quantities must be positive whole numbers within Int32 range.");
		return decimal.ToInt32(quantity);
	}

	private static string ToPriceType(OrderTypes orderType, ShioajiPriceTypes? priceType)
		=> priceType switch
		{
			ShioajiPriceTypes.Limit => "LMT",
			ShioajiPriceTypes.Market => "MKT",
			ShioajiPriceTypes.RangeMarket => "MKP",
			_ => orderType == OrderTypes.Market ? "MKT" : "LMT",
		};

	private static ShioajiOrderCondition CreateCondition(ShioajiOrder order)
	{
		Enum.TryParse<ShioajiStockOrderLots>(order.OrderLot, true, out var lot);
		Enum.TryParse<ShioajiStockOrderConditions>(order.OrderCondition, true, out var condition);
		Enum.TryParse<ShioajiFuturesOpenCloseTypes>(order.OpenCloseType, true, out var openClose);
		return new()
		{
			StockOrderLot = order.OrderLot.IsEmpty() ? null : lot,
			StockOrderCondition = order.OrderCondition.IsEmpty() ? null : condition,
			FuturesOpenCloseType = order.OpenCloseType.IsEmpty() ? null : openClose,
			PriceType = order.PriceType?.ToUpperInvariant() switch
			{
				"LMT" => ShioajiPriceTypes.Limit,
				"MKT" => ShioajiPriceTypes.Market,
				"MKP" => ShioajiPriceTypes.RangeMarket,
				_ => ShioajiPriceTypes.Auto,
			},
			CustomField = order.CustomField,
		};
	}

	private static Exception CreateOrderError(ShioajiOperation operation, ShioajiOrderStatus status)
		=> new InvalidOperationException(operation?.OperationMessage
			.IsEmpty(status?.Message)
			.IsEmpty($"Shioaji order status: {status?.Status.IsEmpty("Failed")}."));
}
