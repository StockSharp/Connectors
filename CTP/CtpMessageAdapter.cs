namespace StockSharp.Ctp;

/// <summary>Shanghai Futures Information Technology CTP message adapter.</summary>
public partial class CtpMessageAdapter
{
	private CtpNativeClient _client;
	private Channel<Func<CancellationToken, ValueTask>> _callbackQueue;
	private CancellationTokenSource _callbackCancellation;
	private Task _callbackPump;
	private readonly HashSet<CtpChannels> _readyChannels = [];
	private readonly SemaphoreSlim _querySync = new(1, 1);
	private DateTime _lastQueryTime;
	private bool _intentionalDisconnect;

	/// <summary>Initializes a new instance of the <see cref="CtpMessageAdapter"/>.</summary>
	public CtpMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["SHFE", "DCE", "CZCE", "CFFEX", "INE", "GFEX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
			throw new PlatformNotSupportedException("The packaged CTP 6.7.11 runtime requires Windows x64 or Linux x64.");
		if (Login.IsEmpty() || Password.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.LoginAndPasswordMustBeSpecified);
		if (BrokerId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.CtpBrokerIdRequired);
		if (DataPath.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.CtpDataDirectoryRequired);
		if (ConnectionTimeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(ConnectionTimeout), ConnectionTimeout, "CTP connection timeout must be positive.");
		if (QueryInterval < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(QueryInterval), QueryInterval, "CTP query interval cannot be negative.");
		if (this.IsMarketData() && MarketDataAddress.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.CtpMarketAddressRequired);
		if (this.IsTransactional() && TraderAddress.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.CtpTraderAddressRequired);
		if (this.IsTransactional() && AppId.IsEmpty() != AuthCode.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.CtpAppAuthPairRequired);

		_intentionalDisconnect = false;
		_readyChannels.Clear();
		_callbackCancellation = new();
		_callbackQueue = Channel.CreateUnbounded<Func<CancellationToken, ValueTask>>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
		});
		_callbackPump = PumpCallbacks(_callbackCancellation.Token);

		try
		{
			_client = new();
			_client.StateChanged += OnNativeStateChanged;
			_client.Error += OnNativeError;
			_client.Instrument += OnNativeInstrument;
			_client.Depth += OnNativeDepth;
			_client.Order += OnNativeOrder;
			_client.Trade += OnNativeTrade;
			_client.Position += OnNativePosition;
			_client.Account += OnNativeAccount;

			if (this.IsTransactional())
				await _client.ConnectTraderAsync(TraderAddress, DataPath, BrokerId, Login, InvestorId, Password, AppId, AuthCode, ProductInfo, ResumeType, ProductionMode, ConnectionTimeout, cancellationToken);
			if (this.IsMarketData())
				await _client.ConnectMarketAsync(MarketDataAddress, DataPath, BrokerId, Login, Password, ProductionMode, ConnectionTimeout, cancellationToken);

			this.AddInfoLog("Connected to CTP. Market Data API {0}, Trader API {1}.", _client.MarketVersion, _client.TraderVersion);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await ReleaseClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_intentionalDisconnect = true;
		if (this.IsMarketData())
			_client.DisconnectMarket();
		if (this.IsTransactional())
			_client.DisconnectTrader();

		await base.DisconnectAsync(disconnectMsg, cancellationToken);
		await ReleaseClient();
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		_intentionalDisconnect = true;
		await ReleaseClient();
		ClearMarketDataState();
		ClearTransactionState();
		_lastQueryTime = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask SendQuery(Action query, CancellationToken cancellationToken)
	{
		await _querySync.WaitAsync(cancellationToken);
		try
		{
			var delay = QueryInterval - (DateTime.UtcNow - _lastQueryTime);
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			query();
			_lastQueryTime = DateTime.UtcNow;
		}
		finally
		{
			_querySync.Release();
		}
	}

	private async ValueTask ReleaseClient()
	{
		if (_client != null)
		{
			_client.StateChanged -= OnNativeStateChanged;
			_client.Error -= OnNativeError;
			_client.Instrument -= OnNativeInstrument;
			_client.Depth -= OnNativeDepth;
			_client.Order -= OnNativeOrder;
			_client.Trade -= OnNativeTrade;
			_client.Position -= OnNativePosition;
			_client.Account -= OnNativeAccount;
			_client.Dispose();
			_client = null;
		}

		_callbackQueue?.Writer.TryComplete();
		_callbackCancellation?.Cancel();
		if (_callbackPump != null)
		{
			try
			{
				await _callbackPump;
			}
			catch (OperationCanceledException)
			{
			}
		}

		_callbackCancellation?.Dispose();
		_callbackCancellation = null;
		_callbackQueue = null;
		_callbackPump = null;
		_readyChannels.Clear();
	}

	private async Task PumpCallbacks(CancellationToken cancellationToken)
	{
		await foreach (var callback in _callbackQueue.Reader.ReadAllAsync(cancellationToken))
		{
			try
			{
				await callback(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
		}
	}

	private void Enqueue(Func<CancellationToken, ValueTask> callback)
	{
		if (_callbackQueue?.Writer.TryWrite(callback) != true)
			this.AddWarningLog("CTP callback was ignored because the adapter is stopping.");
	}

	private void OnNativeStateChanged(CtpChannels channel, CtpNativeConnectionStates state, int reason, CtpNativeError? error)
		=> Enqueue(async cancellationToken =>
		{
			if (state == CtpNativeConnectionStates.Ready)
			{
				_readyChannels.Add(channel);
				return;
			}
			if (state != CtpNativeConnectionStates.Disconnected || !_readyChannels.Remove(channel) || _intentionalDisconnect)
				return;
			var exception = error is { Id: not 0 } value
				? value.ToException($"{channel} disconnect")
				: new InvalidOperationException($"CTP {channel} disconnected. Reason: {reason}.");
			await SendOutErrorAsync(exception, cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
		});

	private void OnNativeError(CtpChannels channel, CtpNativeError error)
		=> Enqueue(cancellationToken => ProcessNativeError(channel, error, cancellationToken));
}
