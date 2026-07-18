namespace StockSharp.FinancialModelingPrep;

public partial class FmpMessageAdapter
{
	private async ValueTask OnStreamData(FmpStreamKinds kind, FmpStreamMessage data,
		CancellationToken cancellationToken)
	{
		if (data?.Symbol.IsEmpty() != false || data.Timestamp is not > 0)
			return;
		var time = Extensions.FromUnixTimestamp(data.Timestamp.Value);
		LiveSubscription[] subscriptions;
		lock (_liveSync)
		{
			subscriptions = _liveSubscriptions.Values.Where(item =>
				MatchesStream(item, kind, data.Symbol)).ToArray();
		}

		var emitted = new List<LiveSubscription>();
		foreach (var subscription in subscriptions)
		{
			if (subscription.DataType == DataType.Level1)
			{
				var message = CreateStreamLevel1(subscription, data, time);
				if (message.Changes.Count == 0)
					continue;
				await SendOutMessageAsync(message, cancellationToken);
			}
			else
			{
				if (!data.Type.EqualsIgnoreCase("T") || Positive(data.LastPrice) == null)
					continue;
				await SendOutMessageAsync(new ExecutionMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					DataTypeEx = DataType.Ticks,
					ServerTime = time,
					TradePrice = data.LastPrice,
					TradeVolume = NonNegative(data.LastSize),
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
		FmpStreamMessage data, DateTime time)
	{
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
		};
		if (data.Type.EqualsIgnoreCase("Q"))
		{
			message
				.TryAdd(Level1Fields.BestBidPrice, Positive(data.BidPrice))
				.TryAdd(Level1Fields.BestBidVolume, NonNegative(data.BidSize))
				.TryAdd(Level1Fields.BestBidTime,
					Positive(data.BidPrice) != null ? time : null)
				.TryAdd(Level1Fields.BestAskPrice, Positive(data.AskPrice))
				.TryAdd(Level1Fields.BestAskVolume, NonNegative(data.AskSize))
				.TryAdd(Level1Fields.BestAskTime,
					Positive(data.AskPrice) != null ? time : null);
		}
		else if (data.Type.EqualsIgnoreCase("T"))
		{
			message
				.TryAdd(Level1Fields.LastTradePrice, Positive(data.LastPrice))
				.TryAdd(Level1Fields.LastTradeVolume, NonNegative(data.LastSize))
				.TryAdd(Level1Fields.LastTradeTime,
					Positive(data.LastPrice) != null ? time : null);
		}
		return message;
	}

	private async Task AddLiveSubscription(MarketDataMessage mdMsg, SecurityId securityId,
		FmpSecurityKey key, long? remaining, CancellationToken cancellationToken)
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
			throw new NotSupportedException("FMP has no matching live stream for this request.");
		lock (_liveSync)
		{
			if (_liveSubscriptions.ContainsKey(mdMsg.TransactionId))
				throw new InvalidOperationException(
					$"FMP subscription {mdMsg.TransactionId} already exists.");
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

	private async Task UnsubscribeOrphan(FmpStreamKinds kind, string symbol,
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

	private void RetireUnusedStream(FmpStreamKinds kind)
	{
		FmpWebSocketClient stream;
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

	private static async Task FinishRetiredStream(FmpWebSocketClient stream)
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

	private static FmpStreamKinds[] GetRequiredStreams(LiveSubscription subscription)
	{
		var key = subscription.Key;
		if (subscription.DataType == DataType.Level1)
		{
			if (key.IsUsStockStream())
				return [FmpStreamKinds.Stocks];
			if (key.Market == FmpMarkets.Forex)
				return [FmpStreamKinds.Forex];
			if (key.Market == FmpMarkets.Crypto)
				return [FmpStreamKinds.Crypto];
		}
		else if (subscription.DataType == DataType.Ticks)
		{
			if (key.IsUsStockStream())
				return [FmpStreamKinds.Stocks];
			if (key.Market == FmpMarkets.Crypto)
				return [FmpStreamKinds.Crypto];
		}
		return [];
	}

	private static bool CanStreamLevel1(FmpSecurityKey key)
		=> key.IsUsStockStream() || key.Market is FmpMarkets.Forex or FmpMarkets.Crypto;

	private static bool CanStreamTicks(FmpSecurityKey key)
		=> key.IsUsStockStream() || key.Market == FmpMarkets.Crypto;

	private static bool NeedsStream(LiveSubscription subscription, FmpStreamKinds kind)
		=> GetRequiredStreams(subscription).Contains(kind);

	private static bool MatchesStream(LiveSubscription subscription, FmpStreamKinds kind,
		string symbol)
		=> NeedsStream(subscription, kind) &&
			subscription.Key.ToStreamSymbol().EqualsIgnoreCase(symbol);
}
