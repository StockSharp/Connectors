namespace StockSharp.Webull;

partial class WebullMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		var instruments = await Send<InstrumentInfo[]>(HttpMethod.Get, "/openapi/instrument/stock/list", new InstrumentListQuery
		{
			Symbols = message.SecurityId.SecurityCode ?? string.Empty,
			Category = WebullInstrumentCategories.UsStock,
		}, null, cancellationToken);

		foreach (var item in instruments ?? [])
		{
			await SendOutMessageAsync(new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = new() { SecurityCode = item.Symbol, BoardCode = BoardCodes.Nasdaq },
				Name = item.Name,
				SecurityType = SecurityTypes.Stock,
				Currency = CurrencyTypes.USD,
				PriceStep = 0.01m,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessStreamingSubscription(message, [WebullMarketDataSubTypes.Snapshot, WebullMarketDataSubTypes.Quote], _level1Subscriptions, null, cancellationToken);
}
