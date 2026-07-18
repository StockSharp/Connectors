namespace StockSharp.ThetaData;

public partial class ThetaDataMessageAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public ThetaSecurityKey Key { get; init; }
		public DataType DataType { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly Lock _liveSync = new();
	private readonly Lock _streamSync = new();
	private ThetaDataRestClient _rest;
	private ThetaDataWebSocketClient _stream;
	private TimeZoneInfo _marketTimeZone;
	private int _streamAttempts;

	/// <summary>Initializes a new instance.</summary>
	public ThetaDataMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(Extensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
	[
		Extensions.StockBoard,
		Extensions.OptionBoard,
		Extensions.IndexBoard,
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null || HasStream())
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ValidateAddress(Address, "ThetaData REST address", Uri.UriSchemeHttp,
			Uri.UriSchemeHttps);
		ValidateAddress(WebSocketAddress, "ThetaData WebSocket address", "ws", "wss");
		if (SessionStart < TimeSpan.Zero || SessionEnd <= SessionStart ||
			SessionEnd >= TimeSpan.FromDays(1))
		{
			throw new InvalidOperationException(
				"ThetaData market session must be an increasing range within one day.");
		}
		_marketTimeZone = Extensions.ResolveMarketTimeZone(MarketTimeZoneId);
		_streamAttempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		var rest = new ThetaDataRestClient(Address, _streamAttempts) { Parent = this };
		_rest = rest;
		try
		{
			await rest.Validate(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			rest.Dispose();
			_rest = null;
			_marketTimeZone = null;
			_streamAttempts = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null && !HasStream())
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

	private ThetaDataRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ThetaDataWebSocketClient GetOrCreateStream()
	{
		using (_streamSync.EnterScope())
		{
			if (_stream != null)
				return _stream;
			_stream = new(WebSocketAddress, _streamAttempts) { Parent = this };
			_stream.DataReceived += OnStreamData;
			_stream.Error += SendOutErrorAsync;
			return _stream;
		}
	}

	private ThetaDataWebSocketClient GetExistingStream()
	{
		using (_streamSync.EnterScope())
			return _stream;
	}

	private bool HasStream()
	{
		using (_streamSync.EnterScope())
			return _stream != null;
	}

	private async Task DisposeClients()
	{
		ThetaDataWebSocketClient stream;
		using (_streamSync.EnterScope())
		{
			stream = _stream;
			_stream = null;
		}
		if (stream != null)
		{
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
		_rest?.Dispose();
		_rest = null;
		_marketTimeZone = null;
		_streamAttempts = 0;
	}

	private void ClearLiveSubscriptions()
	{
		using (_liveSync.EnterScope())
			_liveSubscriptions.Clear();
	}

	private static void ValidateAddress(Uri address, string name, params string[] schemes)
	{
		if (address == null || !address.IsAbsoluteUri ||
			!schemes.Any(scheme => address.Scheme.EqualsIgnoreCase(scheme)))
		{
			throw new InvalidOperationException(
				$"{name} must be an absolute {string.Join("/", schemes).ToUpperInvariant()} URI.");
		}
	}
}
