namespace StockSharp.FubonNeo;

public partial class FubonNeoMessageAdapter
{
	private sealed class LiveCandleState
	{
		public DateTime OpenTime { get; set; }
		public FubonNeoStreamData Data { get; set; }
		public FubonNeoSubscription Subscription { get; set; }
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var instrument in await _client.GetSecuritiesAsync(cancellationToken))
		{
			CacheSecurity(instrument);
			var message = new SecurityMessage
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
			if (!message.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(message, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var depth = mdMsg.MaxDepth ?? 5;
		if (depth is < 1 or > 5)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), depth,
				"Fubon provides five market-depth levels.");
		return ProcessRealtimeSubscription(mdMsg, DataType.MarketDepth, cancellationToken);
	}

	private async ValueTask ProcessRealtimeSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var security = mdMsg.SecurityId.ParseFubonSecurity(mdMsg.SecurityType);
		CacheSecurity(security);
		var channel = security.ToSubscriptionChannel(dataType);
		if (RealtimeMode == FubonNeoRealtimeModes.Speed && channel == "Aggregates")
			throw new NotSupportedException("Fubon aggregate Level1 channels require Normal realtime mode.");
		var subscription = CreateSubscription(mdMsg, security, channel);

		if (!mdMsg.IsSubscribe)
		{
			await _client.UnsubscribeAsync(subscription);
			return;
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		await _client.SubscribeAsync(subscription, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		var security = mdMsg.SecurityId.ParseFubonSecurity(mdMsg.SecurityType);
		CacheSecurity(security);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToFubonTimeFrame(security.Kind);
		var subscription = CreateSubscription(mdMsg, security, "Candles");

		if (!mdMsg.IsSubscribe)
		{
			_liveCandleStates.Remove(subscription.Key);
			await _client.UnsubscribeAsync(subscription);
			return;
		}
		if (!mdMsg.IsHistoryOnly() && timeFrame != TimeSpan.FromMinutes(1))
			throw new NotSupportedException(
				"Fubon streams one-minute candles only. Use StockSharp aggregation for larger realtime intervals.");
		if (!mdMsg.IsHistoryOnly() && RealtimeMode == FubonNeoRealtimeModes.Speed)
			throw new NotSupportedException("Fubon realtime candles require Normal mode.");

		if (mdMsg.IsHistoryOnly() || mdMsg.From != null || mdMsg.To != null)
			await SendCandleHistory(mdMsg, security, timeFrame, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		await _client.SubscribeAsync(subscription, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask SendCandleHistory(MarketDataMessage mdMsg, FubonNeoSecurityInfo security,
		TimeSpan timeFrame, CancellationToken cancellationToken)
	{
		var candles = (await _client.GetCandlesAsync(security, timeFrame, mdMsg.From, mdMsg.To, cancellationToken))
			.Where(item => item?.Date.ParseFubonMarketTime() != null)
			.OrderBy(item => item.Date.ParseFubonMarketTime());
		if (mdMsg.From is DateTime from)
			candles = candles.Where(item => item.Date.ParseFubonMarketTime() >= NormalizeUtc(from)).OrderBy(item => item.Date);
		if (mdMsg.To is DateTime to)
			candles = candles.Where(item => item.Date.ParseFubonMarketTime() <= NormalizeUtc(to)).OrderBy(item => item.Date);

		var left = mdMsg.Count ?? long.MaxValue;
		foreach (var candle in candles)
		{
			if (candle.Date.ParseFubonMarketTime() is not DateTime openTime)
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
			if (--left <= 0)
				break;
		}
	}

	private async ValueTask OnMarketData(FubonNeoSubscription subscription, FubonNeoStreamData data,
		CancellationToken cancellationToken)
	{
		if (data == null)
			return;
		switch (subscription.Channel.ToLowerInvariant())
		{
			case "trades":
				await ProcessTrades(subscription, data, cancellationToken);
				break;
			case "books":
				await ProcessBook(subscription, data, cancellationToken);
				break;
			case "aggregates":
			case "indices":
				await ProcessLevel1(subscription, data, cancellationToken);
				break;
			case "candles":
				await ProcessLiveCandle(subscription, data, cancellationToken);
				break;
		}
	}

	private async ValueTask ProcessTrades(FubonNeoSubscription subscription, FubonNeoStreamData data,
		CancellationToken cancellationToken)
	{
		var serverTime = data.Time.ToFubonMarketTime() ?? CurrentTime;
		if (data.Trades is { Length: > 0 })
		{
			for (var index = 0; index < data.Trades.Length; index++)
			{
				var trade = data.Trades[index];
				if (trade.Price <= 0)
					continue;
				await SendTick(subscription, trade.Price, trade.Size, trade.Serial.IsEmpty(data.Serial),
					trade.Time.ToFubonMarketTime() ?? serverTime, index, cancellationToken);
			}
		}
		else if (data.Price is > 0)
		{
			await SendTick(subscription, data.Price.Value, data.Size ?? 0, data.Serial,
				serverTime, 0, cancellationToken);
		}
	}

	private ValueTask SendTick(FubonNeoSubscription subscription, decimal price, decimal volume,
		string serial, DateTime serverTime, int index, CancellationToken cancellationToken)
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

	private ValueTask ProcessBook(FubonNeoSubscription subscription, FubonNeoStreamData data,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = data.Time.ToFubonMarketTime() ?? data.LastUpdated.ToFubonMarketTime() ?? CurrentTime,
			Bids = [.. (data.Bids ?? []).Where(level => level.Price > 0 && level.Size >= 0)
				.Select(level => new QuoteChange(level.Price, level.Size))],
			Asks = [.. (data.Asks ?? []).Where(level => level.Price > 0 && level.Size >= 0)
				.Select(level => new QuoteChange(level.Price, level.Size))],
		}, cancellationToken);

	private async ValueTask ProcessLevel1(FubonNeoSubscription subscription, FubonNeoStreamData data,
		CancellationToken cancellationToken)
	{
		var lastTrade = data.LastTrade;
		var serverTime = data.LastUpdated.ToFubonMarketTime() ?? lastTrade?.Time.ToFubonMarketTime() ??
			data.Time.ToFubonMarketTime() ?? CurrentTime;
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

	private async ValueTask ProcessLiveCandle(FubonNeoSubscription subscription, FubonNeoStreamData data,
		CancellationToken cancellationToken)
	{
		if (data.Date.ParseFubonMarketTime() is not DateTime openTime || data.Open == null ||
			data.High == null || data.Low == null || data.Close == null)
			return;
		if (_liveCandleStates.TryGetValue(subscription.Key, out var previous) && previous.OpenTime < openTime)
			await SendLiveCandle(previous.Subscription, previous.Data, previous.OpenTime,
				CandleStates.Finished, cancellationToken);
		_liveCandleStates[subscription.Key] = new()
		{
			OpenTime = openTime,
			Data = data,
			Subscription = subscription,
		};
		await SendLiveCandle(subscription, data, openTime, CandleStates.Active, cancellationToken);
	}

	private ValueTask SendLiveCandle(FubonNeoSubscription subscription, FubonNeoStreamData data,
		DateTime openTime, CandleStates state, CancellationToken cancellationToken)
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

	private static FubonNeoSubscription CreateSubscription(MarketDataMessage message,
		FubonNeoSecurityInfo security, string channel)
		=> new()
		{
			Kind = security.Kind,
			Channel = channel,
			Symbol = security.Symbol,
			IsAfterHours = security.IsAfterHours,
			TransactionId = message.IsSubscribe ? message.TransactionId : message.OriginalTransactionId,
			SecurityId = message.SecurityId,
		};

	private static DateTime NormalizeUtc(DateTime value)
		=> value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
}
