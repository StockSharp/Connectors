namespace StockSharp.Webull;

partial class WebullMessageAdapter
{
	private static StockOrderRequest CreateOrderRequest(long transactionId, string securityCode, Sides side, OrderTypes orderType, decimal volume, decimal price, TimeInForce? timeInForce = null)
	{
		return new()
		{
			ClientOrderId = transactionId.ToString(),
			ComboType = WebullComboTypes.Normal,
			InstrumentType = WebullInstrumentTypes.Equity,
			EntrustType = WebullEntrustTypes.Quantity,
			TradingSession = WebullTradingSessions.Core,
			Symbol = securityCode,
			Market = WebullMarkets.Us,
			Side = side == Sides.Buy ? WebullSides.Buy : WebullSides.Sell,
			OrderType = orderType == OrderTypes.Market ? WebullOrderTypes.Market : WebullOrderTypes.Limit,
			Quantity = volume.ToString(CultureInfo.InvariantCulture),
			TimeInForce = timeInForce == TimeInForce.PutInQueue ? WebullTimeInForces.GoodTillCanceled : WebullTimeInForces.Day,
			LimitPrice = orderType == OrderTypes.Market ? null : price.ToString(CultureInfo.InvariantCulture),
		};
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var order = CreateOrderRequest(message.TransactionId, message.SecurityId.SecurityCode, message.Side, message.OrderType ?? OrderTypes.Limit, message.Volume, message.Price, message.TimeInForce);
		var account = message.PortfolioName.IsEmpty() ? Account : message.PortfolioName;
		await Send(HttpMethod.Post, "/openapi/trade/order/place", null, new PlaceOrderRequest
		{
			AccountId = account,
			NewOrders = [order],
		}, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderStringId = order.ClientOrderId,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var account = message.PortfolioName.IsEmpty() ? Account : message.PortfolioName;
		await Send(HttpMethod.Post, "/openapi/trade/order/cancel", null, new CancelOrderRequest
		{
			AccountId = account,
			ClientOrderId = message.OrderStringId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var accounts = Account.IsEmpty()
			? await Send<AccountInfo[]>(HttpMethod.Get, "/openapi/account/list", null, null, cancellationToken) ?? []
			: [new AccountInfo { AccountId = Account, AccountNumber = Account }];

		foreach (var account in accounts)
		{
			var id = account.AccountId;
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = id,
				BoardCode = BoardCodes.Nasdaq,
				OriginalTransactionId = message.TransactionId,
			}, cancellationToken);

			var query = new AccountQuery { AccountId = id };
			var balance = await Send<AccountBalance>(HttpMethod.Get, "/openapi/assets/balance", query, null, cancellationToken);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = id,
				SecurityId = SecurityId.Money,
				OriginalTransactionId = message.TransactionId,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance?.TotalCashBalance)
			.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance?.CurrencyAssets?.FirstOrDefault()?.BuyingPower)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, balance?.UnrealizedProfitLoss)
			.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.USD), cancellationToken);

			foreach (var position in await Send<AccountPosition[]>(HttpMethod.Get, "/openapi/assets/positions", query, null, cancellationToken) ?? [])
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = id,
					SecurityId = new() { SecurityCode = position.Symbol, BoardCode = BoardCodes.Nasdaq },
					OriginalTransactionId = message.TransactionId,
					ServerTime = DateTime.UtcNow,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity)
				.TryAdd(PositionChangeTypes.AveragePrice, position.CostPrice)
				.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfitLoss), cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}
}
