namespace StockSharp.Fugle;

public partial class FugleMessageAdapter
{
	private sealed class FugleLiveCandleState
	{
		public DateTime OpenTime { get; set; }
		public FugleStreamData Data { get; set; }
		public FugleSubscription Subscription { get; set; }
	}

	private readonly SynchronizedDictionary<string, FugleLiveCandleState> _liveCandleStates = new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await _restClient.GetSecurities(cancellationToken))
		{
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = instrument.ToSecurityId(),
				SecurityType = instrument.ToSecurityType(),
				Name = instrument.Name,
				ShortName = instrument.Symbol,
				Class = instrument.Market.IsEmpty(instrument.ContractType),
				Currency = CurrencyTypes.TWD,
				ExpiryDate = instrument.GetExpiry(),
			};

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var depth = mdMsg.MaxDepth ?? 5;
		if (depth is < 1 or > 5)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), depth, "Fugle provides five market-depth levels.");
		return ProcessRealtimeSubscription(mdMsg, DataType.MarketDepth, cancellationToken);
	}

	private async ValueTask ProcessRealtimeSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var security = mdMsg.SecurityId.ParseFugleSecurity();
		var subscription = CreateSubscription(mdMsg, security, security.ToSubscriptionChannel(dataType));

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.IsHistoryOnly())
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			var socket = await GetSocket(security.Kind, cancellationToken);
			await socket.Subscribe(subscription, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var existingSocket = security.Kind == FugleAssetKinds.Stock ? _stockSocket : _futuresSocket;
		if (existingSocket != null)
			await existingSocket.Unsubscribe(subscription, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var security = mdMsg.SecurityId.ParseFugleSecurity();
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToFugleTimeFrame(security.Kind);
		var subscription = CreateSubscription(mdMsg, security, "candles");

		if (!mdMsg.IsSubscribe)
		{
			_liveCandleStates.Remove(subscription.Key);
			var existingSocket = security.Kind == FugleAssetKinds.Stock ? _stockSocket : _futuresSocket;
			if (existingSocket != null)
				await existingSocket.Unsubscribe(subscription, cancellationToken);
			return;
		}

		var isHistoryRequested = mdMsg.IsHistoryOnly() || mdMsg.From != null || mdMsg.To != null;
		if (isHistoryRequested)
			await SendCandleHistory(mdMsg, security, timeFrame, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		if (timeFrame != TimeSpan.FromMinutes(1))
			throw new NotSupportedException("Fugle streams one-minute candles only. Use StockSharp candle aggregation for larger realtime intervals.");

		var socket = await GetSocket(security.Kind, cancellationToken);
		await socket.Subscribe(subscription, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask SendCandleHistory(MarketDataMessage mdMsg, FugleSecurityInfo security,
		TimeSpan timeFrame, CancellationToken cancellationToken)
	{
		IEnumerable<FugleCandle> candles = (await _restClient.GetCandles(
			security, timeFrame, mdMsg.From, mdMsg.To, cancellationToken))
			.Where(candle => candle?.Date.ParseFugleTime() != null)
			.OrderBy(candle => candle.Date.ParseFugleTime());

		if (mdMsg.Count is long count)
			candles = candles.TakeLast((int)Math.Min(Math.Max(count, 0), int.MaxValue))
				.OrderBy(candle => candle.Date.ParseFugleTime());

		foreach (var candle in candles)
		{
			if (candle.Date.ParseFugleTime() is not DateTime openTime)
				continue;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = openTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
	}

	private ValueTask OnDataReceived(FugleSubscription subscription, FugleStreamData data,
		CancellationToken cancellationToken)
		=> subscription.Channel.ToLowerInvariant() switch
		{
			"trades" => ProcessTrades(subscription, data, cancellationToken),
			"books" => ProcessBook(subscription, data, cancellationToken),
			"aggregates" or "indices" => ProcessLevel1(subscription, data, cancellationToken),
			"candles" => ProcessLiveCandle(subscription, data, cancellationToken),
			_ => default,
		};

	private async ValueTask ProcessTrades(FugleSubscription subscription, FugleStreamData data,
		CancellationToken cancellationToken)
	{
		var serverTime = data.Time.ToFugleTime() ?? CurrentTime;
		if (data.Trades is { Length: > 0 })
		{
			for (var i = 0; i < data.Trades.Length; i++)
			{
				var trade = data.Trades[i];
				if (trade.Price <= 0)
					continue;
				await SendTick(subscription, trade.Price, trade.Size, trade.Serial.IsEmpty(data.Serial), serverTime, i,
					cancellationToken);
			}
		}
		else if (data.Price is > 0)
		{
			await SendTick(subscription, data.Price.Value, data.Size ?? 0, data.Serial, serverTime, 0,
				cancellationToken);
		}
	}

	private ValueTask SendTick(FugleSubscription subscription, decimal price, decimal volume, string serial,
		DateTime serverTime, int index, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TradeStringId = serial.IsEmpty()
				? $"{subscription.Symbol}:{serverTime.Ticks}:{index}"
				: index == 0 ? serial : $"{serial}:{index}",
			TradePrice = price,
			TradeVolume = volume > 0 ? volume : null,
			ServerTime = serverTime,
		}, cancellationToken);

	private ValueTask ProcessBook(FugleSubscription subscription, FugleStreamData data,
		CancellationToken cancellationToken)
	{
		var serverTime = data.Time.ToFugleTime() ?? data.LastUpdated.ToFugleTime() ?? CurrentTime;
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = serverTime,
			Bids = [.. (data.Bids ?? []).Where(level => level.Price > 0 && level.Size >= 0)
				.Select(level => new QuoteChange(level.Price, level.Size))],
			Asks = [.. (data.Asks ?? []).Where(level => level.Price > 0 && level.Size >= 0)
				.Select(level => new QuoteChange(level.Price, level.Size))],
		}, cancellationToken);
	}

	private async ValueTask ProcessLevel1(FugleSubscription subscription, FugleStreamData data,
		CancellationToken cancellationToken)
	{
		var lastTrade = data.LastTrade;
		var serverTime = data.LastUpdated.ToFugleTime() ?? lastTrade?.Time.ToFugleTime() ?? data.Time.ToFugleTime() ?? CurrentTime;
		var bid = data.Bids?.Where(level => level.Price > 0).OrderByDescending(level => level.Price).FirstOrDefault();
		var ask = data.Asks?.Where(level => level.Price > 0).OrderBy(level => level.Price).FirstOrDefault();
		var lastPrice = data.Index ?? data.LastPrice ?? lastTrade?.Price ?? data.ClosePrice;
		var lastVolume = data.LastSize ?? lastTrade?.Size;
		var level1 = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = serverTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, lastPrice)
		.TryAdd(Level1Fields.LastTradeVolume, lastVolume)
		.TryAdd(Level1Fields.LastTradeTime, lastPrice != null ? serverTime : null)
		.TryAdd(Level1Fields.OpenPrice, data.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, data.HighPrice)
		.TryAdd(Level1Fields.LowPrice, data.LowPrice)
		.TryAdd(Level1Fields.ClosePrice, data.PreviousClose)
		.TryAdd(Level1Fields.AveragePrice, data.AveragePrice)
		.TryAdd(Level1Fields.Volume, data.Total?.TradeVolume ?? data.Volume)
		.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
		.TryAdd(Level1Fields.BestBidVolume, bid?.Size)
		.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
		.TryAdd(Level1Fields.BestAskVolume, ask?.Size)
		.TryAdd(Level1Fields.BidsVolume, data.Bids?.Sum(level => level.Size))
		.TryAdd(Level1Fields.AsksVolume, data.Asks?.Sum(level => level.Size));

		if (level1.Changes.Count > 0)
			await SendOutMessageAsync(level1, cancellationToken);
	}

	private async ValueTask ProcessLiveCandle(FugleSubscription subscription, FugleStreamData data,
		CancellationToken cancellationToken)
	{
		if (data.Date.ParseFugleTime() is not DateTime openTime || data.Open == null || data.High == null ||
			data.Low == null || data.Close == null)
			return;

		if (_liveCandleStates.TryGetValue(subscription.Key, out var previous) && previous.OpenTime < openTime)
			await SendLiveCandle(previous.Subscription, previous.Data, previous.OpenTime, CandleStates.Finished, cancellationToken);

		_liveCandleStates[subscription.Key] = new()
		{
			OpenTime = openTime,
			Data = data,
			Subscription = subscription,
		};
		await SendLiveCandle(subscription, data, openTime, CandleStates.Active, cancellationToken);
	}

	private ValueTask SendLiveCandle(FugleSubscription subscription, FugleStreamData data, DateTime openTime,
		CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TypedArg = TimeSpan.FromMinutes(1),
			OpenTime = openTime,
			OpenPrice = data.Open.Value,
			HighPrice = data.High.Value,
			LowPrice = data.Low.Value,
			ClosePrice = data.Close.Value,
			TotalVolume = data.Volume ?? 0,
			State = state,
		}, cancellationToken);

	private static FugleSubscription CreateSubscription(MarketDataMessage mdMsg, FugleSecurityInfo security,
		string channel)
		=> new()
		{
			Channel = channel,
			Symbol = security.Symbol,
			IsAfterHours = security.IsAfterHours,
			TransactionId = mdMsg.IsSubscribe ? mdMsg.TransactionId : mdMsg.OriginalTransactionId,
			SecurityId = mdMsg.SecurityId,
		};
}
