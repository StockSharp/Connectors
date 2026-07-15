namespace StockSharp.Xtp;

/// <summary>Zhongtai XTP message adapter.</summary>
public partial class XtpMessageAdapter
{
	private XtpNativeClient _client;
	private Channel<Func<CancellationToken, ValueTask>> _callbackQueue;
	private CancellationTokenSource _callbackCancellation;
	private Task _callbackPump;

	/// <summary>Initializes a new instance of the <see cref="XtpMessageAdapter"/>.</summary>
	public XtpMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards { get; } = [BoardCodes.Sse, BoardCodes.Szse, BoardCodes.Bse];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
			throw new PlatformNotSupportedException("The packaged Zhongtai XTP 2.2.50.8 runtime requires Windows x64 or Linux x64 with glibc 2.17 or newer.");
		if (Login.IsEmpty() || Password.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.LoginAndPasswordMustBeSpecified);
		if (ClientId is 0 or > 99)
			throw new ArgumentOutOfRangeException(nameof(ClientId), ClientId, "Regular XTP accounts require a client ID from 1 to 99.");
		if (this.IsMarketData() && QuoteAddress == null)
			throw new InvalidOperationException(LocalizedStrings.XtpQuoteAddressRequired);
		if (this.IsTransactional() && TransactionAddress == null)
			throw new InvalidOperationException(LocalizedStrings.XtpTraderAddressRequired);

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
			_client = new(ClientId, DataPath, SoftwareVersion, SoftwareKey);
			_client.Disconnected += OnNativeDisconnected;
			_client.Error += OnNativeError;
			_client.Security += OnNativeSecurity;
			_client.Depth += OnNativeDepth;
			_client.Tick += OnNativeTick;
			_client.Order += OnNativeOrder;
			_client.Trade += OnNativeTrade;
			_client.CancelError += OnNativeCancelError;
			_client.Position += OnNativePosition;
			_client.Asset += OnNativeAsset;

			if (this.IsMarketData())
				_client.LoginQuote(QuoteAddress, Login, Password, Protocol, LocalAddress);
			if (this.IsTransactional())
				_client.LoginTrader(TransactionAddress, Login, Password, XtpProtocols.Tcp, LocalAddress);

			this.AddInfoLog("Connected to XTP. Quote API {0}, Trader API {1}.", _client.QuoteVersion, _client.TraderVersion);
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

		if (this.IsMarketData())
			_client.LogoutQuote();
		if (this.IsTransactional())
			_client.LogoutTrader();

		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		await ReleaseClient();
		ClearMarketDataState();
		ClearTransactionState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask ReleaseClient()
	{
		if (_client != null)
		{
			_client.Disconnected -= OnNativeDisconnected;
			_client.Error -= OnNativeError;
			_client.Security -= OnNativeSecurity;
			_client.Depth -= OnNativeDepth;
			_client.Tick -= OnNativeTick;
			_client.Order -= OnNativeOrder;
			_client.Trade -= OnNativeTrade;
			_client.CancelError -= OnNativeCancelError;
			_client.Position -= OnNativePosition;
			_client.Asset -= OnNativeAsset;
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
			this.AddWarningLog("XTP callback was ignored because the adapter is stopping.");
	}

	private void OnNativeDisconnected(XtpChannel channel, int reason)
		=> Enqueue(async cancellationToken =>
		{
			await SendOutErrorAsync(new InvalidOperationException($"XTP {channel} channel disconnected. Reason: {reason}."), cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
		});

	private void OnNativeError(XtpChannel channel, XtpNativeError error)
		=> Enqueue(cancellationToken => SendOutErrorAsync(error.ToException(channel.ToString()), cancellationToken));
}
