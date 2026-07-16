namespace StockSharp.Kiwoom;

public partial class KiwoomMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var definitions = await _rest.GetSecurities(cancellationToken);
		var sent = 0L;
		foreach (var definition in definitions)
		{
			var securityId = definition.Security.ToSecurityId();
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = securityId,
				SecurityType = definition.SecurityType,
				Currency = definition.Security.Currency,
				Name = definition.Name,
				ShortName = definition.ShortName,
			};
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			_securityInfos[GetSecurityKey(securityId)] = definition.Security;
			await SendOutMessageAsync(security, cancellationToken);
			sent++;
			if (lookupMsg.Count is > 0 && sent >= lookupMsg.Count.Value)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var previous))
				await UpdateNativeSubscription(previous, false, cancellationToken);
			return;
		}

		var security = ResolveSecurity(mdMsg.SecurityId);
		if (dataType == DataType.Level1)
		{
			var quote = await _rest.GetQuote(security, cancellationToken);
			await SendOutMessageAsync(CreateLevel1(mdMsg.TransactionId, mdMsg.SecurityId, quote), cancellationToken);
		}
		else if (dataType == DataType.MarketDepth)
		{
			var depth = await _rest.GetDepth(security, cancellationToken);
			await SendOutMessageAsync(CreateDepth(mdMsg.TransactionId, mdMsg.SecurityId, depth,
				Math.Max(1, mdMsg.MaxDepth ?? 10)), cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var subscription = new MarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Security = security,
			DataType = dataType,
			MaxDepth = Math.Max(1, mdMsg.MaxDepth ?? 10),
		};
		_marketSubscriptions[mdMsg.TransactionId] = subscription;
		await UpdateNativeSubscription(subscription, true, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var previous))
				await UpdateNativeSubscription(previous, false, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (!KiwoomExtensions.TimeFrames.Contains(timeFrame))
			throw new ArgumentOutOfRangeException(nameof(mdMsg), timeFrame, "Unsupported Kiwoom candle time frame.");
		var security = ResolveSecurity(mdMsg.SecurityId);
		foreach (var candle in await _rest.GetCandles(security, timeFrame, mdMsg.From, mdMsg.To, mdMsg.Count, cancellationToken))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = candle.OpenTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TotalPrice = candle.Turnover ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var subscription = new MarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Security = security,
			DataType = mdMsg.DataType2,
			TimeFrame = timeFrame,
		};
		_marketSubscriptions[mdMsg.TransactionId] = subscription;
		await UpdateNativeSubscription(subscription, true, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask UpdateNativeSubscription(MarketSubscription changed, bool isAdding,
		CancellationToken cancellationToken)
	{
		var isDepth = changed.DataType == DataType.MarketDepth;
		var hasOther = _marketSubscriptions.CachedValues.Any(subscription =>
			subscription.TransactionId != changed.TransactionId && IsSame(subscription.Security, changed.Security) &&
			(isDepth ? subscription.DataType == DataType.MarketDepth : subscription.DataType != DataType.MarketDepth));
		if (hasOther)
			return;
		var stream = GetStream(changed.Security);
		var type = changed.Security.AssetClass == KiwoomAssetClasses.DomesticStock
			? isDepth ? "0D" : "0B"
			: isDepth ? "FT" : "FE";
		if (isAdding)
			await stream.Subscribe(changed.Security, type, cancellationToken);
		else
			await stream.Unsubscribe(changed.Security, type, cancellationToken);
	}

	private KiwoomWebSocketClient GetStream(KiwoomSecurityInfo security)
		=> security.AssetClass switch
		{
			KiwoomAssetClasses.DomesticStock => _domesticStream ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk),
			KiwoomAssetClasses.UsStock => _usStream ?? throw new NotSupportedException("Kiwoom mock trading supports domestic KRX data only."),
			_ => throw new ArgumentOutOfRangeException(nameof(security), security.AssetClass, null),
		};

	private async ValueTask ProcessRealtimeTrade(KiwoomRealtimeMessage message, CancellationToken cancellationToken)
	{
		var security = ResolveRealtimeSecurity(message);
		var values = message.Data.Values;
		var serverTime = values.TradeDate.ToKiwoomUtc(values.TradeTime, security);
		var price = values.LastPrice.ToPrice() ?? 0;
		var volume = Math.Abs(values.TradeVolume.ToDecimal() ?? 0);
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(subscription =>
			IsSame(subscription.Security, security) && subscription.DataType != DataType.MarketDepth))
		{
			if (subscription.DataType == DataType.Level1)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = serverTime,
				}
				.TryAdd(Level1Fields.LastTradePrice, price)
				.TryAdd(Level1Fields.LastTradeVolume, volume)
				.TryAdd(Level1Fields.LastTradeTime, serverTime)
				.TryAdd(Level1Fields.OpenPrice, values.OpenPrice.ToPrice())
				.TryAdd(Level1Fields.HighPrice, values.HighPrice.ToPrice())
				.TryAdd(Level1Fields.LowPrice, values.LowPrice.ToPrice())
				.TryAdd(Level1Fields.Volume, values.TotalVolume.ToDecimal())
				.TryAdd(Level1Fields.Turnover, values.Turnover.ToDecimal())
				.TryAdd(Level1Fields.BestBidPrice, values.BestBidPrice.ToPrice())
				.TryAdd(Level1Fields.BestAskPrice, values.BestAskPrice.ToPrice()), cancellationToken);
			}
			else if (subscription.DataType == DataType.Ticks)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					TradePrice = price,
					TradeVolume = volume,
					ServerTime = serverTime,
				}, cancellationToken);
			}
			else if (subscription.DataType.IsTFCandles)
			{
				await UpdateCandle(subscription, serverTime, price, volume, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessRealtimeDepth(KiwoomRealtimeMessage message, CancellationToken cancellationToken)
	{
		var security = ResolveRealtimeSecurity(message);
		var values = message.Data.Values;
		var serverTime = string.Empty.ToKiwoomUtc(values.DepthTime, security);
		foreach (var subscription in _marketSubscriptions.CachedValues.Where(subscription =>
			IsSame(subscription.Security, security) && subscription.DataType == DataType.MarketDepth))
		{
			var depth = new KiwoomDepthSnapshot(
				[.. values.BidPrices.Select(value => value.ToPrice() ?? 0)],
				[.. values.BidVolumes.Select(value => value.ToDecimal() ?? 0)],
				[.. values.AskPrices.Select(value => value.ToPrice() ?? 0)],
				[.. values.AskVolumes.Select(value => value.ToDecimal() ?? 0)], serverTime);
			await SendOutMessageAsync(CreateDepth(subscription.TransactionId, subscription.SecurityId, depth,
				subscription.MaxDepth), cancellationToken);
		}
	}

	private async ValueTask UpdateCandle(MarketSubscription subscription, DateTime serverTime, decimal price,
		decimal volume, CancellationToken cancellationToken)
	{
		var openTime = serverTime.Floor(subscription.TimeFrame);
		var candle = subscription.Candle;
		if (candle == null || candle.OpenTime != openTime)
		{
			if (candle != null)
				await SendCandle(subscription, candle, CandleStates.Finished, cancellationToken);
			candle = new()
			{
				OpenTime = openTime,
				Open = price,
				High = price,
				Low = price,
				Close = price,
				Volume = volume,
			};
			subscription.Candle = candle;
		}
		else
		{
			candle.High = Math.Max(candle.High, price);
			candle.Low = Math.Min(candle.Low, price);
			candle.Close = price;
			candle.Volume += volume;
		}
		await SendCandle(subscription, candle, CandleStates.Active, cancellationToken);
	}

	private ValueTask SendCandle(MarketSubscription subscription, ActiveCandle candle, CandleStates state,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TypedArg = subscription.TimeFrame,
			OpenTime = candle.OpenTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			State = state,
		}, cancellationToken);

	private static Level1ChangeMessage CreateLevel1(long transactionId, SecurityId securityId, KiwoomQuoteSnapshot quote)
		=> new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = quote.ServerTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, quote.LastPrice)
		.TryAdd(Level1Fields.OpenPrice, quote.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, quote.HighPrice)
		.TryAdd(Level1Fields.LowPrice, quote.LowPrice)
		.TryAdd(Level1Fields.ClosePrice, quote.PreviousClose)
		.TryAdd(Level1Fields.Volume, quote.Volume)
		.TryAdd(Level1Fields.Turnover, quote.Turnover)
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, quote.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, quote.AskVolume);

	private static QuoteChangeMessage CreateDepth(long transactionId, SecurityId securityId,
		KiwoomDepthSnapshot depth, int maxDepth)
	{
		var bids = depth.BidPrices.Zip(depth.BidVolumes)
			.Where(pair => pair.First > 0).OrderByDescending(pair => pair.First).Take(maxDepth)
			.Select(pair => new QuoteChange(pair.First, pair.Second)).ToArray();
		var asks = depth.AskPrices.Zip(depth.AskVolumes)
			.Where(pair => pair.First > 0).OrderBy(pair => pair.First).Take(maxDepth)
			.Select(pair => new QuoteChange(pair.First, pair.Second)).ToArray();
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = depth.ServerTime,
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
		};
	}
}
