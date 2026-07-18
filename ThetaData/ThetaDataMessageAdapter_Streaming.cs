namespace StockSharp.ThetaData;

public partial class ThetaDataMessageAdapter
{
	private async Task AddLiveSubscription(MarketDataMessage mdMsg, SecurityId securityId,
		ThetaSecurityKey key, long? remaining, CancellationToken cancellationToken)
	{
		var subscription = new LiveSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			Key = key,
			DataType = mdMsg.DataType2,
			Remaining = remaining,
		};
		var streamKey = key.ToStreamKey(mdMsg.DataType2);
		var isFirst = false;
		using (_liveSync.EnterScope())
		{
			if (_liveSubscriptions.ContainsKey(mdMsg.TransactionId))
			{
				throw new InvalidOperationException(
					$"ThetaData subscription {mdMsg.TransactionId} already exists.");
			}
			isFirst = !_liveSubscriptions.Values.Any(item =>
				item.Key.ToStreamKey(item.DataType) == streamKey);
			_liveSubscriptions.Add(mdMsg.TransactionId, subscription);
		}

		try
		{
			if (isFirst)
				await GetOrCreateStream().Subscribe(streamKey, cancellationToken);
		}
		catch
		{
			using (_liveSync.EnterScope())
				_liveSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
	}

	private async Task RemoveLiveSubscription(long transactionId,
		CancellationToken cancellationToken)
	{
		LiveSubscription removed;
		var unsubscribe = false;
		using (_liveSync.EnterScope())
		{
			if (!_liveSubscriptions.Remove(transactionId, out removed))
				return;
			var streamKey = removed.Key.ToStreamKey(removed.DataType);
			unsubscribe = !_liveSubscriptions.Values.Any(item =>
				item.Key.ToStreamKey(item.DataType) == streamKey);
		}
		if (!unsubscribe)
			return;
		var stream = GetExistingStream();
		if (stream != null)
			await stream.Unsubscribe(removed.Key.ToStreamKey(removed.DataType), cancellationToken);
	}

	private async ValueTask OnStreamData(ThetaStreamMessage data,
		CancellationToken cancellationToken)
	{
		ThetaSecurityKey key;
		try
		{
			key = data.Contract.ToThetaKey();
		}
		catch (Exception error) when (error is InvalidDataException or ArgumentException)
		{
			await SendOutErrorAsync(error, cancellationToken);
			return;
		}

		LiveSubscription[] subscriptions;
		using (_liveSync.EnterScope())
		{
			subscriptions = _liveSubscriptions.Values
				.Where(item => item.Key == key).ToArray();
		}

		var emitted = new List<LiveSubscription>();
		foreach (var subscription in subscriptions)
		{
			Message message = null;
			if (subscription.DataType == DataType.Ticks && data.Trade != null &&
				Extensions.TryGetStreamTime(data.Trade.Date, data.Trade.MillisecondsOfDay,
					_marketTimeZone, out var tradeTime))
			{
				message = CreateTick(subscription.TransactionId, subscription.SecurityId,
					tradeTime, data.Trade.Price, data.Trade.Size, data.Trade.Sequence,
					data.Trade.Condition);
			}
			else if (subscription.DataType == DataType.Level1 &&
				key.Market == ThetaDataMarkets.Indices && data.Trade != null &&
				Extensions.TryGetStreamTime(data.Trade.Date, data.Trade.MillisecondsOfDay,
					_marketTimeZone, out tradeTime))
			{
				message = CreatePriceLevel1(subscription.TransactionId,
					subscription.SecurityId, tradeTime, data.Trade.Price);
			}
			else if (data.Quote != null &&
				Extensions.TryGetStreamTime(data.Quote.Date, data.Quote.MillisecondsOfDay,
					_marketTimeZone, out var quoteTime))
			{
				if (subscription.DataType == DataType.Level1)
				{
					message = CreateQuoteLevel1(subscription.TransactionId,
						subscription.SecurityId, quoteTime, data.Quote.Bid,
						data.Quote.BidSize, data.Quote.Ask, data.Quote.AskSize);
				}
				else if (subscription.DataType == DataType.MarketDepth)
				{
					message = CreateDepth(subscription.TransactionId,
						subscription.SecurityId, quoteTime, data.Quote.Bid,
						data.Quote.BidSize, data.Quote.Ask, data.Quote.AskSize);
				}
			}

			if (message == null)
				continue;
			await SendOutMessageAsync(message, cancellationToken);
			emitted.Add(subscription);
		}

		var finished = new List<LiveSubscription>();
		var unsubscribe = new HashSet<ThetaStreamKey>();
		using (_liveSync.EnterScope())
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

			foreach (var subscription in finished)
			{
				var streamKey = subscription.Key.ToStreamKey(subscription.DataType);
				if (!_liveSubscriptions.Values.Any(item =>
					item.Key.ToStreamKey(item.DataType) == streamKey))
				{
					unsubscribe.Add(streamKey);
				}
			}
		}

		foreach (var subscription in finished)
			await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);

		var stream = GetExistingStream();
		if (stream != null && unsubscribe.Count > 0)
			_ = UnsubscribeAfterCallback(stream, [.. unsubscribe]);
	}

	private async Task UnsubscribeAfterCallback(ThetaDataWebSocketClient stream,
		ThetaStreamKey[] keys)
	{
		await Task.Yield();
		foreach (var key in keys)
		{
			try
			{
				await stream.Unsubscribe(key, CancellationToken.None);
			}
			catch (Exception error)
			{
				if (ReferenceEquals(stream, GetExistingStream()))
					await SendOutErrorAsync(error, CancellationToken.None);
			}
		}
	}
}
