namespace StockSharp.StocksTrader;

public partial class StocksTraderMessageAdapter
{
	private sealed class PositionAggregate
	{
		public string Ticker { get; init; }
		public decimal NetVolume { get; set; }
		public decimal GrossVolume { get; set; }
		public decimal WeightedOpenPrice { get; set; }
		public decimal UnrealizedPnL { get; set; }
		public long LastTime { get; set; }
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		var accountId = ResolvePortfolio(regMsg.PortfolioName);
		if (!_account.Status.EqualsIgnoreCase("enabled"))
			throw new InvalidOperationException(
				$"StocksTrader account {accountId} does not allow new deals in state {_account.Status}.");
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume,
				"StocksTrader requires a positive order volume.");
		if ((regMsg.TimeInForce ?? TimeInForce.PutInQueue) != TimeInForce.PutInQueue)
			throw new NotSupportedException(
				"StocksTrader REST API does not expose IOC or FOK orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Market;
		var nativeType = orderType.ToNative();
		if (orderType != OrderTypes.Market && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price,
				"StocksTrader Limit and Stop orders require a positive price.");
		var condition = regMsg.Condition as StocksTraderOrderCondition ?? new();
		ValidateProtection(condition);
		var instrument = await ResolveInstrumentAsync(regMsg.SecurityId, cancellationToken);
		ValidateInstrumentOrder(instrument, regMsg.Side, regMsg.Volume);

		var quote = await Client.GetQuoteAsync(accountId, instrument.Ticker,
			cancellationToken) ?? throw new InvalidOperationException(
				$"StocksTrader returned no pre-trade quote for {instrument.Ticker}.");
		var executablePrice = regMsg.Side == Sides.Buy ? quote.AskPrice : quote.BidPrice;
		if (executablePrice is not > 0)
			throw new InvalidOperationException(
				$"StocksTrader returned no executable {regMsg.Side} quote for {instrument.Ticker}.");

		var expiration = ToExpiration(regMsg.TillDate, orderType);
		var result = await Client.PlaceOrderAsync(accountId, new()
		{
			Ticker = instrument.Ticker,
			Volume = regMsg.Volume,
			Side = regMsg.Side.ToNative(),
			Type = nativeType,
			Price = orderType == OrderTypes.Market ? null : regMsg.Price,
			Expiration = expiration,
			StopLoss = condition.StopLoss,
			TakeProfit = condition.TakeProfit,
		}, cancellationToken);
		var orderId = result?.OrderId.ThrowIfEmpty(
			nameof(StocksTraderOrderResult.OrderId));
		var now = DateTime.UtcNow;
		using (_sync.EnterScope())
		{
			_orderTransactions[orderId] = regMsg.TransactionId;
			_transactionOrders[regMsg.TransactionId] = orderId;
			_knownOrders.Add(orderId);
		}

		await ProcessOrderAsync(new()
		{
			Id = orderId,
			Ticker = instrument.Ticker,
			Volume = regMsg.Volume,
			Side = regMsg.Side.ToNative(),
			Type = nativeType,
			Price = orderType == OrderTypes.Market ? null : regMsg.Price,
			Expiration = expiration,
			CreateTime = checked((long)now.ToUnix()),
			LastModified = checked((long)now.ToUnix()),
			Deals = [],
			Status = orderType == OrderTypes.Market ? "in_execution" : "active",
		}, regMsg.TransactionId, false, true, condition, cancellationToken);
		_lastOrderRefresh = default;
		this.AddInfoLog("StocksTrader placed {0} order {1} for {2} {3} {4}.",
			nativeType, orderId, regMsg.Side, regMsg.Volume, instrument.Ticker);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		ResolvePortfolio(replaceMsg.PortfolioName);
		var nativeId = ResolveNativeId(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.OriginalTransactionId);
		var target = await ResolveTargetAsync(nativeId, cancellationToken);
		var condition = replaceMsg.Condition as StocksTraderOrderCondition ?? new();
		ValidateProtection(condition);

		if (target.Order is { } order)
		{
			var nativeType = order.Type.ToOrderType();
			if (nativeType == OrderTypes.Market)
				throw new NotSupportedException(
					"StocksTrader permits modifying only active Limit or Stop orders.");
			if (replaceMsg.Volume <= 0)
				throw new ArgumentOutOfRangeException(nameof(replaceMsg.Volume),
					replaceMsg.Volume, "StocksTrader requires a positive order volume.");
			if (replaceMsg.Price <= 0)
				throw new ArgumentOutOfRangeException(nameof(replaceMsg.Price),
					replaceMsg.Price, "StocksTrader order modification requires a positive price.");
			if (!replaceMsg.SecurityId.SecurityCode.IsEmpty() &&
				!replaceMsg.SecurityId.SecurityCode.EqualsIgnoreCase(order.Ticker))
				throw new NotSupportedException(
					"StocksTrader cannot change an order instrument in place.");
			if (replaceMsg.Side != order.Side.ToSide())
				throw new NotSupportedException(
					"StocksTrader cannot change an order side in place.");
			var requestedType = replaceMsg.OrderType ?? nativeType;
			if (requestedType != nativeType)
				throw new NotSupportedException(
					"StocksTrader cannot change an order type in place.");

			var instrument = await ResolveInstrumentAsync(order.Ticker.ToSecurityId(),
				cancellationToken);
			ValidateInstrumentOrder(instrument, replaceMsg.Side, replaceMsg.Volume);
			var expiration = ToExpiration(replaceMsg.TillDate, nativeType);
			await Client.ModifyOrderAsync(PortfolioName, nativeId, new()
			{
				Volume = replaceMsg.Volume,
				Price = replaceMsg.Price,
				Expiration = expiration,
				StopLoss = condition.StopLoss,
				TakeProfit = condition.TakeProfit,
			}, cancellationToken);

			using (_sync.EnterScope())
			{
				_orderTransactions[nativeId] = replaceMsg.TransactionId;
				_transactionOrders[replaceMsg.TransactionId] = nativeId;
			}
			order.Volume = replaceMsg.Volume;
			order.Price = replaceMsg.Price;
			order.Expiration = expiration;
			order.LastModified = checked((long)DateTime.UtcNow.ToUnix());
			order.Status = "active";
			await ProcessOrderAsync(order, replaceMsg.TransactionId, false, true,
				condition, cancellationToken);
			this.AddInfoLog("StocksTrader modified order {0}.", nativeId);
		}
		else
		{
			var deal = target.Deal;
			if (condition.StopLoss is null && condition.TakeProfit is null)
				throw new InvalidOperationException(
					"StocksTrader deal modification requires StopLoss or TakeProfit.");
			await Client.ModifyDealAsync(PortfolioName, nativeId, new()
			{
				StopLoss = condition.StopLoss,
				TakeProfit = condition.TakeProfit,
			}, cancellationToken);
			await SendOutMessageAsync(CreateDealOrderMessage(deal,
				replaceMsg.TransactionId, OrderStates.Active, condition), cancellationToken);
			this.AddInfoLog("StocksTrader modified protection for deal {0}.", nativeId);
		}

		_lastOrderRefresh = default;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		ResolvePortfolio(cancelMsg.PortfolioName);
		var nativeId = ResolveNativeId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId);
		var target = await ResolveTargetAsync(nativeId, cancellationToken);
		using (_sync.EnterScope())
			_cancelTransactions[nativeId] = cancelMsg.TransactionId;
		try
		{
			if (target.Order is { } order)
			{
				await Client.CancelOrderAsync(PortfolioName, nativeId, cancellationToken);
				order.Status = "canceled";
				order.LastModified = checked((long)DateTime.UtcNow.ToUnix());
				await ProcessOrderAsync(order, cancelMsg.TransactionId, false, true,
					null, cancellationToken);
				this.AddInfoLog("StocksTrader canceled order {0}.", nativeId);
			}
			else
			{
				await Client.CloseDealAsync(PortfolioName, nativeId, cancellationToken);
				await SendOutMessageAsync(CreateDealOrderMessage(target.Deal,
					cancelMsg.TransactionId, OrderStates.Done, null), cancellationToken);
				this.AddInfoLog("StocksTrader closed deal {0}.", nativeId);
			}
		}
		catch
		{
			using (_sync.EnterScope())
				_cancelTransactions.Remove(nativeId);
			throw;
		}

		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
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

		ResolvePortfolio(statusMsg.PortfolioName);
		await SendOrderSnapshotAsync(statusMsg.TransactionId, statusMsg, true,
			cancellationToken);
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

		ResolvePortfolio(lookupMsg.PortfolioName);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			lookupMsg.PortfolioName, true, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_portfolioFilter = lookupMsg.PortfolioName;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private async Task SendOrderSnapshotAsync(long originalTransactionId,
		OrderStatusMessage filter, bool isLookup, CancellationToken cancellationToken)
	{
		var orders = await LoadOrdersAsync(filter, isLookup, cancellationToken);
		var deals = await LoadDealsAsync(filter, isLookup, cancellationToken);
		RegisterKnownOrders(orders);
		RegisterKnownDeals(deals);

		var skip = Math.Max(0, filter?.Skip ?? 0);
		var left = Math.Max(0, filter?.Count ?? long.MaxValue);
		var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var order in orders.OrderByDescending(GetOrderTime))
		{
			if (!IsOrderMatch(order, filter))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			selected.Add(order.Id);
			await ProcessOrderAsync(order, originalTransactionId, isLookup, isLookup,
				null, cancellationToken);
			left--;
		}

		var orderByDeal = orders
			.Where(static order => order?.Id.IsEmpty() == false)
			.SelectMany(order => (order.Deals ?? [])
				.Where(static dealId => !dealId.IsEmpty())
				.Select(dealId => new { DealId = dealId, Order = order }))
			.GroupBy(static item => item.DealId, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(static group => group.Key, static group => group.First().Order,
				StringComparer.OrdinalIgnoreCase);
		foreach (var deal in deals.OrderBy(static deal => deal.OpenTime ?? 0))
		{
			if (deal?.Id.IsEmpty() != false ||
				!orderByDeal.TryGetValue(deal.Id, out var order) ||
				!selected.Contains(order.Id))
				continue;
			await ProcessDealTradesAsync(deal, order, originalTransactionId, isLookup,
				cancellationToken);
		}

		_lastOrderRefresh = DateTime.UtcNow;
		this.AddDebugLog("StocksTrader reconciled {0} orders and {1} deals.",
			orders.Length, deals.Length);
	}

	private async Task<StocksTraderOrder[]> LoadOrdersAsync(OrderStatusMessage filter,
		bool isLookup, CancellationToken cancellationToken)
	{
		var result = new Dictionary<string, StocksTraderOrder>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var order in await Client.GetOrdersAsync(PortfolioName, null,
			cancellationToken) ?? [])
			AddLatest(result, order, static item => item.LastModified ?? item.CreateTime ?? 0);

		var from = isLookup
			? filter?.From ?? DateTime.UnixEpoch
			: (_lastOrderRefresh == default
				? DateTime.UtcNow - PollingInterval - TimeSpan.FromSeconds(2)
				: _lastOrderRefresh - TimeSpan.FromSeconds(2));
		var to = isLookup ? filter?.To ?? DateTime.UtcNow : DateTime.UtcNow;
		long pageSkip = 0;
		string previousPage = null;
		while (true)
		{
			var page = await Client.GetOrdersAsync(PortfolioName, new()
			{
				From = from,
				To = to,
				Skip = pageSkip,
				Limit = 500,
			}, cancellationToken) ?? [];
			var pageKey = PageKey(page.Select(static item => item?.Id));
			if (!previousPage.IsEmpty() && pageKey == previousPage)
				throw new InvalidOperationException(
					"StocksTrader repeated an order-history page during pagination.");
			previousPage = pageKey;
			foreach (var order in page)
				AddLatest(result, order,
					static item => item.LastModified ?? item.CreateTime ?? 0);
			if (page.Length < 500)
				break;
			pageSkip += page.Length;
		}
		return [.. result.Values];
	}

	private async Task<StocksTraderDeal[]> LoadDealsAsync(OrderStatusMessage filter,
		bool isLookup, CancellationToken cancellationToken)
	{
		var result = new Dictionary<string, StocksTraderDeal>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var deal in await Client.GetDealsAsync(PortfolioName, null,
			cancellationToken) ?? [])
			AddLatest(result, deal, static item => item.CloseTime ?? item.OpenTime ?? 0);

		var from = isLookup
			? filter?.From ?? DateTime.UnixEpoch
			: (_lastOrderRefresh == default
				? DateTime.UtcNow - PollingInterval - TimeSpan.FromSeconds(2)
				: _lastOrderRefresh - TimeSpan.FromSeconds(2));
		var to = isLookup ? filter?.To ?? DateTime.UtcNow : DateTime.UtcNow;
		long pageSkip = 0;
		string previousPage = null;
		while (true)
		{
			var page = await Client.GetDealsAsync(PortfolioName, new()
			{
				From = from,
				To = to,
				Skip = pageSkip,
				Limit = 500,
			}, cancellationToken) ?? [];
			var pageKey = PageKey(page.Select(static item => item?.Id));
			if (!previousPage.IsEmpty() && pageKey == previousPage)
				throw new InvalidOperationException(
					"StocksTrader repeated a deal-history page during pagination.");
			previousPage = pageKey;
			foreach (var deal in page)
				AddLatest(result, deal,
					static item => item.CloseTime ?? item.OpenTime ?? 0);
			if (page.Length < 500)
				break;
			pageSkip += page.Length;
		}
		return [.. result.Values];
	}

	private async ValueTask ProcessOrderAsync(StocksTraderOrder order,
		long originalTransactionId, bool isLookup, bool isForced,
		StocksTraderOrderCondition condition, CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false || order.Ticker.IsEmpty())
			return;

		var state = order.Status.ToOrderState();
		var signature = $"{order.Status}|{order.Volume.ToString(CultureInfo.InvariantCulture)}|" +
			$"{order.Price}|{order.FilledPrice}|{order.LastModified}|" +
			(order.Deals ?? []).Join(",");
		long transactionId;
		long cancelTransactionId;
		using (_sync.EnterScope())
		{
			if (!isForced && _orderSignatures.TryGetValue(order.Id, out var previous) &&
				previous == signature)
				return;
			_orderSignatures[order.Id] = signature;
			_knownOrders.Add(order.Id);
			transactionId = _orderTransactions.TryGetValue(order.Id, out var tracked)
				? tracked
				: 0;
			cancelTransactionId = _cancelTransactions.TryGetValue(order.Id,
				out var cancellation) ? cancellation : 0;
		}

		var originId = isLookup
			? originalTransactionId
			: state == OrderStates.Done && order.Status.EqualsIgnoreCase("canceled") &&
				cancelTransactionId != 0
				? cancelTransactionId
				: transactionId != 0 ? transactionId : originalTransactionId;
		var serverTime = GetOrderTime(order);
		var volume = order.Volume.Abs();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = transactionId,
			OrderId = order.Id.ToNumericId(),
			OrderStringId = order.Id,
			PortfolioName = PortfolioName,
			SecurityId = order.Ticker.ToSecurityId(),
			Side = order.Side.ToSide(),
			OrderType = order.Type.ToOrderType(),
			OrderPrice = order.Price ?? 0,
			AveragePrice = order.FilledPrice,
			OrderVolume = volume,
			Balance = state is OrderStates.Done or OrderStates.Failed ? 0 : volume,
			OrderState = state,
			TimeInForce = TimeInForce.PutInQueue,
			ExpiryDate = order.Expiration is > 0
				? order.Expiration.FromStocksTraderEpoch()
				: null,
			ServerTime = serverTime,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Comment.IsEmpty(
					$"StocksTrader order {order.Id} was rejected."))
				: null,
		}, cancellationToken);

		if (state == OrderStates.Failed)
			this.AddWarningLog("StocksTrader order {0} was rejected: {1}", order.Id,
				order.Comment.IsEmpty("no reason supplied"));
		if (state is OrderStates.Done or OrderStates.Failed)
		{
			using (_sync.EnterScope())
				_cancelTransactions.Remove(order.Id);
		}
	}

	private async ValueTask ProcessDealTradesAsync(StocksTraderDeal deal,
		StocksTraderOrder order, long originalTransactionId, bool isLookup,
		CancellationToken cancellationToken)
	{
		await SendDealTradeAsync(deal, order, false, originalTransactionId,
			isLookup, cancellationToken);
		if (deal.ClosePrice is not null && deal.CloseTime is > 0)
		{
			await SendDealTradeAsync(deal, order, true, originalTransactionId,
				isLookup, cancellationToken);
			using (_sync.EnterScope())
				_cancelTransactions.Remove(deal.Id);
		}
	}

	private ValueTask SendDealTradeAsync(StocksTraderDeal deal, StocksTraderOrder order,
		bool isClose, long originalTransactionId, bool isLookup,
		CancellationToken cancellationToken)
	{
		var tradeId = $"{deal.Id}:{(isClose ? "close" : "open")}";
		long transactionId;
		long cancelTransactionId;
		using (_sync.EnterScope())
		{
			var alreadyReported = _reportedTrades.Contains(tradeId);
			_reportedTrades.Add(tradeId);
			if (alreadyReported && !isLookup)
				return default;
			transactionId = _orderTransactions.TryGetValue(order.Id, out var tracked)
				? tracked
				: 0;
			cancelTransactionId = _cancelTransactions.TryGetValue(deal.Id,
				out var cancellation) ? cancellation : 0;
		}

		var originId = isLookup
			? originalTransactionId
			: isClose && cancelTransactionId != 0
				? cancelTransactionId
				: transactionId != 0 ? transactionId : originalTransactionId;
		var side = isClose ? deal.Side.ToSide().Invert() : deal.Side.ToSide();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderId = order.Id.ToNumericId(),
			OrderStringId = order.Id,
			TradeStringId = isClose ? tradeId : deal.Id,
			PortfolioName = PortfolioName,
			SecurityId = deal.Ticker.ToSecurityId(),
			Side = side,
			TradePrice = isClose ? deal.ClosePrice : deal.OpenPrice,
			TradeVolume = deal.Volume.Abs(),
			ServerTime = isClose
				? deal.CloseTime.FromStocksTraderEpoch()
				: deal.OpenTime.FromStocksTraderEpoch(),
			PnL = isClose ? deal.Profit : null,
		}, cancellationToken);
	}

	private async Task SendPortfolioSnapshotAsync(long originalTransactionId,
		string portfolioName, bool isLookup, CancellationToken cancellationToken)
	{
		var accountId = ResolvePortfolio(portfolioName);
		var stateTask = Client.GetAccountStateAsync(accountId, cancellationToken);
		var dealsTask = Client.GetDealsAsync(accountId, null, cancellationToken);
		await Task.WhenAll(stateTask, dealsTask);
		var state = await stateTask ?? throw new InvalidOperationException(
			"StocksTrader returned no account state.");
		var deals = await dealsTask ?? [];
		RegisterKnownDeals(deals);
		var currency = _account.Currency.ToCurrency();
		var now = DateTime.UtcNow;

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountId,
			BoardCode = StocksTraderExtensions.BoardCode,
			Currency = currency,
		}, cancellationToken);

		var money = new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountId,
			SecurityId = SecurityId.Money,
			ServerTime = now,
		};
		if (state.Margin is { } margin)
		{
			money
				.TryAdd(PositionChangeTypes.BeginValue, margin.Balance, true)
				.TryAdd(PositionChangeTypes.CurrentValue, margin.Equity, true)
				.TryAdd(PositionChangeTypes.BlockedValue, margin.Margin, true)
				.TryAdd(PositionChangeTypes.BuyOrdersMargin, margin.FreeMargin, true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, margin.UnrealizedPnL, true);
		}
		else if (state.Cash is { } cash)
		{
			money
				.TryAdd(PositionChangeTypes.CurrentValue, cash.MyPortfolio, true)
				.TryAdd(PositionChangeTypes.BlockedValue, cash.Investments, true)
				.TryAdd(PositionChangeTypes.BuyOrdersMargin,
					cash.AvailableToInvest, true);
		}
		else
		{
			throw new InvalidOperationException(
				"StocksTrader returned neither margin nor cash account state.");
		}
		money.TryAdd(PositionChangeTypes.Currency, currency);
		await SendOutMessageAsync(money, cancellationToken);

		var positions = new Dictionary<string, PositionAggregate>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var deal in deals.Where(static deal =>
			deal?.Id.IsEmpty() == false && !deal.Ticker.IsEmpty() &&
			(deal.Status.EqualsIgnoreCase("open") ||
			 deal.Status.EqualsIgnoreCase("closing"))))
		{
			if (!positions.TryGetValue(deal.Ticker, out var position))
			{
				position = new() { Ticker = deal.Ticker };
				positions.Add(deal.Ticker, position);
			}
			var volume = deal.Volume.Abs();
			position.NetVolume += deal.Side.ToSide() == Sides.Sell ? -volume : volume;
			position.GrossVolume += volume;
			position.WeightedOpenPrice += volume * deal.OpenPrice;
			position.UnrealizedPnL += deal.Profit;
			position.LastTime = Math.Max(position.LastTime, deal.OpenTime ?? 0);
		}

		string[] previousTickers;
		using (_sync.EnterScope())
		{
			previousTickers = [.. _positionTickers];
			_positionTickers.Clear();
			foreach (var ticker in positions.Keys)
				_positionTickers.Add(ticker);
		}
		if (!isLookup)
		{
			foreach (var ticker in previousTickers.Where(ticker =>
				!positions.ContainsKey(ticker)))
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = accountId,
					SecurityId = ticker.ToSecurityId(),
					ServerTime = now,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, 0m, true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, 0m, true),
					cancellationToken);
			}
		}

		foreach (var position in positions.Values)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = accountId,
				SecurityId = position.Ticker.ToSecurityId(),
				ServerTime = ((long?)position.LastTime).FromStocksTraderEpoch(),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.NetVolume, true)
			.TryAdd(PositionChangeTypes.AveragePrice,
				position.GrossVolume > 0
					? position.WeightedOpenPrice / position.GrossVolume
					: null, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				position.UnrealizedPnL, true), cancellationToken);
		}

		_lastPortfolioRefresh = now;
		_lastConnectionCheck = now;
		this.AddDebugLog("StocksTrader reconciled portfolio {0} with {1} open symbols.",
			accountId, positions.Count);
	}

	private ExecutionMessage CreateDealOrderMessage(StocksTraderDeal deal,
		long originalTransactionId, OrderStates state,
		StocksTraderOrderCondition condition)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = originalTransactionId,
			OrderId = deal.Id.ToNumericId(),
			OrderStringId = deal.Id,
			PortfolioName = PortfolioName,
			SecurityId = deal.Ticker.ToSecurityId(),
			Side = state == OrderStates.Done
				? deal.Side.ToSide().Invert()
				: deal.Side.ToSide(),
			OrderType = OrderTypes.Market,
			OrderPrice = deal.OpenPrice,
			OrderVolume = deal.Volume.Abs(),
			Balance = state == OrderStates.Done ? 0 : deal.Volume.Abs(),
			OrderState = state,
			ServerTime = DateTime.UtcNow,
			Condition = condition,
		};

	private static bool IsOrderMatch(StocksTraderOrder order, OrderStatusMessage filter)
	{
		if (order?.Id.IsEmpty() != false || order.Ticker.IsEmpty())
			return false;
		if (filter is null)
			return true;
		if (filter.OrderId is long numericId && order.Id.ToNumericId() != numericId)
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.EqualsIgnoreCase(order.Id))
			return false;
		if (filter.SecurityId != default &&
			!filter.SecurityId.SecurityCode.EqualsIgnoreCase(order.Ticker))
			return false;
		if (filter.SecurityIds.Length > 0 && !filter.SecurityIds.Any(id =>
			id.SecurityCode.EqualsIgnoreCase(order.Ticker)))
			return false;
		if (filter.Side is Sides side && side != order.Side.ToSide())
			return false;
		if (filter.Volume is decimal volume && volume != order.Volume.Abs())
			return false;
		var state = order.Status.ToOrderState();
		if (filter.States.Length > 0 && !filter.States.Contains(state))
			return false;
		var created = order.CreateTime.FromStocksTraderEpoch();
		if (filter.From is DateTime from && created < from.ToUniversalTime())
			return false;
		return filter.To is not DateTime to || created <= to.ToUniversalTime();
	}

	private static DateTime GetOrderTime(StocksTraderOrder order)
		=> (order.LastModified ?? order.CreateTime).FromStocksTraderEpoch();

	private static long? ToExpiration(DateTime? tillDate, OrderTypes orderType)
	{
		if (orderType == OrderTypes.Market)
		{
			if (tillDate is not null)
				throw new NotSupportedException(
					"StocksTrader market orders do not accept expiration.");
			return null;
		}
		return tillDate is DateTime expiry
			? checked((long)expiry.ToUniversalTime().ToUnix())
			: 0;
	}

	private static void ValidateProtection(StocksTraderOrderCondition condition)
	{
		if (condition.StopLoss is <= 0)
			throw new ArgumentOutOfRangeException(nameof(condition.StopLoss),
				condition.StopLoss, "StocksTrader stop-loss price must be positive.");
		if (condition.TakeProfit is <= 0)
			throw new ArgumentOutOfRangeException(nameof(condition.TakeProfit),
				condition.TakeProfit, "StocksTrader take-profit price must be positive.");
	}

	private static void ValidateInstrumentOrder(StocksTraderInstrument instrument,
		Sides side, decimal volume)
	{
		if (instrument.TradeMode.EqualsIgnoreCase("disabled"))
			throw new InvalidOperationException(
				$"StocksTrader instrument {instrument.Ticker} is disabled.");
		if (instrument.TradeMode.EqualsIgnoreCase("close_only"))
			throw new InvalidOperationException(
				$"StocksTrader instrument {instrument.Ticker} is close-only.");
		if (side == Sides.Buy && instrument.TradeMode.EqualsIgnoreCase("sell_only") ||
			side == Sides.Sell && instrument.TradeMode.EqualsIgnoreCase("buy_only"))
			throw new InvalidOperationException(
				$"StocksTrader instrument {instrument.Ticker} does not allow {side} orders.");
		if (instrument.MinVolume > 0 && volume < instrument.MinVolume)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"StocksTrader minimum volume for {instrument.Ticker} is {instrument.MinVolume}.");
		if (instrument.MaxVolume > 0 && volume > instrument.MaxVolume)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"StocksTrader maximum volume for {instrument.Ticker} is {instrument.MaxVolume}.");
		if (instrument.VolumeStep > 0 && volume % instrument.VolumeStep != 0)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"StocksTrader volume for {instrument.Ticker} must be a multiple of " +
				$"{instrument.VolumeStep}.");
	}

	private static void AddLatest(Dictionary<string, StocksTraderOrder> target,
		StocksTraderOrder value, Func<StocksTraderOrder, long> getTime)
	{
		if (value?.Id.IsEmpty() != false)
			return;
		if (!target.TryGetValue(value.Id, out var previous) ||
			getTime(value) >= getTime(previous))
			target[value.Id] = value;
	}

	private static void AddLatest(Dictionary<string, StocksTraderDeal> target,
		StocksTraderDeal value, Func<StocksTraderDeal, long> getTime)
	{
		if (value?.Id.IsEmpty() != false)
			return;
		if (!target.TryGetValue(value.Id, out var previous) ||
			getTime(value) >= getTime(previous))
			target[value.Id] = value;
	}

	private static string PageKey(IEnumerable<string> ids)
	{
		var page = ids.Where(static id => !id.IsEmpty()).ToArray();
		return page.Length == 0
			? string.Empty
			: $"{page.Length}:{page[0]}:{page[^1]}";
	}
}
