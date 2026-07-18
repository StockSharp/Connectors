namespace StockSharp.EodHistoricalData;

public partial class EodhdMessageAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public EodhdSecurityKey Key { get; init; }
		public DataType DataType { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly object _liveSync = new();
	private readonly object _streamSync = new();
	private EodhdRestClient _rest;
	private EodhdWebSocketClient _stockTrades;
	private EodhdWebSocketClient _stockQuotes;
	private EodhdWebSocketClient _forexQuotes;
	private EodhdWebSocketClient _cryptoTrades;
	private string _streamToken;
	private int _streamAttempts;

	/// <summary>Initializes a new instance.</summary>
	public EodhdMessageAdapter(IdGenerator transactionIdGenerator)
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
		Extensions.OptionBoard,
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
		if (StockExchange.IsEmpty())
			throw new InvalidOperationException("EODHD stock exchange is not specified.");
		if (MaxWebSocketSymbols <= 0)
			throw new InvalidOperationException("EODHD WebSocket symbol limit must be positive.");
		ValidateUri(Address, Uri.UriSchemeHttps, "EODHD REST address");
		ValidateUri(StockTradeWebSocketAddress, "wss", "EODHD US trade WebSocket address");
		ValidateUri(StockQuoteWebSocketAddress, "wss", "EODHD US quote WebSocket address");
		ValidateUri(ForexWebSocketAddress, "wss", "EODHD forex WebSocket address");
		ValidateUri(CryptoWebSocketAddress, "wss", "EODHD crypto WebSocket address");

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

	private EodhdRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private EodhdWebSocketClient GetOrCreateStream(EodhdStreamKinds kind)
	{
		lock (_streamSync)
		{
			var stream = GetStream(kind);
			if (stream != null)
				return stream;
			var address = kind switch
			{
				EodhdStreamKinds.StockTrades => StockTradeWebSocketAddress,
				EodhdStreamKinds.StockQuotes => StockQuoteWebSocketAddress,
				EodhdStreamKinds.ForexQuotes => ForexWebSocketAddress,
				EodhdStreamKinds.CryptoTrades => CryptoWebSocketAddress,
				_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
			};
			stream = new(kind, address, _streamToken, MaxWebSocketSymbols, _streamAttempts)
			{
				Parent = this,
			};
			stream.DataReceived += OnStreamData;
			stream.Error += SendOutErrorAsync;
			SetStream(kind, stream);
			return stream;
		}
	}

	private EodhdWebSocketClient GetStream(EodhdStreamKinds kind)
		=> kind switch
		{
			EodhdStreamKinds.StockTrades => _stockTrades,
			EodhdStreamKinds.StockQuotes => _stockQuotes,
			EodhdStreamKinds.ForexQuotes => _forexQuotes,
			EodhdStreamKinds.CryptoTrades => _cryptoTrades,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
		};

	private void SetStream(EodhdStreamKinds kind, EodhdWebSocketClient stream)
	{
		switch (kind)
		{
			case EodhdStreamKinds.StockTrades:
				_stockTrades = stream;
				break;
			case EodhdStreamKinds.StockQuotes:
				_stockQuotes = stream;
				break;
			case EodhdStreamKinds.ForexQuotes:
				_forexQuotes = stream;
				break;
			case EodhdStreamKinds.CryptoTrades:
				_cryptoTrades = stream;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
		}
	}

	private EodhdWebSocketClient GetExistingStream(EodhdStreamKinds kind)
	{
		lock (_streamSync)
			return GetStream(kind);
	}

	private async Task DisposeUnusedStream(EodhdStreamKinds kind)
	{
		EodhdWebSocketClient stream;
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
		EodhdWebSocketClient[] streams;
		lock (_streamSync)
		{
			streams = [_stockTrades, _stockQuotes, _forexQuotes, _cryptoTrades];
			_stockTrades = null;
			_stockQuotes = null;
			_forexQuotes = null;
			_cryptoTrades = null;
		}
		foreach (var stream in streams.Where(stream => stream != null))
			await DisposeStream(stream);

		_rest?.Dispose();
		_rest = null;
		_streamToken = null;
		_streamAttempts = 0;
	}

	private async Task DisposeStream(EodhdWebSocketClient stream)
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
			return _stockTrades != null || _stockQuotes != null ||
				_forexQuotes != null || _cryptoTrades != null;
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
