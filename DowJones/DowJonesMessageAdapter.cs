namespace StockSharp.DowJones;

public partial class DowJonesMessageAdapter
{
	private sealed class LiveNewsSubscription
	{
		private const int _maximumRememberedIds = 10000;
		private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
		private readonly Queue<string> _seenOrder = new();

		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public DateTime CursorUtc { get; set; }
		public long? Remaining { get; set; }

		public bool IsRemembered(string id)
			=> !id.IsEmpty() && _seen.Contains(id);

		public bool TryRemember(string id)
		{
			if (id.IsEmpty())
				return true;
			if (!_seen.Add(id))
				return false;
			_seenOrder.Enqueue(id);
			while (_seenOrder.Count > _maximumRememberedIds)
				_seen.Remove(_seenOrder.Dequeue());
			return true;
		}
	}

	private readonly record struct TimedArticle(DowJonesContentResource Value,
		DateTime Time, string Key);

	private readonly Lock _liveSync = new();
	private readonly Dictionary<long, LiveNewsSubscription> _liveNews = [];
	private DowJonesClient _client;
	private CancellationTokenSource _pollingCts;
	private Task _pollingTask;

	/// <summary>Initializes a new instance.</summary>
	public DowJonesMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.News);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [DowJonesExtensions.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ValidateSettings();

		var client = new DowJonesClient(AuthenticationMode, Address, OAuthAddress,
			Token?.UnSecure(), ClientId, Login, Password?.UnSecure(),
			Math.Max(1, ReConnectionSettings.ReAttemptCount))
		{
			Parent = this,
		};
		_client = client;
		try
		{
			await client.Authenticate(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
			_pollingCts = new();
			_pollingTask = PollNews(_pollingCts.Token);
		}
		catch
		{
			await StopPolling();
			client.Dispose();
			_client = null;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await StopPolling();
		DisposeClient();
		ClearState();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await StopPolling();
		DisposeClient();
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private DowJonesClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
	}

	private void ValidateSettings()
	{
		ValidateAddress(Address, nameof(Address));
		ValidateAddress(OAuthAddress, nameof(OAuthAddress));
		if (PollingInterval < TimeSpan.FromSeconds(1))
		{
			throw new ArgumentOutOfRangeException(nameof(PollingInterval), PollingInterval,
				"Dow Jones polling interval must be at least one second.");
		}
		if (PageLimit <= 0)
			throw new ArgumentOutOfRangeException(nameof(PageLimit), PageLimit, null);
		if (MaxNewsItems <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxNewsItems), MaxNewsItems, null);
		if (DefaultHistoryLookback <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(DefaultHistoryLookback),
				DefaultHistoryLookback, "Dow Jones history lookback must be positive.");
		}

		switch (AuthenticationMode)
		{
			case DowJonesAuthenticationModes.BearerToken:
			case DowJonesAuthenticationModes.UserKey:
				if (Token.IsEmpty())
					throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
				break;
			case DowJonesAuthenticationModes.ServiceAccount:
				ClientId.ThrowIfEmpty(nameof(ClientId));
				if (Login.IsEmpty())
					throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);
				if (Password.IsEmpty())
					throw new InvalidOperationException(LocalizedStrings.PasswordNotSpecified);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(AuthenticationMode),
					AuthenticationMode, null);
		}
	}

	private async Task PollNews(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(PollingInterval, cancellationToken);
				LiveNewsSubscription[] subscriptions;
				using (var scope = _liveSync.EnterScope())
					subscriptions = [.. _liveNews.Values];

				foreach (var subscription in subscriptions)
				{
					if (cancellationToken.IsCancellationRequested)
						break;
					try
					{
						await PollNews(subscription, cancellationToken);
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						return;
					}
					catch (Exception error)
					{
						await SendOutErrorAsync(error, cancellationToken);
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception error)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
	}

	private async Task PollNews(LiveNewsSubscription subscription,
		CancellationToken cancellationToken)
	{
		if (!IsActive(subscription))
			return;
		var to = DateTime.UtcNow;
		var overlap = TimeSpan.FromMinutes(2);
		var from = subscription.CursorUtc - overlap;
		var target = checked((int)Math.Min(subscription.Remaining ?? MaxNewsItems,
			MaxNewsItems));
		var articles = await SearchNews(subscription.SecurityId, from, to, target,
			false, cancellationToken);

		foreach (var article in articles)
		{
			if (!IsActive(subscription) || subscription.IsRemembered(article.Key))
				continue;
			await SendNews(subscription.TransactionId, subscription.SecurityId,
				article, cancellationToken);
			subscription.TryRemember(article.Key);
			if (subscription.Remaining is > 0 && --subscription.Remaining == 0)
			{
				if (RemoveLiveSubscription(subscription.TransactionId, subscription))
				{
					await SendSubscriptionFinishedAsync(subscription.TransactionId,
						cancellationToken);
				}
				return;
			}
		}
		subscription.CursorUtc = to;
	}

	private void AddLiveSubscription(LiveNewsSubscription subscription)
	{
		using var scope = _liveSync.EnterScope();
		if (!_liveNews.TryAdd(subscription.TransactionId, subscription))
		{
			throw new InvalidOperationException(
				$"Dow Jones news subscription {subscription.TransactionId} already exists.");
		}
	}

	private bool RemoveLiveSubscription(long transactionId,
		LiveNewsSubscription expected = null)
	{
		using var scope = _liveSync.EnterScope();
		if (!_liveNews.TryGetValue(transactionId, out var current) ||
			expected != null && !ReferenceEquals(current, expected))
		{
			return false;
		}
		return _liveNews.Remove(transactionId);
	}

	private bool IsActive(LiveNewsSubscription subscription)
	{
		using var scope = _liveSync.EnterScope();
		return _liveNews.TryGetValue(subscription.TransactionId, out var current) &&
			ReferenceEquals(current, subscription);
	}

	private async Task StopPolling()
	{
		var cancellation = _pollingCts;
		var task = _pollingTask;
		_pollingCts = null;
		_pollingTask = null;
		if (cancellation == null)
			return;
		cancellation.Cancel();
		try
		{
			if (task != null)
				await task;
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			cancellation.Dispose();
		}
	}

	private void ClearState()
	{
		using var scope = _liveSync.EnterScope();
		_liveNews.Clear();
	}

	private static void ValidateAddress(Uri address, string name)
	{
		if (address == null || !address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps)
			throw new InvalidOperationException($"Dow Jones {name} must be an absolute HTTPS URI.");
	}
}
