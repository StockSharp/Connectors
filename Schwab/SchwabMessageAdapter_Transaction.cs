namespace StockSharp.Schwab;

partial class SchwabMessageAdapter
{
	private readonly SynchronizedDictionary<string, string> _accountHashes = new(StringComparer.OrdinalIgnoreCase);

	private async ValueTask OnAccountActivityReceived(AccountActivityContent activity, CancellationToken cancellationToken)
	{
		var payload = activity.Payload;
		if (payload.IsEmpty() || !payload.TrimStart().StartsWith('{'))
			return;

		AccountActivityPayload data;
		try
		{
			data = JsonConvert.DeserializeObject<AccountActivityPayload>(payload);
		}
		catch (JsonException)
		{
			return;
		}

		data = data?.Order ?? data;
		var orderId = data?.OrderId;
		if (orderId.IsEmpty())
			return;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OrderStringId = orderId,
			PortfolioName = activity.Account,
			SecurityId = new() { SecurityCode = data.Symbol, BoardCode = BoardCodes.Nasdaq },
			OrderState = (data.Status ?? data.OrderStatus).ToOrderState(),
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	private async Task<string> ResolveAccount(string account, CancellationToken cancellationToken)
	{
		if (!account.IsEmpty() && _accountHashes.TryGetValue(account, out var hash))
			return hash;

		foreach (var item in await _client.GetAccounts(cancellationToken) ?? [])
		{
			var data = item.Account;
			var number = data.AccountNumber;
			_accountHashes[number] = data.HashValue ?? item.HashValue ?? number;
		}

		if (!account.IsEmpty() && _accountHashes.TryGetValue(account, out hash))
			return hash;
		return _accountHashes.Values.FirstOrDefault() ?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var orderId = await _client.PlaceOrder(
			await ResolveAccount(message.PortfolioName, cancellationToken),
			SchwabExtensions.CreateOrderRequest(message.SecurityId.SecurityCode, message.Side, message.OrderType ?? OrderTypes.Limit, message.Volume, message.Price, message.TimeInForce),
			cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderStringId = orderId,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		await _client.CancelOrder(await ResolveAccount(message.PortfolioName, cancellationToken), orderId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		foreach (var item in await _client.GetAccounts(cancellationToken) ?? [])
		{
			var account = item.Account;
			var number = account.AccountNumber;
			_accountHashes[number] = account.HashValue ?? item.HashValue ?? number;
			var balances = account.Balances;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = number,
				SecurityId = SecurityId.Money,
				OriginalTransactionId = message.TransactionId,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balances?.CashBalance)
			.TryAdd(PositionChangeTypes.BuyOrdersMargin, balances?.BuyingPower)
			.TryAdd(PositionChangeTypes.RealizedPnL, balances?.LiquidationValue)
			.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.USD), cancellationToken);

			foreach (var position in account.Positions ?? [])
			{
				var quantity = (position.LongQuantity ?? 0) - (position.ShortQuantity ?? 0);
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = number,
					SecurityId = new() { SecurityCode = position.Instrument?.Symbol, BoardCode = BoardCodes.Nasdaq },
					OriginalTransactionId = message.TransactionId,
					ServerTime = DateTime.UtcNow,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, quantity)
				.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice)
				.TryAdd(PositionChangeTypes.CurrentPrice, position.MarketValue), cancellationToken);
			}
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var from = message.From?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-60);
		var to = message.To?.ToUniversalTime() ?? DateTime.UtcNow;
		foreach (var order in await _client.GetOrders(await ResolveAccount(message.PortfolioName, cancellationToken), from, to, cancellationToken) ?? [])
		{
			var leg = order.Legs?.FirstOrDefault();
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = message.TransactionId,
				OrderStringId = order.OrderId,
				PortfolioName = message.PortfolioName,
				SecurityId = new() { SecurityCode = leg?.Instrument?.Symbol, BoardCode = BoardCodes.Nasdaq },
				Side = leg?.Instruction is SchwabInstructions.Buy or SchwabInstructions.BuyToCover ? Sides.Buy : Sides.Sell,
				OrderType = order.OrderType == SchwabOrderTypes.Market ? OrderTypes.Market : OrderTypes.Limit,
				OrderPrice = order.Price ?? 0,
				OrderVolume = leg?.Quantity,
				Balance = order.RemainingQuantity,
				OrderState = order.Status.ToOrderState(),
				ServerTime = order.EnteredTime.ToUtcDateTime(),
			}, cancellationToken);
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}
}
