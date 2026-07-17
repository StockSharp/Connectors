namespace StockSharp.CapitalFutures;

public partial class CapitalFuturesMessageAdapter
{
	private readonly SynchronizedDictionary<string, CapitalInstrumentInfo> _instruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, CapitalTrackedOrder> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _orderSync = new(1, 1);
	private CapitalFuturesSdkClient _client;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastConnectionCheck;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="CapitalFuturesMessageAdapter"/>.</summary>
	public CapitalFuturesMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(15);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["TAIFEX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var client = new CapitalFuturesSdkClient(SdkPath, Login, Password, Account,
			Environment, IsTradingEnabled, LogPath)
		{
			Parent = this,
		};
		client.QuoteReceived += OnQuote;
		client.TradeReceived += OnTrade;
		client.BookReceived += OnBook;
		client.OrderReceived += OnOrder;
		client.Error += SendOutErrorAsync;
		client.ConnectionLost += OnConnectionLost;
		_client = client;

		try
		{
			await client.ConnectAsync(cancellationToken);
			connectMsg.SessionId = client.Version;
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClientAsync();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClientAsync();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientAsync();
		_instruments.Clear();
		_orders.Clear();
		_transactionOrders.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioFilter = null;
		_lastConnectionCheck = default;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var now = CurrentTime;
		if (_client != null && now - _lastConnectionCheck >= TimeSpan.FromSeconds(15))
		{
			await _client.KeepAliveAsync(cancellationToken);
			if (!await _client.IsConnectedAsync(cancellationToken))
				await SendOutErrorAsync(new InvalidOperationException("Capital Futures quote connection is not active."),
					cancellationToken);
			_lastConnectionCheck = now;
		}
		if (_portfolioSubscriptionId != 0 &&
			now - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
			await SendPortfolioSnapshot(_portfolioSubscriptionId, _portfolioFilter, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private ValueTask OnConnectionLost(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async Task DisposeClientAsync()
	{
		var client = _client;
		if (client == null)
			return;
		_client = null;
		client.QuoteReceived -= OnQuote;
		client.TradeReceived -= OnTrade;
		client.BookReceived -= OnBook;
		client.OrderReceived -= OnOrder;
		client.Error -= SendOutErrorAsync;
		client.ConnectionLost -= OnConnectionLost;
		try
		{
			await client.DisconnectAsync();
		}
		finally
		{
			client.Dispose();
		}
	}

	private void CacheInstrument(CapitalInstrumentInfo instrument)
	{
		if (instrument?.Symbol.IsEmpty() != false)
			return;
		_instruments[GetInstrumentKey(instrument.SecurityType, instrument.Symbol)] = instrument;
	}

	private async Task<CapitalInstrumentInfo> ResolveInstrumentAsync(SecurityId securityId,
		SecurityTypes? securityType, CancellationToken cancellationToken)
	{
		var symbol = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (securityType is SecurityTypes type &&
			_instruments.TryGetValue(GetInstrumentKey(type, symbol), out var cached))
			return cached;
		var instrument = await _client.GetInstrumentAsync(symbol, securityType, cancellationToken);
		CacheInstrument(instrument);
		return instrument;
	}

	private static string GetInstrumentKey(SecurityTypes securityType, string symbol)
		=> $"{securityType}|{symbol?.ToUpperInvariant()}";
}
