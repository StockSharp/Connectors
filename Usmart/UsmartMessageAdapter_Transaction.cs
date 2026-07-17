namespace StockSharp.Usmart;

public partial class UsmartMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var portfolio = ResolvePortfolio(message.PortfolioName);
		if (message.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Volume), message.Volume,
				"uSMART requires a positive order quantity.");
		if (message.OrderType is not null and not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException(
				$"The documented uSMART stock API does not support {message.OrderType} orders.");

		var securityId = NormalizeSecurityId(message.SecurityId);
		var exchange = (UsmartExchangeTypes)securityId.ToExchangeType(DefaultMarket);
		var condition = message.Condition as UsmartOrderCondition ?? new();
		var instruction = message.OrderType == OrderTypes.Market
			? UsmartOrderInstructions.Market : condition.Instruction;
		ValidateOrder(exchange, instruction, message.Price, condition);
		var side = message.Side == Sides.Sell ? UsmartOrderSides.Sell : UsmartOrderSides.Buy;
		var encryptedPassword = EncryptedTradePassword?.UnSecure();
		UsmartOrderAction action;
		if (condition.IsFractional)
		{
			if (exchange is not UsmartExchangeTypes.HongKong and not UsmartExchangeTypes.UnitedStates)
				throw new NotSupportedException(
					"uSMART fractional orders are documented only for Hong Kong and U.S. stocks.");
			var response = await _rest.PlaceFractionalOrder(new UsmartFractionalOrderRequest
			{
				Quantity = message.Volume,
				Price = message.Price,
				Side = side,
				Exchange = exchange,
				StockCode = securityId.SecurityCode,
			}, cancellationToken);
			action = response.Data;
		}
		else
		{
			var response = await _rest.PlaceOrder(new UsmartPlaceOrderRequest
			{
				SerialNo = message.TransactionId,
				Quantity = message.Volume,
				Price = instruction is UsmartOrderInstructions.Market or
					UsmartOrderInstructions.Auction ? 0 : message.Price,
				Instruction = instruction,
				Side = side,
				Exchange = exchange,
				StockCode = securityId.SecurityCode,
				EncryptedPassword = encryptedPassword,
				ForceOrder = condition.ForceOrder,
				Session = condition.Session,
			}, cancellationToken);
			action = response.Data;
		}
		var orderId = action?.OrderId.IsEmpty(action?.FractionalOrderId);
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				"uSMART accepted the order without returning an order ID.");

		var order = new UsmartOrder
		{
			OrderId = orderId,
			SerialNo = message.TransactionId,
			Quantity = message.Volume,
			Price = message.Price,
			Instruction = ToWire(instruction),
			Side = (int)side,
			Exchange = (int)exchange,
			Flag = condition.IsFractional ? "2" : "0",
			Session = (int)condition.Session,
			Status = action.Status,
			StatusName = action.StatusName,
			StockCode = securityId.SecurityCode,
			CreateTime = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
		};
		_orders[orderId] = order;
		_orderTransactions[orderId] = message.TransactionId;
		_transactionOrders[message.TransactionId] = orderId;
		await ProcessOrder(order, message.TransactionId, false, true, cancellationToken);
		_lastPoll = default;
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		ResolvePortfolio(message.PortfolioName);
		if (message.Volume <= 0 || message.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(message),
				"uSMART replacement requires positive price and quantity.");
		var orderId = GetOrderId(message.OldOrderStringId, message.OriginalTransactionId);
		var order = await GetOrderForMutation(orderId, cancellationToken);
		if (order?.Flag == "2")
			throw new NotSupportedException(
				"The documented uSMART fractional-share endpoint supports withdrawal but not amendment.");
		var nativeId = ParseOrderId(orderId);
		var condition = message.Condition as UsmartOrderCondition ?? new();
		var response = await _rest.ModifyOrder(new UsmartModifyOrderRequest
		{
			Action = UsmartModifyActions.Modify,
			Quantity = message.Volume,
			OrderId = nativeId,
			Price = message.Price,
			EncryptedPassword = EncryptedTradePassword?.UnSecure(),
			ForceOrder = condition.ForceOrder,
		}, cancellationToken);
		if (order != null)
		{
			order.Quantity = message.Volume;
			order.Price = message.Price;
			order.Status = response.Data?.Status ?? 5;
			order.StatusName = response.Data?.StatusName;
			_orders[orderId] = order;
		}
		_orderTransactions[orderId] = message.TransactionId;
		_transactionOrders[message.TransactionId] = orderId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderStringId = orderId,
			PortfolioName = ResolvePortfolio(null),
			SecurityId = order == null ? message.SecurityId
				: order.StockCode.ToSecurityId(order.Exchange),
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = CurrentTime,
		}, cancellationToken);
		_lastPoll = default;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		ResolvePortfolio(message.PortfolioName);
		var orderId = GetOrderId(message.OrderStringId, message.OriginalTransactionId);
		var order = await GetOrderForMutation(orderId, cancellationToken);
		var nativeId = ParseOrderId(orderId);
		_cancelTransactions[orderId] = message.TransactionId;
		try
		{
			if (order?.Flag == "2")
				await _rest.CancelFractionalOrder(new UsmartFractionalCancelRequest
				{
					Action = UsmartModifyActions.Cancel,
					OrderId = nativeId,
				}, cancellationToken);
			else
				await _rest.ModifyOrder(new UsmartModifyOrderRequest
				{
					Action = UsmartModifyActions.Cancel,
					OrderId = nativeId,
					EncryptedPassword = EncryptedTradePassword?.UnSecure(),
				}, cancellationToken);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = message.TransactionId,
				OrderStringId = orderId,
				PortfolioName = ResolvePortfolio(null),
				SecurityId = order == null ? message.SecurityId
					: order.StockCode.ToSecurityId(order.Exchange),
				OrderState = OrderStates.Pending,
				ServerTime = CurrentTime,
			}, cancellationToken);
			_lastPoll = default;
		}
		catch
		{
			_cancelTransactions.Remove(orderId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == message.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}
		ResolvePortfolio(message.PortfolioName);
		await SendOrderSnapshot(message.TransactionId, message, true, cancellationToken);
		if (message.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = message.TransactionId;
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		_lastPoll = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_portfolioSubscriptionId == message.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				_portfolioFilter = null;
			}
			return;
		}
		ResolvePortfolio(message.PortfolioName);
		await SendPortfolioSnapshot(message.TransactionId, message.PortfolioName, true,
			cancellationToken);
		if (message.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = message.TransactionId;
			_portfolioFilter = message.PortfolioName;
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		_lastPoll = CurrentTime;
	}

	private async Task SendOrderSnapshot(long originalTransactionId, OrderStatusMessage filter,
		bool isLookup, CancellationToken cancellationToken)
	{
		ResolvePortfolio(filter?.PortfolioName);
		var orders = await LoadTodayOrders(cancellationToken);
		var skip = Math.Max(0, filter?.Skip ?? 0);
		var left = Math.Max(0, filter?.Count ?? long.MaxValue);
		var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var order in orders.Where(order => IsOrderMatch(order, filter))
			.OrderByDescending(GetOrderTime))
		{
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			selected.Add(order.OrderId);
			await ProcessOrder(order, originalTransactionId, isLookup, isLookup,
				cancellationToken);
			left--;
		}

		var now = DateTime.UtcNow;
		var records = await LoadTrades(filter?.From ?? now.Date, filter?.To ?? now,
			cancellationToken);
		foreach (var trade in records.Where(trade => trade != null && selected.Contains(
			trade.OrderId.ToString(CultureInfo.InvariantCulture))).OrderBy(trade => trade.Time))
			await ProcessTrade(trade, originalTransactionId, isLookup, cancellationToken);
	}

	private async Task<UsmartTrade[]> LoadTrades(DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var result = new List<UsmartTrade>();
		for (var page = 1; ; page++)
		{
			var response = await _rest.GetTrades(new UsmartRecordsRequest
			{
				Exchange = UsmartExchangeTypes.All,
				Page = page,
				PageSize = 100,
				BeginTime = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				EndTime = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			}, cancellationToken);
			var items = response.Data?.Items ?? [];
			result.AddRange(items.Where(trade => trade != null));
			if (items.Length < 100 || result.Count >= (response.Data?.Total ?? 0))
				break;
		}
		return result.ToArray();
	}

	private async Task<UsmartOrder[]> LoadTodayOrders(CancellationToken cancellationToken)
	{
		var result = new List<UsmartOrder>();
		for (var page = 1; ; page++)
		{
			var response = await _rest.GetTodayOrders(new UsmartPagedRequest
			{
				Exchange = UsmartExchangeTypes.All,
				Page = page,
				PageSize = 100,
			}, cancellationToken);
			var items = response.Data?.Items ?? [];
			foreach (var order in items.Where(order => order?.OrderId.IsEmpty() == false))
				_orders[order.OrderId] = order;
			result.AddRange(items.Where(order => order != null));
			if (items.Length < 100 || result.Count >= (response.Data?.Total ?? 0))
				break;
		}
		return result.ToArray();
	}

	private async ValueTask ProcessOrder(UsmartOrder order, long originalTransactionId,
		bool isLookup, bool isForced, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return;
		_orders[order.OrderId] = order;
		var state = order.Status.ToOrderState();
		var balance = state is OrderStates.Done or OrderStates.Failed
			? 0 : Math.Max(0, order.Quantity - order.FilledQuantity);
		var serverTime = GetOrderTime(order);
		var signature = $"{order.Status}|{order.Quantity}|{order.FilledQuantity}|" +
			$"{order.AveragePrice}|{order.Price}|{serverTime:O}";
		if (!isForced && _orderSignatures.TryGetValue(order.OrderId, out var previous) &&
			previous == signature)
			return;
		_orderSignatures[order.OrderId] = signature;
		var transactionId = _orderTransactions.TryGetValue(order.OrderId,
			out var knownTransactionId) ? knownTransactionId : 0;
		var originId = isLookup ? originalTransactionId
			: transactionId != 0 ? transactionId : originalTransactionId;
		if (!isLookup && order.Status is 6 or 7 &&
			_cancelTransactions.TryGetValue(order.OrderId, out var cancelTransactionId))
			originId = cancelTransactionId;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			PortfolioName = ResolvePortfolio(null),
			SecurityId = order.StockCode.ToSecurityId(order.Exchange),
			Side = order.Side == 1 ? Sides.Sell : Sides.Buy,
			OrderType = order.Instruction.EqualsIgnoreCase("w")
				? OrderTypes.Market : OrderTypes.Limit,
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = balance,
			OrderState = state,
			TimeInForce = TimeInForce.PutInQueue,
			AveragePrice = order.AveragePrice > 0 ? order.AveragePrice : null,
			ServerTime = serverTime,
			Condition = CreateCondition(order),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.StatusName.IsEmpty(
					$"uSMART order entered state {order.Status}.")) : null,
		}, cancellationToken);
		if (state is OrderStates.Done or OrderStates.Failed)
			_cancelTransactions.Remove(order.OrderId);
	}

	private async ValueTask ProcessTrade(UsmartTrade trade, long originalTransactionId,
		bool isLookup, CancellationToken cancellationToken)
	{
		if (trade == null || trade.RecordId <= 0 || trade.OrderId <= 0 ||
			trade.Status != 1 || trade.Price <= 0 || trade.Quantity <= 0)
			return;
		var wasReported = _reportedTrades.Contains(trade.RecordId);
		_reportedTrades.Add(trade.RecordId);
		if (wasReported && !isLookup)
			return;
		var orderId = trade.OrderId.ToString(CultureInfo.InvariantCulture);
		var transactionId = _orderTransactions.TryGetValue(orderId,
			out var knownTransactionId) ? knownTransactionId : 0;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = isLookup ? originalTransactionId
				: transactionId != 0 ? transactionId : originalTransactionId,
			OrderStringId = orderId,
			TradeStringId = trade.RecordId.ToString(CultureInfo.InvariantCulture),
			PortfolioName = ResolvePortfolio(null),
			SecurityId = trade.StockCode.ToSecurityId(trade.Exchange),
			Side = trade.Side == 1 ? Sides.Sell : Sides.Buy,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			ServerTime = trade.Time.ToUtc(trade.Exchange, CurrentTime),
		}, cancellationToken);
	}

	private async Task SendPortfolioSnapshot(long originalTransactionId, string portfolioName,
		bool isLookup, CancellationToken cancellationToken)
	{
		var account = ResolvePortfolio(portfolioName);
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account,
			BoardCode = DefaultMarket.ToBoard(),
		}, cancellationToken);

		var previousPositions = _positionIds.ToArray();
		_positionIds.Clear();
		var succeeded = 0;
		Exception lastError = null;
		foreach (var exchange in new[]
		{
			UsmartExchangeTypes.HongKong,
			UsmartExchangeTypes.UnitedStates,
			UsmartExchangeTypes.China,
		})
		{
			UsmartAsset asset;
			try
			{
				asset = (await _rest.GetAsset(exchange, cancellationToken)).Data;
				succeeded++;
			}
			catch (Exception error)
			{
				lastError = error;
				continue;
			}
			if (asset == null)
				continue;
			var currency = GetCurrency(exchange, asset.Currency);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = account,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.BeginValue, asset.TotalAsset.ToDecimalValue())
			.TryAdd(PositionChangeTypes.CurrentValue, asset.AvailableCash.ToDecimalValue(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, asset.FrozenCash.ToDecimalValue())
			.TryAdd(PositionChangeTypes.Currency, currency), cancellationToken);

			foreach (var holding in asset.Holdings ?? [])
			{
				if (holding?.StockCode.IsEmpty() != false)
					continue;
				var securityId = holding.StockCode.ToSecurityId(holding.Exchange);
				var key = $"{securityId.SecurityCode}@{securityId.BoardCode}";
				_positionIds[key] = securityId;
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = account,
					SecurityId = securityId,
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, holding.Quantity.ToDecimalValue(), true)
				.TryAdd(PositionChangeTypes.BlockedValue, holding.FrozenQuantity.ToDecimalValue())
				.TryAdd(PositionChangeTypes.AveragePrice,
					holding.AccurateCostPrice.ToDecimalValue() ?? holding.CostPrice.ToDecimalValue())
				.TryAdd(PositionChangeTypes.CurrentPrice, holding.LastPrice.ToDecimalValue())
				.TryAdd(PositionChangeTypes.UnrealizedPnL, holding.UnrealizedPnL.ToDecimalValue())
				.TryAdd(PositionChangeTypes.Currency, currency), cancellationToken);
			}
		}
		if (succeeded == 0 && lastError != null)
			throw lastError;
		if (!isLookup)
		{
			foreach (var previous in previousPositions.Where(previous =>
				!_positionIds.ContainsKey(previous.Key)))
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = account,
					SecurityId = previous.Value,
					ServerTime = CurrentTime,
				}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true), cancellationToken);
		}
	}

	private async Task<UsmartOrder> GetOrderForMutation(string orderId,
		CancellationToken cancellationToken)
	{
		if (_orders.TryGetValue(orderId, out var order))
			return order;
		await LoadTodayOrders(cancellationToken);
		return _orders.TryGetValue(orderId, out order) ? order : null;
	}

	private string GetOrderId(string orderId, long originalTransactionId)
	{
		if (orderId.IsEmpty() &&
			_transactionOrders.TryGetValue(originalTransactionId, out var mapped))
			orderId = mapped;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
		return orderId;
	}

	private static long ParseOrderId(string orderId)
		=> long.TryParse(orderId, NumberStyles.None, CultureInfo.InvariantCulture,
			out var result) ? result : throw new InvalidOperationException(
				$"uSMART order ID '{orderId}' is not numeric.");

	private static bool IsOrderMatch(UsmartOrder order, OrderStatusMessage filter)
	{
		if (order?.OrderId.IsEmpty() != false)
			return false;
		if (filter == null)
			return true;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.EqualsIgnoreCase(order.OrderId))
			return false;
		var securityId = order.StockCode.ToSecurityId(order.Exchange);
		if (filter.SecurityId != default &&
			!filter.SecurityId.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode))
			return false;
		if (filter.SecurityIds.Length > 0 && !filter.SecurityIds.Any(id =>
			id.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)))
			return false;
		if (filter.Side is Sides side && side != (order.Side == 1 ? Sides.Sell : Sides.Buy))
			return false;
		if (filter.Volume is decimal volume && volume != order.Quantity)
			return false;
		var state = order.Status.ToOrderState();
		if (filter.States.Length > 0 && !filter.States.Contains(state))
			return false;
		var time = GetOrderTime(order);
		if (filter.From is DateTime from && time < EnsureUtc(from))
			return false;
		return filter.To is not DateTime to || time <= EnsureUtc(to);
	}

	private static DateTime GetOrderTime(UsmartOrder order)
	{
		var value = order?.CreateTime;
		if (order?.CreateDate.IsEmpty() == false && value?.Contains('-') != true)
			value = $"{order.CreateDate} {value}";
		return value.ToUtc(order?.Exchange ?? 0, DateTime.UtcNow);
	}

	private static UsmartOrderCondition CreateCondition(UsmartOrder order)
		=> order == null ? null : new()
		{
			Instruction = FromWire(order.Instruction),
			Session = Enum.IsDefined(typeof(UsmartTradingSessions), order.Session)
				? (UsmartTradingSessions)order.Session : UsmartTradingSessions.Regular,
			IsFractional = order.Flag == "2",
		};

	private static CurrencyTypes? GetCurrency(UsmartExchangeTypes exchange, int? nativeCurrency)
		=> (nativeCurrency is int currency ? currency.ToCurrency() : null) ?? exchange switch
		{
			UsmartExchangeTypes.HongKong => CurrencyTypes.HKD,
			UsmartExchangeTypes.UnitedStates => CurrencyTypes.USD,
			_ => CurrencyTypes.CNY,
		};

	private static void ValidateOrder(UsmartExchangeTypes exchange,
		UsmartOrderInstructions instruction, decimal price, UsmartOrderCondition condition)
	{
		if (!Enum.IsDefined(instruction))
			throw new ArgumentOutOfRangeException(nameof(instruction), instruction,
				"Unknown uSMART order instruction.");
		if (instruction is not UsmartOrderInstructions.Market and
			not UsmartOrderInstructions.Auction && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"uSMART requires a positive price for this order instruction.");
		if (exchange is UsmartExchangeTypes.ShanghaiConnect or UsmartExchangeTypes.ShenzhenConnect &&
			instruction != UsmartOrderInstructions.Limit)
			throw new NotSupportedException("uSMART A-share orders support limit instruction only.");
		if (exchange == UsmartExchangeTypes.UnitedStates && instruction is not
			UsmartOrderInstructions.Limit and not UsmartOrderInstructions.Market)
			throw new NotSupportedException(
				"uSMART U.S. stock orders support limit and market instructions only.");
		if (condition.IsFractional && instruction != UsmartOrderInstructions.Limit)
			throw new NotSupportedException("uSMART fractional orders require a limit price.");
	}

	private static string ToWire(UsmartOrderInstructions instruction)
		=> instruction switch
		{
			UsmartOrderInstructions.Limit => "0",
			UsmartOrderInstructions.EnhancedLimit => "e",
			UsmartOrderInstructions.Auction => "d",
			UsmartOrderInstructions.AuctionLimit => "g",
			UsmartOrderInstructions.Market => "w",
			_ => throw new ArgumentOutOfRangeException(nameof(instruction), instruction, null),
		};

	private static UsmartOrderInstructions FromWire(string instruction)
		=> instruction?.ToLowerInvariant() switch
		{
			"e" => UsmartOrderInstructions.EnhancedLimit,
			"d" => UsmartOrderInstructions.Auction,
			"g" => UsmartOrderInstructions.AuctionLimit,
			"w" => UsmartOrderInstructions.Market,
			_ => UsmartOrderInstructions.Limit,
		};
}
