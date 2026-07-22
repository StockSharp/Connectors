namespace StockSharp.Zerodha;

public partial class ZerodhaMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public long InstrumentToken { get; init; }
		public DataType DataType { get; init; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; set; }
		public decimal Price { get; set; }
		public decimal Volume { get; set; }
		public ZerodhaOrderCondition Condition { get; init; }
		public decimal ReportedFilled { get; set; }
	}

	private ZerodhaRestClient _rest;
	private ZerodhaWebSocketClient _stream;
	private KiteProfile _profile;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<long, KiteInstrument> _instruments = [];
	private readonly SynchronizedDictionary<string, KiteInstrument> _instrumentsBySymbol =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFillVolumes =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _lastTicks = [];
	private readonly SynchronizedSet<string> _reportedTrades = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="ZerodhaMessageAdapter"/> class.</summary>
	public ZerodhaMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(ZerodhaExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NSE", "BSE", "NFO", "BFO", "CDS", "BCD", "MCX"];

	private string PortfolioName => _profile?.UserId.IsEmpty("ZERODHA");

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_rest != null || _stream != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var apiKey = Key?.UnSecure().ThrowIfEmpty(nameof(Key));
		var accessToken = Token?.UnSecure();
		_rest = new(apiKey, accessToken, Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
		try
		{
			if (accessToken.IsEmpty())
			{
				var session = await _rest.ExchangeToken(RequestToken?.UnSecure(), Secret?.UnSecure(),
					cancellationToken);
				accessToken = session.AccessToken;
				Token = accessToken.Secure();
			}

			_profile = await _rest.GetProfile(cancellationToken)
				?? throw new InvalidDataException("Zerodha returned no user profile.");
			_stream = new(apiKey, accessToken, Math.Max(1, ReConnectionSettings.ReAttemptCount))
			{
				Parent = this,
			};
			_stream.TickReceived += ProcessTick;
			_stream.OrderReceived += ProcessOrderUpdate;
			_stream.Error += SendOutErrorAsync;
			_stream.StateChanged += SendOutConnectionStateAsync;
			await _stream.Connect(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null || _stream == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		await _stream.Disconnect();
		DisposeClients();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClients();
		_marketSubscriptions.Clear();
		_instruments.Clear();
		_instrumentsBySymbol.Clear();
		_orders.Clear();
		_orderFillVolumes.Clear();
		_lastTicks.Clear();
		_reportedTrades.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId != 0 &&
			DateTime.UtcNow - _lastPortfolioRefresh >= TimeSpan.FromSeconds(15))
		{
			await SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);
			_lastPortfolioRefresh = DateTime.UtcNow;
		}
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private ZerodhaRestClient GetRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void CacheInstrument(KiteInstrument instrument)
	{
		if (instrument?.InstrumentToken <= 0 || instrument.TradingSymbol.IsEmpty() || instrument.Exchange.IsEmpty())
			return;
		_instruments[instrument.InstrumentToken] = instrument;
		_instrumentsBySymbol[$"{instrument.Exchange}:{instrument.TradingSymbol}"] = instrument;
	}

	private async Task<KiteInstrument> ResolveInstrument(SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var token = securityId.Native?.To<long?>();
		if (token is > 0 && _instruments.TryGetValue(token.Value, out var instrument))
			return instrument;
		var key = $"{securityId.BoardCode}:{securityId.SecurityCode}";
		if (_instrumentsBySymbol.TryGetValue(key, out instrument))
			return instrument;

		foreach (var item in await GetRest().GetInstruments(cancellationToken))
			CacheInstrument(item);
		if (token is > 0 && _instruments.TryGetValue(token.Value, out instrument))
			return instrument;
		if (_instrumentsBySymbol.TryGetValue(key, out instrument))
			return instrument;
		throw new InvalidOperationException($"Zerodha instrument '{securityId}' was not found. Run security lookup first.");
	}

	private void DisposeClients()
	{
		if (_stream != null)
		{
			_stream.TickReceived -= ProcessTick;
			_stream.OrderReceived -= ProcessOrderUpdate;
			_stream.Error -= SendOutErrorAsync;
			_stream.StateChanged -= SendOutConnectionStateAsync;
			_stream.Dispose();
			_stream = null;
		}

		_rest?.Dispose();
		_rest = null;
		_profile = null;
	}
}
