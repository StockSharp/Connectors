namespace StockSharp.Deriv;

public partial class DerivMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		DerivActiveSymbol[] symbols;
		using (_sync.EnterScope())
			symbols = [.. _symbols.Values.OrderBy(static symbol => symbol.Symbol)];

		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var native in symbols)
		{
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = native.Symbol.ToSecurityId(),
				SecurityType = native.ToSecurityType(),
				Name = native.Name.IsEmpty(native.Symbol),
				ShortName = native.Name.IsEmpty(native.Symbol),
				Class = new[] { native.Market, native.Submarket }
					.Where(static value => !value.IsEmpty()).Join("/"),
				PriceStep = native.PipSize > 0 ? native.PipSize : null,
				Decimals = native.PipSize > 0 ? native.PipSize.GetCachedDecimals() : null,
				Currency = GetSymbolCurrency(native),
			};

			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;

			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}

		this.AddDebugLog("Deriv security lookup {0} completed from {1} active symbols.",
			lookupMsg.TransactionId, symbols.Length);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveMarketSubscriptionsAsync(mdMsg.OriginalTransactionId,
				DerivSubscriptionKinds.Level1, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var native = ResolveSymbol(mdMsg.SecurityId);
		var securityId = native.Symbol.ToSecurityId();
		if (HasHistoryRequest(mdMsg) || mdMsg.IsHistoryOnly())
		{
			var history = await WebSocketClient.RequestAsync(
				CreateHistoryRequest(mdMsg, native.Symbol, "ticks", null, false),
				cancellationToken);
			await SendLevel1HistoryAsync(mdMsg, securityId, history, cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var subscription = new DerivSubscription
		{
			NativeKey = $"level1:{mdMsg.TransactionId}",
			Kind = DerivSubscriptionKinds.Level1,
			TransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			Symbol = native.Symbol,
		};
		AddSubscription(subscription);
		try
		{
			var response = await WebSocketClient.SubscribeAsync(subscription.NativeKey,
				new JObject
				{
					["ticks"] = native.Symbol,
					["subscribe"] = 1,
				}, true, cancellationToken);
			await ProcessLevel1ResponseAsync(subscription, response, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			this.AddDebugLog("Deriv Level1 subscribed for {0}, transaction {1}.",
				native.Symbol, mdMsg.TransactionId);
		}
		catch
		{
			TryRemoveSubscription(subscription.NativeKey, out _);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveMarketSubscriptionsAsync(mdMsg.OriginalTransactionId,
				DerivSubscriptionKinds.Ticks, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var native = ResolveSymbol(mdMsg.SecurityId);
		var subscription = new DerivSubscription
		{
			NativeKey = $"ticks:{mdMsg.TransactionId}",
			Kind = DerivSubscriptionKinds.Ticks,
			TransactionId = mdMsg.TransactionId,
			SecurityId = native.Symbol.ToSecurityId(),
			Symbol = native.Symbol,
			Skip = Math.Max(0, mdMsg.Skip ?? 0),
			Count = Math.Max(1, mdMsg.Count ?? (HasHistoryRequest(mdMsg) ? 1000 : 1)),
		};

		var request = HasHistoryRequest(mdMsg) || mdMsg.IsHistoryOnly()
			? CreateHistoryRequest(mdMsg, native.Symbol, "ticks", null,
				!mdMsg.IsHistoryOnly())
			: new JObject
			{
				["ticks"] = native.Symbol,
				["subscribe"] = 1,
			};

		if (mdMsg.IsHistoryOnly())
		{
			var response = await WebSocketClient.RequestAsync(request, cancellationToken);
			await ProcessTicksResponseAsync(subscription, response, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		AddSubscription(subscription);
		try
		{
			var response = await WebSocketClient.SubscribeAsync(subscription.NativeKey,
				request, true, cancellationToken);
			await ProcessTicksResponseAsync(subscription, response, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			this.AddDebugLog("Deriv ticks subscribed for {0}, transaction {1}.",
				native.Symbol, mdMsg.TransactionId);
		}
		catch
		{
			TryRemoveSubscription(subscription.NativeKey, out _);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveMarketSubscriptionsAsync(mdMsg.OriginalTransactionId,
				DerivSubscriptionKinds.Candles, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var native = ResolveSymbol(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToDerivGranularity();
		var subscription = new DerivSubscription
		{
			NativeKey = $"candles:{mdMsg.TransactionId}",
			Kind = DerivSubscriptionKinds.Candles,
			TransactionId = mdMsg.TransactionId,
			SecurityId = native.Symbol.ToSecurityId(),
			Symbol = native.Symbol,
			TimeFrame = timeFrame,
			Skip = Math.Max(0, mdMsg.Skip ?? 0),
			Count = Math.Max(1, mdMsg.Count ?? (HasHistoryRequest(mdMsg) ? 1000 : 1)),
		};
		var request = CreateHistoryRequest(mdMsg, native.Symbol, "candles",
			timeFrame, !mdMsg.IsHistoryOnly());

		if (mdMsg.IsHistoryOnly())
		{
			var response = await WebSocketClient.RequestAsync(request, cancellationToken);
			await ProcessCandleResponseAsync(subscription, response, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		AddSubscription(subscription);
		try
		{
			var response = await WebSocketClient.SubscribeAsync(subscription.NativeKey,
				request, true, cancellationToken);
			await ProcessCandleResponseAsync(subscription, response, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			this.AddDebugLog("Deriv {0} candles subscribed for {1}, transaction {2}.",
				timeFrame, native.Symbol, mdMsg.TransactionId);
		}
		catch
		{
			TryRemoveSubscription(subscription.NativeKey, out _);
			throw;
		}
	}

	private async ValueTask ProcessLevel1ResponseAsync(DerivSubscription subscription,
		DerivResponse response, CancellationToken cancellationToken)
	{
		var tick = response?.Get<DerivTick>("tick");
		if (tick is null)
			return;
		await SendLevel1Async(subscription.TransactionId, subscription.SecurityId,
			tick, cancellationToken);
	}

	private async ValueTask ProcessTicksResponseAsync(DerivSubscription subscription,
		DerivResponse response, CancellationToken cancellationToken)
	{
		if (response is null)
			return;
		var history = response.Get<DerivHistory>("history");
		if (history is not null)
		{
			var count = Math.Min(history.Prices?.Length ?? 0, history.Times?.Length ?? 0);
			var skip = subscription.Skip.Min(count).To<int>();
			var take = subscription.Count.Min(count - skip).To<int>();
			for (var i = skip; i < skip + take; i++)
				await SendTickAsync(subscription.TransactionId, subscription.SecurityId,
					subscription.Symbol, history.Times[i], history.Prices[i],
					cancellationToken);
		}

		var tick = response.Get<DerivTick>("tick");
		if (tick is not null)
			await SendTickAsync(subscription.TransactionId, subscription.SecurityId,
				tick.Symbol.IsEmpty(subscription.Symbol), tick.Epoch, tick.Quote,
				cancellationToken);
	}

	private async ValueTask ProcessCandleResponseAsync(DerivSubscription subscription,
		DerivResponse response, CancellationToken cancellationToken)
	{
		if (response is null)
			return;

		var candles = response.GetArray<DerivCandle>("candles")
			.OrderBy(static candle => candle.Epoch)
			.Skip(subscription.Skip.Min(int.MaxValue).To<int>())
			.Take(subscription.Count.Min(int.MaxValue).To<int>())
			.ToArray();
		for (var i = 0; i < candles.Length; i++)
		{
			var candle = candles[i];
			var isLastLive = i == candles.Length - 1 &&
				TryGetSubscription(subscription.NativeKey, out _);
			await SendCandleAsync(subscription, candle,
				isLastLive && candle.Epoch.FromDerivEpoch() + subscription.TimeFrame >
					DateTime.UtcNow ? CandleStates.Active : CandleStates.Finished,
				cancellationToken);
		}
		if (candles.Length > 0)
			subscription.LastCandle = candles[^1];

		var ohlc = response.Get<DerivOhlc>("ohlc");
		if (ohlc is null)
			return;
		var current = ohlc.ToCandle();
		if (subscription.LastCandle is { } previous && previous.Epoch != current.Epoch)
			await SendCandleAsync(subscription, previous, CandleStates.Finished,
				cancellationToken);
		await SendCandleAsync(subscription, current, CandleStates.Active,
			cancellationToken);
		subscription.LastCandle = current;
	}

	private async ValueTask SendLevel1HistoryAsync(MarketDataMessage message,
		SecurityId securityId, DerivResponse response, CancellationToken cancellationToken)
	{
		var history = response.Get<DerivHistory>("history");
		if (history is null)
			return;
		var count = Math.Min(history.Prices?.Length ?? 0, history.Times?.Length ?? 0);
		var skip = Math.Max(0, message.Skip ?? 0).Min(count).To<int>();
		var take = Math.Max(1, message.Count ?? count).Min(count - skip).To<int>();
		for (var i = skip; i < skip + take; i++)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = securityId,
				ServerTime = history.Times[i].FromDerivEpoch(),
			}.TryAdd(Level1Fields.LastTradePrice, history.Prices[i]), cancellationToken);
		}
	}

	private ValueTask SendLevel1Async(long transactionId, SecurityId securityId,
		DerivTick tick, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = tick.Epoch.FromDerivEpoch(),
		}
		.TryAdd(Level1Fields.BestBidPrice, tick.Bid)
		.TryAdd(Level1Fields.BestAskPrice, tick.Ask)
		.TryAdd(Level1Fields.LastTradePrice, tick.Quote), cancellationToken);

	private ValueTask SendTickAsync(long transactionId, SecurityId securityId,
		string symbol, long epoch, decimal price, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = epoch.FromDerivEpoch(),
			TradeStringId = $"{symbol}-{epoch.ToString(CultureInfo.InvariantCulture)}",
			TradePrice = price,
		}, cancellationToken);

	private ValueTask SendCandleAsync(DerivSubscription subscription,
		DerivCandle candle, CandleStates state, CancellationToken cancellationToken)
	{
		var openTime = candle.Epoch.FromDerivEpoch();
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			OpenTime = openTime,
			CloseTime = openTime + subscription.TimeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TypedArg = subscription.TimeFrame,
			State = state,
		}, cancellationToken);
	}

	private async ValueTask RemoveMarketSubscriptionsAsync(long transactionId,
		DerivSubscriptionKinds kind, CancellationToken cancellationToken)
	{
		foreach (var subscription in GetSubscriptions(transactionId, kind))
		{
			if (TryRemoveSubscription(subscription.NativeKey, out _))
				await WebSocketClient.UnsubscribeAsync(subscription.NativeKey,
					cancellationToken);
		}
		this.AddDebugLog("Deriv unsubscribed {0} transaction {1}.", kind, transactionId);
	}

	private static JObject CreateHistoryRequest(MarketDataMessage message, string symbol,
		string style, TimeSpan? timeFrame, bool isSubscribe)
	{
		var skip = Math.Max(0, message.Skip ?? 0);
		var requested = Math.Max(1, message.Count ??
			(message.From is not null || message.To is not null ? 1000 : 1));
		var count = Math.Min(5000L,
			Math.Min(requested, 5000L) + Math.Min(skip, 5000L));
		var request = new JObject
		{
			["ticks_history"] = symbol,
			["style"] = style,
			["count"] = count,
			["end"] = message.To is DateTime to
				? to.ToDerivEpoch().ToString(CultureInfo.InvariantCulture)
				: "latest",
		};
		if (message.From is DateTime from)
			request["start"] = from.ToDerivEpoch();
		if (timeFrame is TimeSpan value)
			request["granularity"] = value.ToDerivGranularity();
		if (isSubscribe)
			request["subscribe"] = 1;
		return request;
	}

	private static bool HasHistoryRequest(MarketDataMessage message)
		=> message.From is not null || message.To is not null ||
			message.Count is not null || message.Skip is not null;

	private static CurrencyTypes? GetSymbolCurrency(DerivActiveSymbol symbol)
	{
		if (!symbol.Market.EqualsIgnoreCase("forex"))
			return null;
		var currency = symbol.Name?.Split('/').LastOrDefault();
		return Enum.TryParse<CurrencyTypes>(currency, true, out var value) ? value : null;
	}
}
