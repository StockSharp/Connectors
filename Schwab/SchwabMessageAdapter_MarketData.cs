namespace StockSharp.Schwab;

partial class SchwabMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var response = await _client.Lookup(message.SecurityId.SecurityCode ?? string.Empty, cancellationToken);
		var types = message.GetSecurityTypes();
		var left = message.Count ?? long.MaxValue;

		foreach (var instrument in response?.Instruments ?? [])
		{
			var security = new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = new() { SecurityCode = instrument.Symbol, BoardCode = BoardCodes.Nasdaq },
				Name = instrument.Description,
				SecurityType = instrument.AssetType == SchwabAssetTypes.Etf ? SecurityTypes.Fund : SecurityTypes.Stock,
				Currency = CurrencyTypes.USD,
				PriceStep = 0.01m,
			};

			if (!security.IsMatch(message, types))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var from = message.From?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-1);
		var to = message.To?.ToUniversalTime() ?? DateTime.UtcNow;
		var timeFrame = message.GetTimeFrame();
		var response = await _client.GetCandles(message.SecurityId.SecurityCode, timeFrame, from, to, cancellationToken);
		var left = message.Count ?? long.MaxValue;

		foreach (var candle in response?.Candles ?? [])
		{
			var openTime = candle.Timestamp.FromUnix(false);
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = message.SecurityId,
				OpenTime = openTime,
				CloseTime = openTime + timeFrame,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_level1Subscriptions.Remove(message.OriginalTransactionId, out var securityId))
				await _streamer.Unsubscribe("LEVELONE_EQUITIES", securityId.SecurityCode, cancellationToken);
			return;
		}

		_level1Subscriptions.Add(message.TransactionId, message.SecurityId);
		await _streamer.Subscribe("LEVELONE_EQUITIES", message.SecurityId.SecurityCode, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_depthSubscriptions.Remove(message.OriginalTransactionId, out var subscription))
				await _streamer.Unsubscribe(subscription.service, subscription.securityId.SecurityCode, cancellationToken);
			return;
		}

		var service = message.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Nyse) ? "NYSE_BOOK" : "NASDAQ_BOOK";
		_depthSubscriptions.Add(message.TransactionId, (message.SecurityId, service));
		await _streamer.Subscribe(service, message.SecurityId.SecurityCode, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}
}
