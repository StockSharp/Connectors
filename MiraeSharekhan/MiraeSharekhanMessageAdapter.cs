namespace StockSharp.MiraeSharekhan;

public partial class MiraeSharekhanMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string StreamKey { get; init; }
		public DataType DataType { get; init; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public MiraeSharekhanOrderRequest Request { get; set; }
		public SecurityId SecurityId { get; init; }
		public OrderTypes OrderType { get; set; }
		public decimal ReportedFilled { get; set; }
	}

	private MiraeSharekhanRestClient _rest;
	private MiraeSharekhanWebSocketClient _stream;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, MiraeSharekhanInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, MiraeSharekhanInstrument> _instrumentsBySymbol =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, (DateTime time, decimal price, decimal volume)> _lastTicks =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _reportedTrades = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="MiraeSharekhanMessageAdapter"/> class.</summary>
	public MiraeSharekhanMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(MiraeSharekhanExtensions.TimeFrames);
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
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NC", "BC", "NF", "BF", "RN", "RB", "MX"];

	private string PortfolioName => CustomerId.IsEmpty("SHAREKHAN");

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null || _stream != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		var apiKey = ApiKey.ThrowIfEmpty(nameof(ApiKey));
		var accessToken = Token?.UnSecure().ThrowIfEmpty(nameof(Token));
		if (this.IsTransactional())
			CustomerId.ThrowIfEmpty(nameof(CustomerId));

		_rest = new(apiKey, accessToken, VendorKey, Math.Max(1, ReConnectionSettings.ReAttemptCount))
		{
			Parent = this,
		};
		try
		{
			if (this.IsMarketData())
			{
				_stream = new(apiKey, accessToken, Math.Max(1, ReConnectionSettings.ReAttemptCount))
				{
					Parent = this,
				};
				_stream.FeedReceived += ProcessFeed;
				_stream.Error += SendOutErrorAsync;
				_stream.StateChanged += SendOutConnectionStateAsync;
				await _stream.Connect(cancellationToken);
			}
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await ResetClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		if (_stream != null)
			await _stream.Disconnect();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId != 0 &&
			DateTime.UtcNow - _lastOrderRefresh >= TimeSpan.FromSeconds(5))
			await RefreshOrderStatus(cancellationToken);
		if (_portfolioSubscriptionId != 0 &&
			DateTime.UtcNow - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
			await SendPortfolioSnapshot(cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		await ResetClients();
		_marketSubscriptions.Clear();
		_instruments.Clear();
		_instrumentsBySymbol.Clear();
		_orders.Clear();
		_lastTicks.Clear();
		_reportedTrades.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask ResetClients()
	{
		if (_stream != null)
		{
			_stream.FeedReceived -= ProcessFeed;
			_stream.Error -= SendOutErrorAsync;
			_stream.StateChanged -= SendOutConnectionStateAsync;
			await _stream.Disconnect();
			_stream.Dispose();
			_stream = null;
		}
		_rest?.Dispose();
		_rest = null;
	}

	private MiraeSharekhanRestClient GetRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void CacheInstrument(MiraeSharekhanInstrument instrument)
	{
		var scripCode = instrument?.GetScripCode() ?? 0;
		var exchange = instrument?.Exchange.ToNativeExchange();
		if (scripCode <= 0 || exchange.IsEmpty())
			return;
		instrument.Exchange = exchange;
		_instruments[exchange.ToStreamKey(scripCode)] = instrument;
		_instrumentsBySymbol[CreateSymbolKey(exchange, instrument.GetSymbol())] = instrument;
	}

	private async Task<MiraeSharekhanInstrument> ResolveInstrument(SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var exchange = securityId.BoardCode.ToNativeExchange().ThrowIfEmpty(nameof(securityId.BoardCode));
		var nativeText = securityId.Native?.ToString();
		if (long.TryParse(nativeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nativeCode) &&
			nativeCode > 0)
		{
			var streamKey = exchange.ToStreamKey(nativeCode);
			if (_instruments.TryGetValue(streamKey, out var nativeInstrument))
				return nativeInstrument;
			return new()
			{
				Exchange = exchange,
				ScripCode = nativeCode.ToString(CultureInfo.InvariantCulture),
				TradingSymbol = securityId.SecurityCode,
			};
		}

		var symbolKey = CreateSymbolKey(exchange, securityId.SecurityCode);
		if (_instrumentsBySymbol.TryGetValue(symbolKey, out var instrument))
			return instrument;
		foreach (var item in await GetRest().GetInstruments(exchange, cancellationToken))
			CacheInstrument(item);
		if (_instrumentsBySymbol.TryGetValue(symbolKey, out instrument))
			return instrument;
		throw new InvalidOperationException($"Mirae Asset Sharekhan instrument " +
			$"'{securityId.SecurityCode}@{exchange}' was not found in the scrip master.");
	}

	private static string CreateSymbolKey(string exchange, string symbol)
		=> $"{exchange.ToNativeExchange()}:{symbol?.ToUpperInvariant()}";

	private static SecurityId CreateSecurityId(string exchange, long scripCode, string symbol)
		=> new()
		{
			SecurityCode = symbol.IsEmpty(scripCode.ToString(CultureInfo.InvariantCulture)),
			BoardCode = exchange.ToNativeExchange(),
			Native = scripCode,
		};
}
