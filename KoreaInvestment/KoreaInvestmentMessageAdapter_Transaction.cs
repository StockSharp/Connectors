namespace StockSharp.KoreaInvestment;

public partial class KoreaInvestmentMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		if (regMsg.Volume <= 0 || regMsg.Volume != decimal.Truncate(regMsg.Volume))
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume, "KIS order quantity must be a positive integer.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException($"KIS does not support StockSharp order type '{orderType}'.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price, "KIS limit order price must be positive.");

		var condition = regMsg.Condition as KoreaInvestmentOrderCondition ?? new();
		var security = ResolveSecurity(regMsg.SecurityId, null, condition.Market);
		var quantity = Format(regMsg.Volume);
		var price = orderType == OrderTypes.Market ? "0" : Format(regMsg.Price);
		KisOperations operation;
		object request;

		switch (security.AssetClass)
		{
			case KisAssetClasses.DomesticStock:
				operation = regMsg.Side == Sides.Buy ? KisOperations.DomesticBuy : KisOperations.DomesticSell;
				request = new KisDomesticOrderRequest
				{
					AccountNumber = AccountNumber,
					ProductCode = ProductCode,
					ProductNumber = security.Code,
					OrderDivision = condition.Division.ToDomesticOrderDivision(orderType),
					Quantity = quantity,
					Price = price,
					ExchangeCode = security.OrderExchangeCode,
					SellType = regMsg.Side == Sides.Sell ? "01" : string.Empty,
					ConditionPrice = "0",
				};
				break;

			case KisAssetClasses.DomesticDerivative:
			{
				var codes = condition.Division.ToDerivativeOrderCodes(condition.TimeInForce, orderType);
				operation = condition.IsNight ? KisOperations.DerivativeNightOrder : KisOperations.DerivativeOrder;
				request = new KisDerivativeOrderRequest
				{
					AccountNumber = AccountNumber,
					ProductCode = ProductCode,
					SideCode = regMsg.Side == Sides.Buy ? "02" : "01",
					ProductNumber = security.Code,
					Quantity = quantity,
					Price = price,
					QuoteTypeCode = codes.quoteType,
					QuoteConditionCode = codes.condition,
					OrderDivisionCode = codes.division,
				};
				break;
			}

			case KisAssetClasses.OverseasStock:
				operation = security.ToOverseasOrderOperation(regMsg.Side);
				request = new KisOverseasOrderRequest
				{
					AccountNumber = AccountNumber,
					ProductCode = ProductCode,
					ExchangeCode = security.OrderExchangeCode,
					ProductNumber = security.Code,
					Quantity = quantity,
					Price = price,
					SellType = regMsg.Side == Sides.Sell ? "00" : string.Empty,
					OrderDivision = condition.Division.ToOverseasOrderDivision(orderType),
				};
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(security), security.AssetClass, null);
		}

		var result = await _rest.PlaceOrder(operation, request, cancellationToken);
		var orderNumber = result.OrderNumber.ThrowIfEmpty(nameof(result.OrderNumber));
		var tracker = new OrderTracker
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = security.ToSecurityId(),
			Security = security,
			OrderNumber = orderNumber,
			OrganizationNumber = result.OrganizationNumber,
			Side = regMsg.Side,
			OrderType = orderType,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
			Condition = condition,
		};
		_orders[orderNumber] = tracker;
		_orderFills[orderNumber] = 0;

		await SendOutMessageAsync(CreateOrderMessage(tracker, regMsg.TransactionId, OrderStates.Active,
			regMsg.Volume, result.OrderTime.ToKisUtc(null, security)), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderNumber = cancelMsg.OrderStringId.ThrowIfEmpty(nameof(cancelMsg.OrderStringId));
		_orders.TryGetValue(orderNumber, out var tracker);
		var condition = cancelMsg.Condition as KoreaInvestmentOrderCondition ?? tracker?.Condition ?? new();
		var security = tracker?.Security ?? ResolveSecurity(cancelMsg.SecurityId, null, condition.Market);
		var organizationNumber = tracker?.OrganizationNumber ?? string.Empty;
		KisOperations operation;
		object request;

		switch (security.AssetClass)
		{
			case KisAssetClasses.DomesticStock:
				operation = KisOperations.DomesticCancel;
				request = new KisDomesticCancelRequest
				{
					AccountNumber = AccountNumber,
					ProductCode = ProductCode,
					OrganizationNumber = organizationNumber,
					OriginalOrderNumber = orderNumber,
					OrderDivision = condition.Division.ToDomesticOrderDivision(tracker?.OrderType ?? OrderTypes.Limit),
					Quantity = "0",
					Price = "0",
					ExchangeCode = security.OrderExchangeCode,
					ConditionPrice = "0",
				};
				break;

			case KisAssetClasses.DomesticDerivative:
				operation = condition.IsNight ? KisOperations.DerivativeNightCancel : KisOperations.DerivativeCancel;
				request = new KisDerivativeCancelRequest
				{
					AccountNumber = AccountNumber,
					ProductCode = ProductCode,
					OriginalOrderNumber = orderNumber,
				};
				break;

			case KisAssetClasses.OverseasStock:
				operation = KisOperations.OverseasCancel;
				request = new KisOverseasCancelRequest
				{
					AccountNumber = AccountNumber,
					ProductCode = ProductCode,
					ExchangeCode = security.OrderExchangeCode,
					ProductNumber = security.Code,
					OriginalOrderNumber = orderNumber,
				};
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(security), security.AssetClass, null);
		}

		await _rest.CancelOrder(operation, request, cancellationToken);
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

		var from = statusMsg.From?.UtcKind() ?? DateTime.UtcNow.AddDays(-7);
		var to = statusMsg.To?.UtcKind() ?? DateTime.UtcNow;
		await SendOrderSnapshot(statusMsg.TransactionId, from, to, cancellationToken, statusMsg.Count);

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

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = PortfolioName,
			BoardCode = ProductCode == "03" ? "KRX-FUT" : "KRX",
		}, cancellationToken);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (ProductCode == "03")
		{
			await SendPositions(await _rest.GetDerivativePositions(cancellationToken), originalTransactionId, cancellationToken);
			return;
		}

		await SendPositions(await _rest.GetDomesticPositions(cancellationToken), originalTransactionId, cancellationToken);
		foreach (var market in new[]
		{
			KoreaInvestmentMarkets.Nasdaq,
			KoreaInvestmentMarkets.HongKong,
			KoreaInvestmentMarkets.Shanghai,
			KoreaInvestmentMarkets.Shenzhen,
			KoreaInvestmentMarkets.Tokyo,
			KoreaInvestmentMarkets.Hanoi,
			KoreaInvestmentMarkets.HoChiMinh,
		})
		{
			try
			{
				await SendPositions(await _rest.GetOverseasPositions(market, cancellationToken),
					originalTransactionId, cancellationToken);
			}
			catch (HttpRequestException ex)
			{
				this.AddWarningLog("KIS {0} balance is unavailable: {1}", market, ex.Message);
			}
		}
	}

	private async ValueTask SendPositions(IEnumerable<KisPosition> positions, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var position in positions ?? [])
		{
			var securityId = position.Security.ToSecurityId();
			_securityInfos[GetSecurityKey(securityId)] = position.Security;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = securityId,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
			.TryAdd(PositionChangeTypes.Currency, position.Security.Currency), cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshot(long originalTransactionId, DateTime from, DateTime to,
		CancellationToken cancellationToken, long? count = null)
	{
		IEnumerable<KisOrderExecution> executions;
		if (ProductCode == "03")
			executions = await _rest.GetDerivativeExecutions(from, to, cancellationToken);
		else
		{
			var domestic = await _rest.GetDomesticExecutions(from, to, cancellationToken);
			KisOrderExecution[] overseas = [];
			try
			{
				overseas = await _rest.GetOverseasExecutions(from, to, cancellationToken);
			}
			catch (HttpRequestException ex)
			{
				this.AddWarningLog("KIS overseas executions are unavailable: {0}", ex.Message);
			}
			executions = domestic.Concat(overseas);
		}

		if (count is > 0)
			executions = executions.OrderByDescending(e => e.Time).Take((int)Math.Min(count.Value, int.MaxValue));
		foreach (var execution in executions.OrderBy(e => e.Time))
			await ProcessExecution(execution, originalTransactionId, cancellationToken);
	}

	private async ValueTask ProcessExecution(KisOrderExecution execution, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (execution.OrderNumber.IsEmpty())
			return;

		_orders.TryGetValue(execution.OrderNumber, out var tracker);
		var volume = execution.OrderQuantity > 0 ? execution.OrderQuantity : tracker?.Volume ?? 0;
		var state = execution.IsCanceled || volume > 0 && execution.FilledQuantity >= volume
			? OrderStates.Done : OrderStates.Active;
		var securityId = tracker?.SecurityId ?? execution.Security.ToSecurityId();
		var origin = originalTransactionId != 0 ? originalTransactionId : tracker?.TransactionId ?? _orderStatusSubscriptionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = originalTransactionId != 0 ? tracker?.TransactionId ?? 0 : 0,
			OrderStringId = execution.OrderNumber,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = execution.Side,
			OrderType = tracker?.OrderType ?? OrderTypes.Limit,
			OrderPrice = execution.OrderPrice ?? tracker?.Price ?? 0,
			OrderVolume = volume,
			Balance = Math.Max(0, volume - execution.FilledQuantity),
			AveragePrice = execution.AveragePrice,
			OrderState = state,
			ServerTime = execution.Time,
			Condition = tracker?.Condition,
		}, cancellationToken);

		_orderFills.TryGetValue(execution.OrderNumber, out var previousFill);
		if (execution.FilledQuantity > previousFill && execution.AveragePrice is > 0)
		{
			var tradeId = $"{execution.OrderNumber}:{Format(execution.FilledQuantity)}";
			if (_tradeIds.TryAdd(tradeId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = origin,
					OrderStringId = execution.OrderNumber,
					TradeStringId = tradeId,
					SecurityId = securityId,
					PortfolioName = PortfolioName,
					Side = execution.Side,
					TradePrice = execution.AveragePrice,
					TradeVolume = execution.FilledQuantity - previousFill,
					ServerTime = execution.Time,
				}, cancellationToken);
			}
		}
		_orderFills[execution.OrderNumber] = Math.Max(previousFill, execution.FilledQuantity);
	}

	private async ValueTask ProcessOrderNotice(KisRealtimeOrderNotice notice, CancellationToken cancellationToken)
	{
		if (notice.OrderNumber.IsEmpty())
			return;
		_orders.TryGetValue(notice.OrderNumber, out var tracker);
		var origin = tracker?.TransactionId ?? _orderStatusSubscriptionId;
		if (origin == 0)
			return;

		var security = tracker?.Security ?? notice.Channel switch
		{
			KisRealtimeChannels.DomesticOrderNotice => KisSecurityInfo.Create(notice.Symbol, KoreaInvestmentMarkets.Krx, SecurityTypes.Stock),
			KisRealtimeChannels.DerivativeOrderNotice => KisSecurityInfo.Create(notice.Symbol, KoreaInvestmentMarkets.KrxDerivatives, null),
			_ => KisSecurityInfo.Create(notice.Symbol, KoreaInvestmentMarkets.Nasdaq, SecurityTypes.Stock),
		};
		var securityId = tracker?.SecurityId ?? security.ToSecurityId();
		_orderFills.TryGetValue(notice.OrderNumber, out var previousFill);
		var cumulativeFill = previousFill + (notice.IsExecution ? notice.FilledQuantity : 0);
		var volume = notice.OrderQuantity > 0 ? notice.OrderQuantity : tracker?.Volume ?? cumulativeFill;
		var state = notice.IsRejected ? OrderStates.Failed
			: volume > 0 && cumulativeFill >= volume ? OrderStates.Done : OrderStates.Active;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderStringId = notice.OrderNumber,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = notice.Side,
			OrderType = tracker?.OrderType ?? OrderTypes.Limit,
			OrderPrice = notice.OrderPrice ?? tracker?.Price ?? 0,
			OrderVolume = volume,
			Balance = Math.Max(0, volume - cumulativeFill),
			AveragePrice = notice.FillPrice,
			OrderState = state,
			ServerTime = notice.ServerTime,
			Condition = tracker?.Condition,
			Error = notice.IsRejected ? new InvalidOperationException($"KIS order {notice.OrderNumber} was rejected.") : null,
		}, cancellationToken);

		if (notice.IsExecution && notice.FilledQuantity > 0 && notice.FillPrice is > 0)
		{
			var tradeId = $"{notice.OrderNumber}:{notice.ServerTime.Ticks}:{Format(notice.FilledQuantity)}";
			if (_tradeIds.TryAdd(tradeId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = origin,
					OrderStringId = notice.OrderNumber,
					TradeStringId = tradeId,
					SecurityId = securityId,
					PortfolioName = PortfolioName,
					Side = notice.Side,
					TradePrice = notice.FillPrice,
					TradeVolume = notice.FilledQuantity,
					ServerTime = notice.ServerTime,
				}, cancellationToken);
			}
		}
		_orderFills[notice.OrderNumber] = cumulativeFill;
	}

	private ExecutionMessage CreateOrderMessage(OrderTracker tracker, long originalTransactionId,
		OrderStates state, decimal balance, DateTime serverTime)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = tracker.TransactionId,
			OrderStringId = tracker.OrderNumber,
			SecurityId = tracker.SecurityId,
			PortfolioName = PortfolioName,
			Side = tracker.Side,
			OrderType = tracker.OrderType,
			OrderPrice = tracker.Price,
			OrderVolume = tracker.Volume,
			Balance = balance,
			OrderState = state,
			ServerTime = serverTime,
			Condition = tracker.Condition,
		};

	private static string Format(decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);
}
