namespace StockSharp.Quodd;

public partial class QuoddMessageAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Ticker { get; init; }
		public QuoddAssetTypes AssetType { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly Lock _liveSync = new();
	private QuoddClient _client;

	/// <summary>Initializes a new instance.</summary>
	public QuoddMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[QuoddExtensions.BoardCode, QuoddExtensions.OptionsBoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Address == null || !Address.IsAbsoluteUri || Address.Scheme != Uri.UriSchemeHttps)
			throw new InvalidOperationException("QUODD gRPC address must be an absolute HTTPS URI.");

		var token = Token?.UnSecure();
		var password = Password?.UnSecure();
		var firmPassword = FirmPassword?.UnSecure();
		switch (AuthenticationMode)
		{
			case QuoddAuthenticationModes.Token:
				token.ThrowIfEmpty(nameof(Token));
				break;
			case QuoddAuthenticationModes.Trial:
				Login.ThrowIfEmpty(nameof(Login));
				password.ThrowIfEmpty(nameof(Password));
				ValidateAuthenticationAddress();
				break;
			case QuoddAuthenticationModes.Firm:
				Login.ThrowIfEmpty(nameof(Login));
				FirmLogin.ThrowIfEmpty(nameof(FirmLogin));
				firmPassword.ThrowIfEmpty(nameof(FirmPassword));
				ValidateAuthenticationAddress();
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(AuthenticationMode),
					AuthenticationMode, null);
		}

		var provider = new QuoddTokenProvider(AuthenticationMode, AuthenticationAddress,
			token, Login, password, FirmLogin, firmPassword);
		_client = new(Address, provider, Math.Max(1, ReConnectionSettings.ReAttemptCount));
		_client.SnapReceived += OnSnapReceived;
		_client.Error += SendOutErrorAsync;
		try
		{
			await _client.Connect(ValidationTicker?.Trim(), cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClient();
		ClearLiveSubscriptions();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClient();
		ClearLiveSubscriptions();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void ValidateAuthenticationAddress()
	{
		if (AuthenticationAddress == null || !AuthenticationAddress.IsAbsoluteUri ||
			AuthenticationAddress.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidOperationException(
				"QUODD authentication address must be an absolute HTTPS URI.");
		}
	}

	private async ValueTask DisposeClient()
	{
		var client = _client;
		_client = null;
		if (client == null)
			return;

		client.SnapReceived -= OnSnapReceived;
		client.Error -= SendOutErrorAsync;
		await client.DisposeAsync();
	}

	private QuoddClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void ClearLiveSubscriptions()
	{
		using var scope = _liveSync.EnterScope();
		_liveSubscriptions.Clear();
	}

	private async ValueTask OnSnapReceived(SnapMessage snap, QuoddAssetTypes assetType,
		CancellationToken cancellationToken)
	{
		if (snap == null)
			return;
		if (!snap.Error.IsEmpty())
		{
			await SendOutErrorAsync(new QuoddApiException(
				$"QUODD stream error for '{snap.Ticker}': {snap.Error}"), cancellationToken);
			return;
		}
		if (snap.Ticker.IsEmpty())
			return;

		LiveSubscription[] subscriptions;
		using (var scope = _liveSync.EnterScope())
		{
			subscriptions = _liveSubscriptions.Values
				.Where(subscription => subscription.AssetType == assetType &&
					subscription.Ticker.EqualsIgnoreCase(snap.Ticker))
				.ToArray();
		}

		var emitted = new List<long>(subscriptions.Length);
		foreach (var subscription in subscriptions)
		{
			var message = snap.ToLevel1Message(subscription.TransactionId,
				subscription.SecurityId);
			if (message.Changes.Count == 0)
				continue;
			await SendOutMessageAsync(message, cancellationToken);
			emitted.Add(subscription.TransactionId);
		}

		if (emitted.Count == 0)
			return;

		var finished = new List<long>();
		var unsubscribe = false;
		using (var scope = _liveSync.EnterScope())
		{
			foreach (var transactionId in emitted)
			{
				if (!_liveSubscriptions.TryGetValue(transactionId, out var subscription))
					continue;
				if (subscription.Remaining is > 0 && --subscription.Remaining == 0)
				{
					_liveSubscriptions.Remove(transactionId);
					finished.Add(transactionId);
				}
			}
			unsubscribe = finished.Count > 0 && !_liveSubscriptions.Values.Any(subscription =>
				subscription.AssetType == assetType &&
				subscription.Ticker.EqualsIgnoreCase(snap.Ticker));
		}

		foreach (var transactionId in finished)
			await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
		if (unsubscribe)
			_client?.Unsubscribe(assetType, snap.Ticker);
	}

	private void AddLiveSubscription(MarketDataMessage message, SecurityId securityId,
		string ticker, QuoddAssetTypes assetType, long? remaining)
	{
		var subscription = new LiveSubscription
		{
			TransactionId = message.TransactionId,
			SecurityId = securityId,
			Ticker = ticker,
			AssetType = assetType,
			Remaining = remaining,
		};

		var isFirst = false;
		using (var scope = _liveSync.EnterScope())
		{
			if (_liveSubscriptions.ContainsKey(message.TransactionId))
				throw new InvalidOperationException(
					$"QUODD subscription {message.TransactionId} already exists.");
			isFirst = !_liveSubscriptions.Values.Any(item => item.AssetType == assetType &&
				item.Ticker.EqualsIgnoreCase(ticker));
			_liveSubscriptions.Add(message.TransactionId, subscription);
		}

		try
		{
			if (isFirst)
				SafeClient().Subscribe(assetType, ticker);
		}
		catch
		{
			using var scope = _liveSync.EnterScope();
			_liveSubscriptions.Remove(message.TransactionId);
			throw;
		}
	}

	private void RemoveLiveSubscription(long transactionId)
	{
		LiveSubscription removed;
		var unsubscribe = false;
		using (var scope = _liveSync.EnterScope())
		{
			if (!_liveSubscriptions.Remove(transactionId, out removed))
				return;
			unsubscribe = !_liveSubscriptions.Values.Any(item =>
				item.AssetType == removed.AssetType &&
				item.Ticker.EqualsIgnoreCase(removed.Ticker));
		}
		if (unsubscribe)
			SafeClient().Unsubscribe(removed.AssetType, removed.Ticker);
	}
}
