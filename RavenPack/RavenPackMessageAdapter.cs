namespace StockSharp.RavenPack;

sealed record RavenPackResolvedSecurity(string EntityId, SecurityMessage Security);

public partial class RavenPackMessageAdapter
{
	private sealed class LiveNewsSubscription
	{
		public long TransactionId { get; init; }
		public string EntityId { get; init; }
		public SecurityId SecurityId { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Lock _liveSync = new();
	private readonly Dictionary<long, LiveNewsSubscription> _liveNews = [];
	private readonly Lock _securitySync = new();
	private readonly Dictionary<SecurityId, RavenPackResolvedSecurity> _securityCache = [];
	private readonly SemaphoreSlim _feedStartLock = new(1, 1);

	private RavenPackRestClient _rest;
	private RavenPackFeedClient _feed;

	/// <summary>Initializes a new instance.</summary>
	public RavenPackMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.News);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [RavenPackExtensions.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null || _feed != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		DatasetId = DatasetId?.Trim();
		DatasetId.ThrowIfEmpty(nameof(DatasetId));
		ValidateAddress(Address, nameof(Address));
		ValidateAddress(FeedAddress, nameof(FeedAddress));
		if (MaxRecords is <= 0 or > 10000)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxRecords), MaxRecords,
				"RavenPack synchronous JSON queries support between 1 and 10000 records.");
		}
		if (DefaultHistoryLookback <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(DefaultHistoryLookback),
				DefaultHistoryLookback, "RavenPack history lookback must be positive.");
		}

		var apiKey = Token.UnSecure();
		var attempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(Address, apiKey, attempts) { Parent = this };
		try
		{
			var dataset = await _rest.GetDataset(DatasetId, cancellationToken)
				?? throw new InvalidOperationException($"RavenPack dataset '{DatasetId}' was not returned.");
			if (!dataset.Frequency.IsEmpty() && !dataset.Frequency.EqualsIgnoreCase("granular"))
			{
				throw new InvalidOperationException(
					$"RavenPack dataset '{DatasetId}' must use granular frequency for real-time news.");
			}
			var expectedProduct = Product == RavenPackProducts.Edge ? "edge" : "rpa";
			if (!dataset.Product.IsEmpty() && !dataset.Product.EqualsIgnoreCase(expectedProduct))
			{
				throw new InvalidOperationException(
					$"RavenPack dataset product '{dataset.Product}' does not match '{expectedProduct}'.");
			}
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null && _feed == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClients();
		ClearState();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClients();
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private RavenPackRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private async Task EnsureFeed(CancellationToken cancellationToken)
	{
		await _feedStartLock.WaitAsync(cancellationToken);
		try
		{
			if (_feed?.IsStopped == false)
				return;
			if (_feed != null)
			{
				DetachFeed(_feed);
				_feed.Dispose();
				_feed = null;
			}

			var apiKey = Token?.UnSecure();
			apiKey.ThrowIfEmpty(nameof(Token));
			var feed = new RavenPackFeedClient(FeedAddress, DatasetId, apiKey,
				Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
			feed.RecordReceived += OnFeedRecord;
			feed.Error += OnFeedError;
			_feed = feed;
			try
			{
				await feed.Connect(cancellationToken);
			}
			catch
			{
				DetachFeed(feed);
				_feed = null;
				feed.Dispose();
				throw;
			}
		}
		finally
		{
			_feedStartLock.Release();
		}
	}

	private async Task DisposeClients()
	{
		var feed = _feed;
		_feed = null;
		if (feed != null)
		{
			DetachFeed(feed);
			try
			{
				await feed.Disconnect();
			}
			finally
			{
				feed.Dispose();
			}
		}

		_rest?.Dispose();
		_rest = null;
	}

	private void DetachFeed(RavenPackFeedClient feed)
	{
		feed.RecordReceived -= OnFeedRecord;
		feed.Error -= OnFeedError;
	}

	private async ValueTask OnFeedError(Exception error, bool isTerminal,
		CancellationToken cancellationToken)
	{
		await SendOutErrorAsync(error, cancellationToken);
		if (!isTerminal)
			return;

		long[] finished;
		using (var scope = _liveSync.EnterScope())
		{
			finished = [.. _liveNews.Keys];
			_liveNews.Clear();
		}
		foreach (var transactionId in finished)
			await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
	}

	private async ValueTask OnFeedRecord(RavenPackAnalyticsRecord record,
		CancellationToken cancellationToken)
	{
		if (record == null || !record.TryGetTimestamp(out var serverTime))
			return;

		LiveNewsSubscription[] subscriptions;
		var finished = new HashSet<long>();
		using (var scope = _liveSync.EnterScope())
		{
			subscriptions = _liveNews.Values
				.Where(subscription => record.MatchesEntity(subscription.EntityId)).ToArray();
			foreach (var subscription in subscriptions)
			{
				if (subscription.Remaining is > 0 && --subscription.Remaining == 0)
				{
					_liveNews.Remove(subscription.TransactionId);
					finished.Add(subscription.TransactionId);
				}
			}
		}

		foreach (var subscription in subscriptions)
		{
			await SendNews(subscription.TransactionId, subscription.SecurityId,
				record, serverTime, cancellationToken);
			if (finished.Contains(subscription.TransactionId))
				await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
		}
	}

	private void AddLiveSubscription(LiveNewsSubscription subscription)
	{
		using var scope = _liveSync.EnterScope();
		if (!_liveNews.TryAdd(subscription.TransactionId, subscription))
		{
			throw new InvalidOperationException(
				$"RavenPack news subscription {subscription.TransactionId} already exists.");
		}
	}

	private bool RemoveLiveSubscription(long transactionId)
	{
		using var scope = _liveSync.EnterScope();
		return _liveNews.Remove(transactionId);
	}

	private RavenPackResolvedSecurity GetCachedSecurity(SecurityId securityId)
	{
		using var scope = _securitySync.EnterScope();
		return _securityCache.TryGetValue(securityId, out var value) ? value : null;
	}

	private void CacheSecurity(SecurityId requested, RavenPackResolvedSecurity resolved)
	{
		using var scope = _securitySync.EnterScope();
		if (requested != default)
			_securityCache[requested] = resolved;
		_securityCache[resolved.Security.SecurityId] = resolved;
	}

	private void ClearState()
	{
		using (var scope = _liveSync.EnterScope())
			_liveNews.Clear();
		using (var scope = _securitySync.EnterScope())
			_securityCache.Clear();
	}

	private static void ValidateAddress(Uri address, string name)
	{
		if (address == null || !address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps)
			throw new InvalidOperationException($"RavenPack {name} must be an absolute HTTPS URI.");
	}
}
