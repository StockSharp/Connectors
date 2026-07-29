namespace StockSharp.Intrinio.Native;

readonly record struct IntrinioUnsubscription(
	string Symbol,
	bool IsOption,
	bool IsLastForFeed);

sealed class IntrinioStreamSubscription
{
	private readonly Lock _deliverySync = new();
	private TaskCompletionSource _deactivated;
	private int _deliveryCount;
	private bool _isActive = true;

	public long TransactionId { get; init; }
	public SecurityId SecurityId { get; init; }
	public string Symbol { get; init; }
	public DataType DataType { get; init; }
	public bool IsOption { get; init; }

	public bool TryEnterDelivery()
	{
		lock (_deliverySync)
		{
			if (!_isActive)
				return false;

			_deliveryCount++;
			return true;
		}
	}

	public void ExitDelivery()
	{
		TaskCompletionSource deactivated = null;
		lock (_deliverySync)
		{
			if (_deliveryCount <= 0)
				throw new InvalidOperationException("Intrinio delivery is not active.");

			_deliveryCount--;
			if (_deliveryCount == 0)
			{
				deactivated = _deactivated;
				_deactivated = null;
			}
		}

		deactivated?.TrySetResult();
	}

	public ValueTask DeactivateAsync()
	{
		lock (_deliverySync)
		{
			_isActive = false;
			if (_deliveryCount == 0)
				return ValueTask.CompletedTask;

			_deactivated ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
			return new(_deactivated.Task);
		}
	}

	public void Activate()
	{
		lock (_deliverySync)
		{
			if (_deliveryCount != 0)
			{
				throw new InvalidOperationException(
					"Cannot activate an Intrinio subscription with active deliveries.");
			}

			_isActive = true;
		}
	}
}

sealed class IntrinioSubscriptionIndex
{
	private readonly record struct FeedKey(bool IsOption, string Symbol);

	private sealed class FeedKeyComparer : IEqualityComparer<FeedKey>
	{
		public bool Equals(FeedKey x, FeedKey y)
			=> x.IsOption == y.IsOption &&
				StringComparer.OrdinalIgnoreCase.Equals(x.Symbol, y.Symbol);

		public int GetHashCode(FeedKey obj)
			=> HashCode.Combine(obj.IsOption,
				StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Symbol));
	}

	private static readonly FeedKeyComparer _feedKeyComparer = new();

	private readonly ConcurrentDictionary<long, IntrinioStreamSubscription>
		_byTransaction = [];
	private readonly ConcurrentDictionary<FeedKey,
		ConcurrentDictionary<long, IntrinioStreamSubscription>> _byFeed =
		new(_feedKeyComparer);

	public bool TryAdd(IntrinioStreamSubscription subscription, out bool isFirstForFeed)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		subscription.Symbol.ThrowIfEmpty(nameof(subscription.Symbol));

		if (!_byTransaction.TryAdd(subscription.TransactionId, subscription))
		{
			isFirstForFeed = false;
			return false;
		}

		var key = GetFeedKey(subscription);
		var feed = _byFeed.GetOrAdd(key, static _ => []);
		if (!feed.TryAdd(subscription.TransactionId, subscription))
		{
			_byTransaction.TryRemove(subscription.TransactionId, out _);
			throw new InvalidOperationException(
				$"Intrinio feed index already contains subscription {subscription.TransactionId}.");
		}

		isFirstForFeed = feed.Count == 1;
		return true;
	}

	public bool TryRemove(long transactionId, out IntrinioStreamSubscription subscription,
		out bool isLastForFeed)
	{
		if (!_byTransaction.TryRemove(transactionId, out subscription))
		{
			isLastForFeed = false;
			return false;
		}

		var key = GetFeedKey(subscription);
		if (!_byFeed.TryGetValue(key, out var feed) ||
			!feed.TryRemove(transactionId, out _))
		{
			_byTransaction.TryAdd(transactionId, subscription);
			throw new InvalidOperationException(
				$"Intrinio feed index does not contain subscription {transactionId}.");
		}

		isLastForFeed = feed.IsEmpty;
		if (isLastForFeed)
			_byFeed.TryRemove(key, out _);
		return true;
	}

	public bool TryGet(long transactionId, out IntrinioStreamSubscription subscription,
		out bool isLastForFeed)
	{
		if (!_byTransaction.TryGetValue(transactionId, out subscription))
		{
			isLastForFeed = false;
			return false;
		}

		var key = GetFeedKey(subscription);
		if (!_byFeed.TryGetValue(key, out var feed) ||
			!feed.ContainsKey(transactionId))
		{
			throw new InvalidOperationException(
				$"Intrinio feed index does not contain subscription {transactionId}.");
		}

		isLastForFeed = feed.Count == 1;
		return true;
	}

	public IntrinioStreamSubscription[] Match(bool isOption, string symbol)
		=> _byFeed.TryGetValue(new(isOption, symbol), out var feed)
			? [.. feed.Values]
			: [];

	public IntrinioStreamSubscription[] Clear()
	{
		var subscriptions = _byTransaction.Values.ToArray();
		_byTransaction.Clear();
		_byFeed.Clear();
		return subscriptions;
	}

	private static FeedKey GetFeedKey(IntrinioStreamSubscription subscription)
		=> new(subscription.IsOption, subscription.Symbol);
}
