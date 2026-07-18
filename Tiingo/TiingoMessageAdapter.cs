namespace StockSharp.Tiingo;

public partial class TiingoMessageAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public TiingoSecurityKey Key { get; init; }
		public DataType DataType { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly object _liveSync = new();
	private readonly object _streamSync = new();
	private TiingoRestClient _rest;
	private TiingoWebSocketClient _stockStream;
	private TiingoWebSocketClient _forexStream;
	private TiingoWebSocketClient _cryptoStream;
	private string _streamToken;
	private int _streamAttempts;

	/// <summary>Initializes a new instance.</summary>
	public TiingoMessageAdapter(IdGenerator transactionIdGenerator)
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
		[Extensions.StockBoard, Extensions.ForexBoard, Extensions.CryptoBoard];

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
		ValidateUri(Address, Uri.UriSchemeHttps, "Tiingo REST address");
		ValidateUri(IexWebSocketAddress, "wss", "Tiingo IEX WebSocket address");
		ValidateUri(ForexWebSocketAddress, "wss", "Tiingo forex WebSocket address");
		ValidateUri(CryptoWebSocketAddress, "wss", "Tiingo crypto WebSocket address");
		ValidateUri(SupportedTickersAddress, Uri.UriSchemeHttps,
			"Tiingo supported-tickers address");

		_streamToken = Token.UnSecure();
		_streamAttempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(Address, SupportedTickersAddress, _streamToken, _streamAttempts)
		{
			Parent = this,
		};
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

	private TiingoRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private TiingoWebSocketClient GetOrCreateStream(TiingoMarkets market)
	{
		lock (_streamSync)
		{
			var stream = GetStream(market);
			if (stream != null)
				return stream;
			var address = market switch
			{
				TiingoMarkets.Stocks => IexWebSocketAddress,
				TiingoMarkets.Forex => ForexWebSocketAddress,
				TiingoMarkets.Crypto => CryptoWebSocketAddress,
				_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
			};
			var threshold = market switch
			{
				TiingoMarkets.Stocks => EquityStreamingMode.ToThreshold(),
				TiingoMarkets.Forex => 5,
				TiingoMarkets.Crypto => CryptoExchange.IsEmpty() ? 5 : 2,
				_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
			};
			stream = new(market, address, _streamToken, threshold, _streamAttempts)
			{
				Parent = this,
			};
			stream.DataReceived += OnStreamData;
			stream.Error += SendOutErrorAsync;
			SetStream(market, stream);
			return stream;
		}
	}

	private TiingoWebSocketClient GetStream(TiingoMarkets market)
		=> market switch
		{
			TiingoMarkets.Stocks => _stockStream,
			TiingoMarkets.Forex => _forexStream,
			TiingoMarkets.Crypto => _cryptoStream,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	private void SetStream(TiingoMarkets market, TiingoWebSocketClient stream)
	{
		switch (market)
		{
			case TiingoMarkets.Stocks:
				_stockStream = stream;
				break;
			case TiingoMarkets.Forex:
				_forexStream = stream;
				break;
			case TiingoMarkets.Crypto:
				_cryptoStream = stream;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(market), market, null);
		}
	}

	private async Task DisposeUnusedStream(TiingoMarkets market)
	{
		TiingoWebSocketClient stream;
		lock (_liveSync)
		{
			if (_liveSubscriptions.Values.Any(item => item.Key.Market == market))
				return;
			lock (_streamSync)
			{
				stream = GetStream(market);
				SetStream(market, null);
			}
		}
		await DisposeStream(stream);
	}

	private async Task DisposeClients()
	{
		TiingoWebSocketClient[] streams;
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
	}

	private async Task DisposeStream(TiingoWebSocketClient stream)
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
			throw new InvalidOperationException($"{name} must be an absolute {scheme.ToUpperInvariant()} URI.");
	}
}
