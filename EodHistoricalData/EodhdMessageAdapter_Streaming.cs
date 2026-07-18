namespace StockSharp.EodHistoricalData;

public partial class EodhdMessageAdapter
{
	private async ValueTask OnStreamData(EodhdStreamKinds kind, EodhdStreamMessage data,
		CancellationToken cancellationToken)
	{
		if (data?.Symbol.IsEmpty() != false || data.Timestamp == null)
			return;
		LiveSubscription[] subscriptions;
		lock (_liveSync)
		{
			subscriptions = _liveSubscriptions.Values
				.Where(subscription => MatchesStream(subscription, kind, data.Symbol)).ToArray();
		}

		var time = Extensions.FromUnixMilliseconds(data.Timestamp.Value);
		var emitted = new List<LiveSubscription>();
		foreach (var subscription in subscriptions)
		{
			if (subscription.DataType == DataType.Level1)
			{
				var message = CreateStreamLevel1(subscription, kind, data, time);
				if (message.Changes.Count == 0)
					continue;
				await SendOutMessageAsync(message, cancellationToken);
			}
			else
			{
				if (kind is not EodhdStreamKinds.StockTrades and
					not EodhdStreamKinds.CryptoTrades || Positive(data.Price) == null)
				{
					continue;
				}
				await SendOutMessageAsync(new ExecutionMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					DataTypeEx = DataType.Ticks,
					ServerTime = time,
					TradePrice = data.Price,
					TradeVolume = NonNegative(kind == EodhdStreamKinds.StockTrades
						? data.Volume : data.Quantity),
				}, cancellationToken);
			}
			emitted.Add(subscription);
		}

		var finished = new List<LiveSubscription>();
		lock (_liveSync)
		{
			foreach (var subscription in emitted)
			{
				if (!_liveSubscriptions.TryGetValue(subscription.TransactionId,
					out var current) || !ReferenceEquals(subscription, current))
				{
					continue;
				}
				if (current.Remaining is > 0 && --current.Remaining == 0)
				{
					_liveSubscriptions.Remove(current.TransactionId);
					finished.Add(current);
				}
			}
		}

		foreach (var subscription in finished)
			await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
		foreach (var subscription in finished)
		{
			foreach (var requiredKind in GetRequiredStreams(subscription))
				await UnsubscribeOrphan(requiredKind, subscription.Key.ToStreamSymbol(),
					cancellationToken, true);
		}
	}

	private static Level1ChangeMessage CreateStreamLevel1(LiveSubscription subscription,
		EodhdStreamKinds kind, EodhdStreamMessage data, DateTime time)
	{
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
		};
		switch (kind)
		{
			case EodhdStreamKinds.StockTrades:
				message
					.TryAdd(Level1Fields.LastTradePrice, Positive(data.Price))
					.TryAdd(Level1Fields.LastTradeVolume, NonNegative(data.Volume))
					.TryAdd(Level1Fields.LastTradeTime,
						Positive(data.Price) != null ? time : null)
					.TryAdd(Level1Fields.State, ToSecurityState(data.MarketStatus));
				break;
			case EodhdStreamKinds.StockQuotes:
				message
					.TryAdd(Level1Fields.BestBidPrice, Positive(data.BidPrice))
					.TryAdd(Level1Fields.BestBidVolume, NonNegative(data.BidSize))
					.TryAdd(Level1Fields.BestBidTime,
						Positive(data.BidPrice) != null ? time : null)
					.TryAdd(Level1Fields.BestAskPrice, Positive(data.AskPrice))
					.TryAdd(Level1Fields.BestAskVolume, NonNegative(data.AskSize))
					.TryAdd(Level1Fields.BestAskTime,
						Positive(data.AskPrice) != null ? time : null);
				break;
			case EodhdStreamKinds.ForexQuotes:
				var bid = Positive(data.Bid);
				var ask = Positive(data.Ask);
				message
					.TryAdd(Level1Fields.BestBidPrice, bid)
					.TryAdd(Level1Fields.BestBidTime, bid != null ? time : null)
					.TryAdd(Level1Fields.BestAskPrice, ask)
					.TryAdd(Level1Fields.BestAskTime, ask != null ? time : null)
					.TryAdd(Level1Fields.TheorPrice,
						bid != null && ask != null ? (bid.Value + ask.Value) / 2 : null)
					.TryAdd(Level1Fields.Change, ParseNullableDecimal(data.DailyChange));
				break;
			case EodhdStreamKinds.CryptoTrades:
				message
					.TryAdd(Level1Fields.LastTradePrice, Positive(data.Price))
					.TryAdd(Level1Fields.LastTradeVolume, NonNegative(data.Quantity))
					.TryAdd(Level1Fields.LastTradeTime,
						Positive(data.Price) != null ? time : null)
					.TryAdd(Level1Fields.Change, ParseNullableDecimal(data.DailyChange));
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
		}
		return message;
	}

	private async Task AddLiveSubscription(MarketDataMessage mdMsg, SecurityId securityId,
		EodhdSecurityKey key, long? remaining, CancellationToken cancellationToken)
	{
		var subscription = new LiveSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			Key = key,
			DataType = mdMsg.DataType2,
			Remaining = remaining,
		};
		var kinds = GetRequiredStreams(subscription);
		if (kinds.Length == 0)
			throw new NotSupportedException("EODHD has no matching live stream for this request.");
		lock (_liveSync)
		{
			if (_liveSubscriptions.ContainsKey(mdMsg.TransactionId))
				throw new InvalidOperationException(
					$"EODHD subscription {mdMsg.TransactionId} already exists.");
			_liveSubscriptions.Add(mdMsg.TransactionId, subscription);
		}

		var symbol = key.ToStreamSymbol();
		try
		{
			foreach (var kind in kinds)
				await GetOrCreateStream(kind).Subscribe(symbol, cancellationToken);
		}
		catch
		{
			lock (_liveSync)
				_liveSubscriptions.Remove(mdMsg.TransactionId);
			foreach (var kind in kinds)
				await UnsubscribeOrphan(kind, symbol, cancellationToken, false);
			throw;
		}
	}

	private async Task RemoveLiveSubscription(long transactionId,
		CancellationToken cancellationToken)
	{
		LiveSubscription removed;
		lock (_liveSync)
		{
			if (!_liveSubscriptions.Remove(transactionId, out removed))
				return;
		}
		var symbol = removed.Key.ToStreamSymbol();
		foreach (var kind in GetRequiredStreams(removed))
			await UnsubscribeOrphan(kind, symbol, cancellationToken, false);
	}

	private async Task UnsubscribeOrphan(EodhdStreamKinds kind, string symbol,
		CancellationToken cancellationToken, bool isReceiveCallback)
	{
		lock (_liveSync)
		{
			if (_liveSubscriptions.Values.Any(item => MatchesStream(item, kind, symbol)))
				return;
		}
		var stream = GetExistingStream(kind);
		if (stream != null)
			await stream.Unsubscribe(symbol, cancellationToken);
		if (isReceiveCallback)
			RetireUnusedStream(kind);
		else
			await DisposeUnusedStream(kind);
	}

	private void RetireUnusedStream(EodhdStreamKinds kind)
	{
		EodhdWebSocketClient stream;
		lock (_liveSync)
		{
			if (_liveSubscriptions.Values.Any(item => NeedsStream(item, kind)))
				return;
			lock (_streamSync)
			{
				stream = GetStream(kind);
				SetStream(kind, null);
			}
		}
		if (stream == null)
			return;
		stream.DataReceived -= OnStreamData;
		stream.Error -= SendOutErrorAsync;
		stream.RequestStop();
		_ = FinishRetiredStream(stream);
	}

	private static async Task FinishRetiredStream(EodhdWebSocketClient stream)
	{
		try
		{
			await stream.Disconnect();
		}
		finally
		{
			stream.Dispose();
		}
	}

	private static EodhdStreamKinds[] GetRequiredStreams(LiveSubscription subscription)
	{
		var key = subscription.Key;
		if (subscription.DataType == DataType.Level1)
		{
			if (key.IsUsStock())
				return [EodhdStreamKinds.StockTrades, EodhdStreamKinds.StockQuotes];
			if (key.Market == EodhdMarkets.Forex)
				return [EodhdStreamKinds.ForexQuotes];
			if (key.Market == EodhdMarkets.Crypto)
				return [EodhdStreamKinds.CryptoTrades];
		}
		else if (subscription.DataType == DataType.Ticks)
		{
			if (key.IsUsStock())
				return [EodhdStreamKinds.StockTrades];
			if (key.Market == EodhdMarkets.Crypto)
				return [EodhdStreamKinds.CryptoTrades];
		}
		return [];
	}

	private static bool NeedsStream(LiveSubscription subscription, EodhdStreamKinds kind)
		=> GetRequiredStreams(subscription).Contains(kind);

	private static bool MatchesStream(LiveSubscription subscription, EodhdStreamKinds kind,
		string symbol)
		=> NeedsStream(subscription, kind) &&
			subscription.Key.ToStreamSymbol().EqualsIgnoreCase(symbol);

	private static SecurityStates? ToSecurityState(string value)
		=> value.EqualsIgnoreCase("open") || value.EqualsIgnoreCase("extended-hours")
			? SecurityStates.Trading : value.EqualsIgnoreCase("closed")
				? SecurityStates.Stoped : null;

	private static decimal? ParseNullableDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture,
			out var result) ? result : null;
}
