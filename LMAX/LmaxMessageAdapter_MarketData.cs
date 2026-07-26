namespace StockSharp.LMAX;

partial class LmaxMessageAdapter
{
	private readonly SynchronizedPairSet<string, SecurityId> _instrumentIds = [];

	private async Task FillSecIds(CancellationToken cancellationToken)
	{
		var response = await _httpClient.GetInstrumentDataAsync(cancellationToken);

		foreach (var instrument in response.Instruments)
		{
			_instrumentIds[instrument.InstrumentId] = new()
			{
				SecurityCode = instrument.Symbol,
				BoardCode = BoardCodes.Lmax,
				Native = instrument.InstrumentId,
			};
		}
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var response = await _httpClient.GetInstrumentDataAsync(cancellationToken);

		foreach (var instrument in response.Instruments)
		{
			var securityId = new SecurityId
			{
				SecurityCode = instrument.Symbol,
				BoardCode = BoardCodes.Lmax,
				Native = instrument.InstrumentId,
			};

			_instrumentIds[instrument.InstrumentId] = securityId;

			await SendOutMessageAsync(new SecurityMessage
			{
				SecurityId = securityId,
				PriceStep = instrument.PriceIncrement?.ToDecimal(),
				VolumeStep = instrument.QuantityIncrement?.ToDecimal(),
				Currency = instrument.Currency?.To<CurrencyTypes?>(),
				SecurityType = instrument.AssetClass.ToSecurityType(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var instrumentId = GetInstrumentId(mdMsg.SecurityId);

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTickerAsync(instrumentId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeTickerAsync(instrumentId, cancellationToken);
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var instrumentId = GetInstrumentId(mdMsg.SecurityId);

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeOrderBookAsync(instrumentId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeOrderBookAsync(instrumentId, cancellationToken);
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var instrumentId = GetInstrumentId(mdMsg.SecurityId);

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTradesAsync(instrumentId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeTradesAsync(instrumentId, cancellationToken);
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		}
	}

	private ValueTask OnOrderBookReceived(WsOrderBookMessage msg, CancellationToken cancellationToken)
	{
		var secId = GetSecurityId(msg.InstrumentId);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			Bids = msg.Bids?.Select(p => new QuoteChange(
				p.Price.ToDecimal() ?? throw new InvalidDataException(
					"LMAX bid price is missing."),
				p.Quantity.ToDecimal() ?? throw new InvalidDataException(
					"LMAX bid quantity is missing."))).ToArray() ?? [],
			Asks = msg.Asks?.Select(p => new QuoteChange(
				p.Price.ToDecimal() ?? throw new InvalidDataException(
					"LMAX ask price is missing."),
				p.Quantity.ToDecimal() ?? throw new InvalidDataException(
					"LMAX ask quantity is missing."))).ToArray() ?? [],
			ServerTime = msg.Timestamp,
		}, cancellationToken);
	}

	private ValueTask OnTickerReceived(
		WsOrderBookMessage msg,
		CancellationToken cancellationToken)
	{
		var secId = GetSecurityId(msg.InstrumentId);
		var bestBid = msg.Bids?.FirstOrDefault();
		var bestAsk = msg.Asks?.FirstOrDefault();

		var l1Msg = new Level1ChangeMessage
		{
			SecurityId = secId,
			ServerTime = msg.Timestamp,
		}
		.TryAdd(Level1Fields.BestBidPrice, bestBid?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, bestBid?.Quantity.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, bestAsk?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, bestAsk?.Quantity.ToDecimal())
		;

		if (l1Msg.Changes.Count > 0)
			return SendOutMessageAsync(l1Msg, cancellationToken);

		return default;
	}

	private async ValueTask OnTradeReceived(
		WsTradeEventMessage msg,
		CancellationToken cancellationToken)
	{
		var secId = GetSecurityId(msg.InstrumentId);

		foreach (var trade in msg.Trades ?? [])
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = secId,
				TradePrice = trade.Price.ToDecimal(),
				TradeVolume = trade.Quantity.ToDecimal(),
				ServerTime = msg.Timestamp,
			}, cancellationToken);
		}
	}

	private string GetInstrumentId(SecurityId securityId)
		=> _instrumentIds[securityId];

	private SecurityId GetSecurityId(string instrumentId)
	{
		if (_instrumentIds.TryGetValue(instrumentId, out var secId))
			return secId;

		return new() { Native = instrumentId, BoardCode = BoardCodes.Lmax };
	}
}
