namespace StockSharp.FinancialModelingPrep;

public partial class FmpMessageAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public FmpSecurityKey Key { get; init; }
		public DataType DataType { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly object _liveSync = new();
	private readonly object _streamSync = new();
	private FmpRestClient _rest;
	private FmpWebSocketClient _stockStream;
	private FmpWebSocketClient _forexStream;
	private FmpWebSocketClient _cryptoStream;
	private string _streamToken;
	private int _streamAttempts;
	private TimeZoneInfo _intradayTimeZone;

	/// <summary>Initializes a new instance.</summary>
	public FmpMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.News);
		this.AddSupportedCandleTimeFrames(Extensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
	[
		Extensions.StockBoard,
		Extensions.ForexBoard,
		Extensions.CryptoBoard,
		Extensions.IndexBoard,
		Extensions.CommodityBoard,
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.News || dataType.IsTFCandles;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null || HasStreams())
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		ValidateUri(Address, Uri.UriSchemeHttps, "FMP REST address");
		ValidateUri(StockWebSocketAddress, "wss", "FMP stock WebSocket address");
		ValidateUri(ForexWebSocketAddress, "wss", "FMP forex WebSocket address");
		ValidateUri(CryptoWebSocketAddress, "wss", "FMP crypto WebSocket address");
		_intradayTimeZone = Extensions.ResolveTimeZone(IntradayTimeZoneId);

		_streamToken = Token.UnSecure();
		_streamAttempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(Address, _streamToken, _streamAttempts) { Parent = this };
		try
		{
			await _rest.Validate(cancellationToken);
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
		if (_rest == null && !HasStreams())
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClients();
		ClearLiveSubscriptions();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClients();
		ClearLiveSubscriptions();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private FmpRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private FmpWebSocketClient GetOrCreateStream(FmpStreamKinds kind)
	{
		lock (_streamSync)
		{
			var stream = GetStream(kind);
			if (stream != null)
				return stream;
			var address = kind switch
			{
				FmpStreamKinds.Stocks => StockWebSocketAddress,
				FmpStreamKinds.Forex => ForexWebSocketAddress,
				FmpStreamKinds.Crypto => CryptoWebSocketAddress,
				_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
			};
			stream = new(kind, address, _streamToken, _streamAttempts) { Parent = this };
			stream.DataReceived += OnStreamData;
			stream.Error += SendOutErrorAsync;
			SetStream(kind, stream);
			return stream;
		}
	}

	private FmpWebSocketClient GetStream(FmpStreamKinds kind)
		=> kind switch
		{
			FmpStreamKinds.Stocks => _stockStream,
			FmpStreamKinds.Forex => _forexStream,
			FmpStreamKinds.Crypto => _cryptoStream,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
		};

	private void SetStream(FmpStreamKinds kind, FmpWebSocketClient stream)
	{
		switch (kind)
		{
			case FmpStreamKinds.Stocks:
				_stockStream = stream;
				break;
			case FmpStreamKinds.Forex:
				_forexStream = stream;
				break;
			case FmpStreamKinds.Crypto:
				_cryptoStream = stream;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
		}
	}

	private FmpWebSocketClient GetExistingStream(FmpStreamKinds kind)
	{
		lock (_streamSync)
			return GetStream(kind);
	}

	private async Task DisposeUnusedStream(FmpStreamKinds kind)
	{
		FmpWebSocketClient stream;
		lock (_liveSync)
		{
			if (_liveSubscriptions.Values.Any(item => NeedsStream(item, kind)))
				return;
			lock (_streamSync)
			{
				stream = GetStream(kind);
				SetStream(kind, null);
			}
		}
		await DisposeStream(stream);
	}

	private async Task DisposeClients()
	{
		FmpWebSocketClient[] streams;
		lock (_streamSync)
		{
			streams = [_stockStream, _forexStream, _cryptoStream];
			_stockStream = null;
			_forexStream = null;
			_cryptoStream = null;
		}
		foreach (var stream in streams.Where(stream => stream != null))
			await DisposeStream(stream);

		_rest?.Dispose();
		_rest = null;
		_streamToken = null;
		_streamAttempts = 0;
		_intradayTimeZone = null;
	}

	private async Task DisposeStream(FmpWebSocketClient stream)
	{
		if (stream == null)
			return;
		stream.DataReceived -= OnStreamData;
		stream.Error -= SendOutErrorAsync;
		try
		{
			await stream.Disconnect();
		}
		finally
		{
			stream.Dispose();
		}
	}

	private bool HasStreams()
	{
		lock (_streamSync)
			return _stockStream != null || _forexStream != null || _cryptoStream != null;
	}

	private void ClearLiveSubscriptions()
	{
		lock (_liveSync)
			_liveSubscriptions.Clear();
	}

	private static void ValidateUri(Uri value, string scheme, string name)
	{
		if (value == null || !value.IsAbsoluteUri || !value.Scheme.EqualsIgnoreCase(scheme))
			throw new InvalidOperationException(
				$"{name} must be an absolute {scheme.ToUpperInvariant()} URI.");
	}
}
