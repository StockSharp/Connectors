namespace StockSharp.Fugle;

public partial class FugleMessageAdapter
{
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromMinutes(60),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	private readonly SemaphoreSlim _socketSync = new(1, 1);
	private FugleRestClient _restClient;
	private FugleSocketClient _stockSocket;
	private FugleSocketClient _futuresSocket;
	private DateTime _lastHeartbeat;

	/// <summary>Supported candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Initializes a new instance of the <see cref="FugleMessageAdapter"/>.</summary>
	public FugleMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["TWSE", "TPEX", "TAIFEX", "TAIFEX-AH"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (ReconnectAttempts < 0)
			throw new ArgumentOutOfRangeException(nameof(ReconnectAttempts), ReconnectAttempts, "Reconnect attempts cannot be negative.");

		_restClient = new(Token) { Parent = this };
		try
		{
			await _restClient.Validate(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_restClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_stockSocket != null)
			await _stockSocket.Disconnect(cancellationToken);
		if (_futuresSocket != null)
			await _futuresSocket.Disconnect(cancellationToken);
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (CurrentTime - _lastHeartbeat >= TimeSpan.FromSeconds(20))
		{
			if (_stockSocket != null)
				await _stockSocket.SendHeartbeat(cancellationToken);
			if (_futuresSocket != null)
				await _futuresSocket.SendHeartbeat(cancellationToken);
			_lastHeartbeat = CurrentTime;
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClients();
		_liveCandleStates.Clear();
		_lastHeartbeat = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async Task<FugleSocketClient> GetSocket(FugleAssetKinds kind, CancellationToken cancellationToken)
	{
		var socket = kind == FugleAssetKinds.Stock ? _stockSocket : _futuresSocket;
		if (socket != null)
			return socket;

		await _socketSync.WaitAsync(cancellationToken);
		try
		{
			socket = kind == FugleAssetKinds.Stock ? _stockSocket : _futuresSocket;
			if (socket != null)
				return socket;

			socket = new(kind, Token, ReconnectAttempts, ReConnectionSettings.WorkingTime) { Parent = this };
			socket.DataReceived += OnDataReceived;
			socket.Error += SendOutErrorAsync;
			try
			{
				await socket.Connect(cancellationToken);
			}
			catch
			{
				socket.DataReceived -= OnDataReceived;
				socket.Error -= SendOutErrorAsync;
				socket.Dispose();
				throw;
			}

			if (kind == FugleAssetKinds.Stock)
				_stockSocket = socket;
			else
				_futuresSocket = socket;
			return socket;
		}
		finally
		{
			_socketSync.Release();
		}
	}

	private void DisposeClients()
	{
		if (_stockSocket != null)
		{
			_stockSocket.DataReceived -= OnDataReceived;
			_stockSocket.Error -= SendOutErrorAsync;
			_stockSocket.Dispose();
			_stockSocket = null;
		}

		if (_futuresSocket != null)
		{
			_futuresSocket.DataReceived -= OnDataReceived;
			_futuresSocket.Error -= SendOutErrorAsync;
			_futuresSocket.Dispose();
			_futuresSocket = null;
		}

		_restClient?.Dispose();
		_restClient = null;
	}
}
