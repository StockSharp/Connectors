namespace StockSharp.Webull;

partial class WebullMessageAdapter
{
	private async ValueTask ProcessStreamingSubscription(MarketDataMessage message, WebullMarketDataSubTypes[] subTypes, ConcurrentDictionary<long, SecurityId> subscriptions, int? depth, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var symbol = message.SecurityId.SecurityCode;
		if (message.IsSubscribe)
		{
			subscriptions[message.TransactionId] = message.SecurityId;
			await Send(HttpMethod.Post, "/openapi/market-data/streaming/subscribe", null, new SubscribeMarketDataRequest
			{
				SessionId = _streamSessionId,
				Symbols = [symbol],
				Category = WebullInstrumentCategories.UsStock,
				SubTypes = subTypes,
				Depth = depth,
			}, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		else
		{
			subscriptions.TryRemove(message.OriginalTransactionId, out _);
			await Send(HttpMethod.Post, "/openapi/market-data/streaming/unsubscribe", null, new UnsubscribeMarketDataRequest
			{
				SessionId = _streamSessionId,
				Symbols = [symbol],
				Category = WebullInstrumentCategories.UsStock,
				SubTypes = subTypes,
				UnsubscribeAll = false,
			}, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessStreamingSubscription(message, [WebullMarketDataSubTypes.Quote], _depthSubscriptions, message.MaxDepth ?? 10, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessStreamingSubscription(message, [WebullMarketDataSubTypes.Tick], _tickSubscriptions, null, cancellationToken);

	private async ValueTask OnMqttMessage(string topic, byte[] payload, CancellationToken cancellationToken)
	{
		switch (topic)
		{
			case "snapshot":
				var snapshot = WebullProtoDecoder.DecodeSnapshot(payload);
				if (snapshot.Basic is null)
					return;
				foreach (var pair in FindSubscriptions(_level1Subscriptions, snapshot.Basic.Symbol))
				{
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						OriginalTransactionId = pair.Key,
						SecurityId = pair.Value,
						ServerTime = ToTime(snapshot.TradeTime ?? snapshot.Basic.Timestamp),
					}
					.TryAdd(Level1Fields.LastTradePrice, snapshot.Price)
					.TryAdd(Level1Fields.OpenPrice, snapshot.Open)
					.TryAdd(Level1Fields.HighPrice, snapshot.High)
					.TryAdd(Level1Fields.LowPrice, snapshot.Low)
					.TryAdd(Level1Fields.ClosePrice, snapshot.PreviousClose)
					.TryAdd(Level1Fields.Volume, snapshot.Volume), cancellationToken);
				}
				break;

			case "quote":
				var quote = WebullProtoDecoder.DecodeQuote(payload);
				if (quote.Basic is null)
					return;
				foreach (var pair in FindSubscriptions(_level1Subscriptions, quote.Basic.Symbol))
				{
					var bid = quote.Bids.FirstOrDefault();
					var ask = quote.Asks.FirstOrDefault();
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						OriginalTransactionId = pair.Key,
						SecurityId = pair.Value,
						ServerTime = ToTime(quote.Basic.Timestamp),
					}
					.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
					.TryAdd(Level1Fields.BestBidVolume, bid?.Volume)
					.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
					.TryAdd(Level1Fields.BestAskVolume, ask?.Volume), cancellationToken);
				}
				foreach (var pair in FindSubscriptions(_depthSubscriptions, quote.Basic.Symbol))
				{
					await SendOutMessageAsync(new QuoteChangeMessage
					{
						OriginalTransactionId = pair.Key,
						SecurityId = pair.Value,
						ServerTime = ToTime(quote.Basic.Timestamp),
						Bids = quote.Bids.Select(v => new QuoteChange(v.Price, v.Volume)).ToArray(),
						Asks = quote.Asks.Select(v => new QuoteChange(v.Price, v.Volume)).ToArray(),
						State = QuoteChangeStates.SnapshotComplete,
					}, cancellationToken);
				}
				break;

			case "tick":
				var tick = WebullProtoDecoder.DecodeTick(payload);
				if (tick.Basic is null || tick.Price is null)
					return;
				foreach (var pair in FindSubscriptions(_tickSubscriptions, tick.Basic.Symbol))
				{
					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						OriginalTransactionId = pair.Key,
						SecurityId = pair.Value,
						TradePrice = tick.Price.Value,
						TradeVolume = tick.Volume,
						OriginSide = tick.Side?.Equals("BUY", StringComparison.OrdinalIgnoreCase) == true ? Sides.Buy : tick.Side?.Equals("SELL", StringComparison.OrdinalIgnoreCase) == true ? Sides.Sell : null,
						ServerTime = ToTime(tick.Time ?? tick.Basic.Timestamp),
					}, cancellationToken);
				}
				break;
		}
	}

	private async ValueTask OnTradeEvent(WebullTradeEvent tradeEvent, CancellationToken cancellationToken)
	{
		if (!tradeEvent.ContentType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || tradeEvent.Payload is null)
			return;
		TradeEventPayload data;
		try
		{
			data = JsonConvert.DeserializeObject<TradeEventPayload>(tradeEvent.Payload);
		}
		catch (JsonException)
		{
			return;
		}

		var orderId = data?.OrderId ?? data?.ClientOrderId;
		if (orderId is null)
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OrderStringId = orderId,
			PortfolioName = data.AccountId,
			SecurityId = new() { SecurityCode = data.Symbol, BoardCode = BoardCodes.Nasdaq },
			OrderState = ToOrderState(data.OrderStatus),
			ServerTime = tradeEvent.Timestamp > 0 ? tradeEvent.Timestamp.FromUnix(false) : DateTime.UtcNow,
		}, cancellationToken);
	}

	private ValueTask OnStreamError(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private static (long Key, SecurityId Value)[] FindSubscriptions(ConcurrentDictionary<long, SecurityId> subscriptions, string symbol)
		=> subscriptions.Where(p => p.Value.SecurityCode.Equals(symbol, StringComparison.OrdinalIgnoreCase)).Select(p => (p.Key, p.Value)).ToArray();

	private static DateTime ToTime(string value)
	{
		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
			return timestamp > 10_000_000_000 ? timestamp.FromUnix(false) : timestamp.FromUnix();
		return DateTime.UtcNow;
	}

	private static OrderStates ToOrderState(WebullOrderStatuses status)
		=> status switch
		{
			WebullOrderStatuses.New or WebullOrderStatuses.Pending or WebullOrderStatuses.Working or WebullOrderStatuses.Submitted or WebullOrderStatuses.PartialFilled or WebullOrderStatuses.PartiallyFilled => OrderStates.Active,
			WebullOrderStatuses.Filled => OrderStates.Done,
			WebullOrderStatuses.Canceled or WebullOrderStatuses.Cancelled or WebullOrderStatuses.Rejected or WebullOrderStatuses.Expired or WebullOrderStatuses.Failed => OrderStates.Failed,
			_ => OrderStates.Pending,
		};
}
