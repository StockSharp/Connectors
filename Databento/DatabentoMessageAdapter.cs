namespace StockSharp.Databento;

using StockSharp.Databento.Native;

public partial class DatabentoMessageAdapter
{
	private sealed class MarketSubscription
	{
		private readonly object _sync = new();
		private readonly HashSet<uint> _instrumentIds = [];

		public long TransactionId { get; init; }
		public SecurityId RequestedSecurityId { get; init; }
		public DataType DataType { get; init; }
		public string Schema { get; init; }
		public TimeSpan? TimeFrame { get; init; }
		public string LiveKey { get; init; }

		public void Bind(uint instrumentId)
		{
			lock (_sync)
				_instrumentIds.Add(instrumentId);
		}

		public bool IsBound(uint instrumentId)
		{
			lock (_sync)
				return _instrumentIds.Contains(instrumentId);
		}
	}

	private sealed class MboOrderState
	{
		public decimal Price { get; init; }
		public decimal Size { get; init; }
		public Sides Side { get; init; }
	}

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<uint, SecurityId> _instrumentSecurities = [];
	private readonly object _liveGroupsSync = new();
	private readonly Dictionary<string, HashSet<long>> _liveGroups = new(StringComparer.Ordinal);
	private readonly object _symbolMappingsSync = new();
	private readonly Dictionary<string, HashSet<uint>> _symbolMappings =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _liveConnectLock = new(1, 1);
	private readonly object _mboSync = new();
	private readonly Dictionary<(long transactionId, uint instrumentId, string orderKey), MboOrderState>
		_mboOrders = [];

	private DatabentoLiveClient _liveClient;
	private DatabentoHistoricalClient _historicalClient;
	private bool _isLiveConnected;

	/// <summary>Initializes a new instance of the <see cref="DatabentoMessageAdapter"/> class.</summary>
	public DatabentoMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.OrderLog);
		this.AddSupportedCandleTimeFrames(_timeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [Dataset];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_historicalClient != null || _liveClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		var apiKey = Key?.UnSecure();
		if (apiKey.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

		_historicalClient = new(apiKey, HistoricalAddress) { Parent = this };
		_liveClient = new(Dataset, apiKey, LiveAddress, HeartbeatInterval,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		_liveClient.RecordReceived += ProcessLiveRecord;
		_liveClient.Error += SendOutErrorAsync;
		_liveClient.Reconnected += OnLiveReconnected;
		await base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_historicalClient == null || _liveClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		try
		{
			if (_isLiveConnected)
				await _liveClient.Disconnect(cancellationToken);
		}
		finally
		{
			DisposeClients();
			ClearRuntimeState();
		}
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		if (_isLiveConnected && _liveClient != null)
		{
			try
			{
				await _liveClient.Disconnect(cancellationToken);
			}
			catch (Exception ex)
			{
				this.AddWarningLog("Databento reset disconnect failed: {0}", ex.Message);
			}
		}
		DisposeClients();
		ClearRuntimeState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void ClearRuntimeState()
	{
		_marketSubscriptions.Clear();
		_instrumentSecurities.Clear();
		lock (_liveGroupsSync)
			_liveGroups.Clear();
		lock (_symbolMappingsSync)
			_symbolMappings.Clear();
		lock (_mboSync)
			_mboOrders.Clear();
	}

	private DatabentoHistoricalClient GetHistoricalClient()
		=> _historicalClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private async Task<DatabentoLiveClient> GetLiveClient(CancellationToken cancellationToken)
	{
		var client = _liveClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		if (_isLiveConnected)
			return client;

		await _liveConnectLock.WaitAsync(cancellationToken);
		try
		{
			if (!_isLiveConnected)
			{
				await client.Connect(cancellationToken);
				_isLiveConnected = true;
			}
			return client;
		}
		finally
		{
			_liveConnectLock.Release();
		}
	}

	private ValueTask OnLiveReconnected(CancellationToken cancellationToken)
	{
		this.AddInfoLog("Databento live subscriptions restored.");
		return default;
	}

	private void DisposeClients()
	{
		_isLiveConnected = false;
		if (_liveClient != null)
		{
			_liveClient.RecordReceived -= ProcessLiveRecord;
			_liveClient.Error -= SendOutErrorAsync;
			_liveClient.Reconnected -= OnLiveReconnected;
			_liveClient.Dispose();
			_liveClient = null;
		}
		_historicalClient?.Dispose();
		_historicalClient = null;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		ClearRuntimeState();
		_liveConnectLock.Dispose();
		base.DisposeManaged();
	}
}
