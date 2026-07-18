namespace StockSharp.ActivFinancial;

public partial class ActivFinancialMessageAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public ActivSecurityKey Key { get; init; }
		public DataType DataType { get; init; }
	}

	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly Lock _liveSync = new();
	private ActivGatewayClient _client;

	/// <summary>Initializes a new instance.</summary>
	public ActivFinancialMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(ActivFinancialExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [ActivFinancialExtensions.BoardCode];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		Login.ThrowIfEmpty(nameof(Login));
		var password = Password?.UnSecure();
		password.ThrowIfEmpty(nameof(Password));
		Host.ThrowIfEmpty(nameof(Host));
		NodePath.ThrowIfEmpty(nameof(NodePath));
		GatewayDirectory.ThrowIfEmpty(nameof(GatewayDirectory));
		ActivFinancialExtensions.ResolveTimeZone(FallbackTimeZoneId);
		if (MaxLookupResults is <= 0 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(MaxLookupResults), MaxLookupResults,
				"ACTIV lookup limit must be between 1 and 100000.");
		if (MaxHistoryResults is <= 0 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(MaxHistoryResults), MaxHistoryResults,
				"ACTIV history limit must be between 1 and 100000.");

		_client = new(NodePath, GatewayDirectory);
		_client.RecordReceived += OnGatewayRecord;
		_client.SubscriptionFinished += OnGatewaySubscriptionFinished;
		_client.Error += OnGatewayError;
		_client.Log += OnGatewayLog;
		try
		{
			await _client.Connect(Host, Login, password, cancellationToken);
			this.AddInfoLog("Connected through ACTIV One API {0} (typed gateway {1}).",
				_client.OneApiVersion, _client.GatewayVersion);
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
		await DisposeClient(cancellationToken);
		ClearLiveSubscriptions();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClient(cancellationToken);
		ClearLiveSubscriptions();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private ActivGatewayClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private async ValueTask DisposeClient(CancellationToken cancellationToken = default)
	{
		var client = _client;
		_client = null;
		if (client == null)
			return;

		client.RecordReceived -= OnGatewayRecord;
		client.SubscriptionFinished -= OnGatewaySubscriptionFinished;
		client.Error -= OnGatewayError;
		client.Log -= OnGatewayLog;
		try
		{
			await client.Disconnect(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	private async ValueTask OnGatewayRecord(long subscriptionId,
		ActivGatewayRecord record, CancellationToken cancellationToken)
	{
		LiveSubscription subscription;
		using (var scope = _liveSync.EnterScope())
			_liveSubscriptions.TryGetValue(subscriptionId, out subscription);
		if (subscription == null || record == null)
			return;

		Message message = subscription.DataType == DataType.Level1
			? CreateLevel1(subscription.TransactionId, subscription.SecurityId, record)
			: CreateTick(subscription.TransactionId, subscription.SecurityId, record);
		if (message != null)
			await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask OnGatewaySubscriptionFinished(long subscriptionId,
		CancellationToken cancellationToken)
	{
		if (!RemoveLiveSubscription(subscriptionId))
			return;
		await SendSubscriptionFinishedAsync(subscriptionId, cancellationToken);
	}

	private async ValueTask OnGatewayError(long? subscriptionId, Exception error,
		CancellationToken cancellationToken)
	{
		await SendOutErrorAsync(error, cancellationToken);
		if (subscriptionId != null && RemoveLiveSubscription(subscriptionId.Value))
			await SendSubscriptionFinishedAsync(subscriptionId.Value, cancellationToken);
	}

	private ValueTask OnGatewayLog(int level, string message,
		CancellationToken cancellationToken)
	{
		if (!message.IsEmpty())
		{
			if (level >= 3)
				this.AddErrorLog(message);
			else
				this.AddWarningLog(message);
		}
		return default;
	}

	private void AddLiveSubscription(LiveSubscription subscription)
	{
		using var scope = _liveSync.EnterScope();
		if (!_liveSubscriptions.TryAdd(subscription.TransactionId, subscription))
		{
			throw new InvalidOperationException(
				$"ACTIV subscription {subscription.TransactionId} already exists.");
		}
	}

	private bool RemoveLiveSubscription(long transactionId)
	{
		using var scope = _liveSync.EnterScope();
		return _liveSubscriptions.Remove(transactionId);
	}

	private void ClearLiveSubscriptions()
	{
		using var scope = _liveSync.EnterScope();
		_liveSubscriptions.Clear();
	}
}
