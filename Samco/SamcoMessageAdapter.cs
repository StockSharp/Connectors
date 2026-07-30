namespace StockSharp.Samco;

public partial class SamcoMessageAdapter
{
	private sealed class CandleSubscription
	{
		public SamcoInstrumentRef Instrument { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastTime { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, SamcoInstrument>
		_instrumentDetails =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, SamcoInstrumentRef>
		_instrumentsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, SamcoInstrumentRef>
		_instrumentsBySymbol =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, SamcoInstrumentRef>
		_level1Subscriptions = [];
	private readonly Dictionary<long, SamcoInstrumentRef>
		_depthSubscriptions = [];
	private readonly Dictionary<long, SamcoInstrumentRef>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, int> _feedSubscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _orderSubscriptions = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _transactionOrders = [];
	private readonly HashSet<string> _tradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string,
		(DateTimeOffset Time, decimal Price, decimal Volume)> _lastTicks =
			new(StringComparer.OrdinalIgnoreCase);
	private SamcoRestClient _restClient;
	private SamcoSocketClient _socketClient;
	private string _portfolioName;
	private DateTime _lastPolling;
	private bool _instrumentsLoaded;

	/// <summary>Supported historical candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		SamcoExtensions.TimeFrames;

	/// <summary>
	/// Initialize <see cref="SamcoMessageAdapter"/>.
	/// </summary>
	public SamcoMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval =
			TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } =
		[5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		["NSE", "BSE", "NFO", "BFO", "CDS", "MCX", "MFO"];

	private SamcoRestClient RestClient =>
		_restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private async ValueTask EnsureInstrumentsAsync(
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			if (_instrumentsLoaded)
				return;
		var instruments = await RestClient.GetInstrumentsAsync(
			cancellationToken);
		using (_sync.EnterScope())
		{
			foreach (var instrument in instruments)
			{
				var reference = instrument.ToReference();
				if (reference.Exchange.IsEmpty() ||
					reference.SymbolCode.IsEmpty())
					continue;
				_instrumentDetails[reference.Key] = instrument;
				_instrumentsByNative[reference.SymbolCode] = reference;
				CacheSymbol(reference.Exchange,
					reference.TradingSymbol, reference);
				CacheSymbol(reference.Exchange,
					reference.Name, reference);
			}

			_instrumentsLoaded = true;
		}
	}

	private void CacheSymbol(string board, string symbol,
		SamcoInstrumentRef instrument)
	{
		if (!symbol.IsEmpty())
			_instrumentsBySymbol[
				SymbolKey(board, symbol)] = instrument;
	}

	private async ValueTask<SamcoInstrumentRef>
		ResolveInstrumentAsync(SecurityId securityId,
			CancellationToken cancellationToken)
	{
		var native = securityId.Native?.ToString();
		if (!native.IsEmpty())
		{
			using (_sync.EnterScope())
				if (_instrumentsByNative.TryGetValue(native,
					out var known))
					return known;
		}
		await EnsureInstrumentsAsync(cancellationToken);
		var symbol = securityId.SecurityCode
			.ThrowIfEmpty(nameof(securityId.SecurityCode));
		using (_sync.EnterScope())
		{
			if (!securityId.BoardCode.IsEmpty() &&
				_instrumentsBySymbol.TryGetValue(SymbolKey(
					securityId.BoardCode, symbol), out var known))
				return known;
			var values = _instrumentsByNative.Values
				.Where(value =>
					value.TradingSymbol.EqualsIgnoreCase(symbol) ||
					value.Name.EqualsIgnoreCase(symbol) ||
					value.SymbolCode.EqualsIgnoreCase(symbol))
				.DistinctBy(static value => value.Key)
				.ToArray();
			if (values.Length == 1)
				return values[0];
		}
		throw new InvalidOperationException(
			$"Samco instrument '{securityId}' was not found.");
	}

	private SamcoInstrumentRef ResolveFeedInstrument(string symbolCode)
	{
		using (_sync.EnterScope())
			if (_instrumentsByNative.TryGetValue(symbolCode,
				out var known))
				return known;
		var separator = symbolCode.LastIndexOf('_');
		var exchange = separator > 0
			? symbolCode[(separator + 1)..].ToUpperInvariant()
			: "NSE";
		return new(exchange, symbolCode, symbolCode, symbolCode, 1,
			null);
	}

	private (long Id, SamcoInstrumentRef Instrument)[] FindTargets(
		Dictionary<long, SamcoInstrumentRef> subscriptions,
		SamcoInstrumentRef instrument)
	{
		using (_sync.EnterScope())
			return subscriptions
				.Where(pair => pair.Value.Key.EqualsIgnoreCase(
					instrument.Key))
				.Select(static pair => (pair.Key, pair.Value))
				.ToArray();
	}

	private static string SymbolKey(string exchange, string symbol)
		=> $"{exchange}:{symbol}";

	private static SecurityId ToSecurityId(
		SamcoInstrumentRef instrument)
		=> instrument.ToSecurityId();

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}
}
