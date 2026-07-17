namespace StockSharp.RakutenRss;

[SupportedOSPlatform("windows")]
public partial class RakutenRssMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		EnsureConnected();
		var code = message.SecurityId.SecurityCode;
		if (code.IsEmpty())
			throw new NotSupportedException(
				"MARKETSPEED II RSS has no instrument-master enumeration. Specify a security code.");
		var derivative = message.SecurityId.IsDerivative() ||
			message.GetSecurityTypes()?.Any(type => type is SecurityTypes.Future or SecurityTypes.Option) == true;
		var nativeCode = message.SecurityId.ToNativeCode();
		var info = await _client.GetSecurity(nativeCode, derivative
			? RakutenRssInstrumentKinds.Derivative : RakutenRssInstrumentKinds.Equity);
		if (info != null)
		{
			await SendOutMessageAsync(new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = info.Code.ToSecurityId(info.Market, derivative),
				Name = info.Name,
				ShortName = info.Name,
				SecurityType = derivative ? InferDerivativeType(info.Code) : SecurityTypes.Stock,
				Currency = CurrencyTypes.JPY,
			}, cancellationToken);
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
		=> await SubscribeQuote(message, FeedKinds.Level1, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
		=> await SubscribeQuote(message, FeedKinds.Depth, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		EnsureConnected();
		if (!message.IsSubscribe)
		{
			await RemoveSubscription(message.OriginalTransactionId);
			return;
		}
		var securityId = Normalize(message.SecurityId);
		var feedId = await _client.CreateTickFeed(new()
		{
			SecurityCode = securityId.ToNativeCode(),
			InstrumentKind = securityId.IsDerivative()
				? RakutenRssInstrumentKinds.Derivative : RakutenRssInstrumentKinds.Equity,
			Count = (int)Math.Clamp(message.Count ?? 300, 1, 300),
		});
		var subscription = new MarketSubscription
		{
			FeedId = feedId,
			SecurityId = securityId,
			Kind = FeedKinds.Ticks,
			DataType = message.DataType2,
		};
		try
		{
			await SendTicks(message.TransactionId, subscription, message.From, message.To,
				cancellationToken);
			if (message.IsHistoryOnly())
			{
				await _client.RemoveFeed(feedId);
				await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
			}
			else
			{
				_subscriptions[message.TransactionId] = subscription;
				await SendSubscriptionResultAsync(message, cancellationToken);
			}
		}
		catch
		{
			await _client.RemoveFeed(feedId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		EnsureConnected();
		if (!message.IsSubscribe)
		{
			await RemoveSubscription(message.OriginalTransactionId);
			return;
		}
		var securityId = Normalize(message.SecurityId);
		var count = (int)Math.Clamp(message.Count ?? 300, 1, 3000);
		var feedId = await _client.CreateCandleFeed(new()
		{
			SecurityCode = securityId.ToNativeCode(),
			TimeFrame = message.GetTimeFrame().ToNativeTimeFrame(),
			Count = count,
			From = message.From,
		});
		var subscription = new MarketSubscription
		{
			FeedId = feedId,
			SecurityId = securityId,
			Kind = FeedKinds.Candles,
			DataType = message.DataType2,
		};
		try
		{
			await SendCandles(message.TransactionId, subscription, message.From, message.To,
				false, cancellationToken);
			if (message.IsHistoryOnly())
			{
				await _client.RemoveFeed(feedId);
				await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
			}
			else
			{
				_subscriptions[message.TransactionId] = subscription;
				await SendSubscriptionResultAsync(message, cancellationToken);
			}
		}
		catch
		{
			await _client.RemoveFeed(feedId);
			throw;
		}
	}

	private async ValueTask SubscribeQuote(MarketDataMessage message, FeedKinds kind,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		EnsureConnected();
		if (!message.IsSubscribe)
		{
			await RemoveSubscription(message.OriginalTransactionId);
			return;
		}
		var securityId = Normalize(message.SecurityId);
		var feedId = await _client.CreateQuoteFeed(new()
		{
			SecurityCode = securityId.ToNativeCode(),
			InstrumentKind = securityId.IsDerivative()
				? RakutenRssInstrumentKinds.Derivative : RakutenRssInstrumentKinds.Equity,
		});
		var subscription = new MarketSubscription
		{
			FeedId = feedId,
			SecurityId = securityId,
			Kind = kind,
			MaxDepth = Math.Clamp(message.MaxDepth ?? 10, 1, 10),
			DataType = message.DataType2,
		};
		try
		{
			await SendQuote(message.TransactionId, subscription, await _client.ReadQuote(feedId),
				true, cancellationToken);
			if (message.IsHistoryOnly())
			{
				await _client.RemoveFeed(feedId);
				await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
			}
			else
			{
				_subscriptions[message.TransactionId] = subscription;
				await SendSubscriptionResultAsync(message, cancellationToken);
			}
		}
		catch
		{
			await _client.RemoveFeed(feedId);
			throw;
		}
	}

	private async ValueTask PollSubscription(long transactionId, MarketSubscription subscription,
		CancellationToken cancellationToken)
	{
		switch (subscription.Kind)
		{
			case FeedKinds.Level1:
			case FeedKinds.Depth:
				await SendQuote(transactionId, subscription,
					await _client.ReadQuote(subscription.FeedId), false, cancellationToken);
				break;
			case FeedKinds.Ticks:
				await SendTicks(transactionId, subscription, null, null, cancellationToken);
				break;
			case FeedKinds.Candles:
				await SendCandles(transactionId, subscription, null, null, true, cancellationToken);
				break;
		}
	}

	private async ValueTask SendQuote(long transactionId, MarketSubscription subscription,
		RakutenRssQuote quote, bool force, CancellationToken cancellationToken)
	{
		if (quote == null)
			return;
		if (subscription.Kind == FeedKinds.Level1)
		{
			var signature = $"{quote.Time:O}|{quote.LastPrice}|{quote.BestBidPrice}|" +
				$"{quote.BestAskPrice}|{quote.Volume}|{quote.State}";
			if (!force && signature == subscription.Signature)
				return;
			subscription.Signature = signature;
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = transactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = quote.Time,
			}
			.TryAdd(Level1Fields.LastTradePrice, quote.LastPrice)
			.TryAdd(Level1Fields.OpenPrice, quote.OpenPrice)
			.TryAdd(Level1Fields.HighPrice, quote.HighPrice)
			.TryAdd(Level1Fields.LowPrice, quote.LowPrice)
			.TryAdd(Level1Fields.ClosePrice, quote.PreviousClose)
			.TryAdd(Level1Fields.Volume, quote.Volume)
			.TryAdd(Level1Fields.Turnover, quote.Turnover)
			.TryAdd(Level1Fields.BestAskPrice, quote.BestAskPrice)
			.TryAdd(Level1Fields.BestAskVolume, quote.BestAskVolume)
			.TryAdd(Level1Fields.BestBidPrice, quote.BestBidPrice)
			.TryAdd(Level1Fields.BestBidVolume, quote.BestBidVolume)
			.TryAdd(Level1Fields.State, quote.State is "#" ? SecurityStates.Stoped
				: SecurityStates.Trading), cancellationToken);
		}
		else
		{
			var levels = quote.Depth ?? [];
			var bids = levels.Where(level => level?.BidPrice is > 0 && level.BidVolume is > 0)
				.OrderByDescending(level => level.BidPrice).Take(subscription.MaxDepth)
				.Select(level => new QuoteChange(level.BidPrice.Value, level.BidVolume.Value)).ToArray();
			var asks = levels.Where(level => level?.AskPrice is > 0 && level.AskVolume is > 0)
				.OrderBy(level => level.AskPrice).Take(subscription.MaxDepth)
				.Select(level => new QuoteChange(level.AskPrice.Value, level.AskVolume.Value)).ToArray();
			var signature = string.Join('|', bids.Select(item => $"B{item.Price}:{item.Volume}")
				.Concat(asks.Select(item => $"A{item.Price}:{item.Volume}")));
			if (!force && signature == subscription.Signature)
				return;
			subscription.Signature = signature;
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = transactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = quote.Time,
				Bids = bids,
				Asks = asks,
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		}
	}

	private async ValueTask SendTicks(long transactionId, MarketSubscription subscription,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		foreach (var tick in (await _client.ReadTicks(subscription.FeedId)).OrderBy(item => item.Time))
		{
			if (from != null && tick.Time < EnsureUtc(from.Value) ||
				to != null && tick.Time > EnsureUtc(to.Value))
				continue;
			var id = $"{tick.Time:O}|{tick.Price}|{tick.Volume}";
			if (!subscription.TradeIds.Add(id))
				continue;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = transactionId,
				SecurityId = subscription.SecurityId,
				TradeStringId = id,
				TradePrice = tick.Price,
				TradeVolume = tick.Volume,
				ServerTime = tick.Time,
			}, cancellationToken);
		}
	}

	private async ValueTask SendCandles(long transactionId, MarketSubscription subscription,
		DateTime? from, DateTime? to, bool onlyChanged, CancellationToken cancellationToken)
	{
		var candles = (await _client.ReadCandles(subscription.FeedId))
			.Where(candle => (from == null || candle.OpenTime >= EnsureUtc(from.Value)) &&
				(to == null || candle.OpenTime <= EnsureUtc(to.Value)))
			.OrderBy(candle => candle.OpenTime).ToArray();
		if (onlyChanged && candles.Length > 0)
			candles = [candles[^1]];
		foreach (var candle in candles)
		{
			var signature = $"{candle.OpenTime:O}|{candle.Open}|{candle.High}|" +
				$"{candle.Low}|{candle.Close}|{candle.Volume}";
			if (onlyChanged && signature == subscription.Signature)
				continue;
			subscription.Signature = signature;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = transactionId,
				SecurityId = subscription.SecurityId,
				DataType = subscription.DataType,
				OpenTime = candle.OpenTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
	}

	private async Task RemoveSubscription(long transactionId)
	{
		if (_subscriptions.Remove(transactionId, out var subscription))
			await _client.RemoveFeed(subscription.FeedId);
	}

	private void EnsureConnected()
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private static SecurityTypes InferDerivativeType(string code)
		=> code?.Contains('C') == true || code?.Contains('P') == true
			? SecurityTypes.Option : SecurityTypes.Future;

	private static SecurityId Normalize(SecurityId id)
		=> new()
		{
			SecurityCode = id.SecurityCode.ThrowIfEmpty(nameof(id.SecurityCode)),
			BoardCode = id.BoardCode.IsEmpty(id.IsDerivative() ? "OSE" : BoardCodes.Tse),
		};

	private static DateTime EnsureUtc(DateTime value)
		=> value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
