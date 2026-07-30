namespace StockSharp.MStock;

public partial class MStockMessageAdapter
{
	private sealed class CandleSubscription
	{
		public MStockInstrumentRef Instrument { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastTime { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, MStockInstrument>
		_instrumentDetails =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MStockInstrumentRef>
		_instrumentsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MStockInstrumentRef>
		_instrumentsBySymbol =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MStockInstrumentRef>
		_level1Subscriptions = [];
	private readonly Dictionary<long, MStockInstrumentRef>
		_depthSubscriptions = [];
	private readonly Dictionary<long, MStockInstrumentRef>
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
	private MStockRestClient _restClient;
	private MStockSocketClient _socketClient;
	private string _portfolioName;
	private DateTime _lastPolling;
	private bool _instrumentsLoaded;

	/// <summary>Supported historical candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		MStockExtensions.TimeFrames;

	/// <summary>
	/// Initialize <see cref="MStockMessageAdapter"/>.
	/// </summary>
	public MStockMessageAdapter(IdGenerator transactionIdGenerator)
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
		["NSE", "BSE", "NFO", "BFO", "CDS"];

	private MStockRestClient RestClient =>
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
				if (instrument.Exchange.IsEmpty() ||
					instrument.Token.IsEmpty())
					continue;
				var value = instrument.ToReference();
				_instrumentDetails[value.Key] = instrument;
				_instrumentsByNative[value.Key] = value;
				CacheSymbol(value.Exchange, value.TradingSymbol,
					value);
				CacheSymbol(value.Exchange, value.Symbol, value);
			}

			_instrumentsLoaded = true;
		}
	}

	private void CacheSymbol(string board, string symbol,
		MStockInstrumentRef instrument)
	{
		if (!symbol.IsEmpty())
			_instrumentsBySymbol[
				SymbolKey(board, symbol)] = instrument;
	}

	private async ValueTask<MStockInstrumentRef>
		ResolveInstrumentAsync(SecurityId securityId,
			CancellationToken cancellationToken)
	{
		if (securityId.Native.TryParseMStockNative(
			out var exchange, out var token))
		{
			using (_sync.EnterScope())
				if (_instrumentsByNative.TryGetValue(
					NativeKey(exchange, token), out var known))
					return known;
			return new(exchange, token,
				securityId.SecurityCode.IsEmpty(token),
				securityId.SecurityCode.IsEmpty(token), 1);
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
			var values = _instrumentsBySymbol.Values
				.Where(value =>
					value.TradingSymbol.EqualsIgnoreCase(symbol) ||
					value.Symbol.EqualsIgnoreCase(symbol))
				.DistinctBy(static value => value.Key)
				.ToArray();
			if (values.Length == 1)
				return values[0];
		}
		throw new InvalidOperationException(
			$"m.Stock instrument '{securityId}' was not found.");
	}

	private MStockInstrumentRef ResolveFeedInstrument(
		MStockFeed feed)
	{
		using (_sync.EnterScope())
			if (_instrumentsByNative.TryGetValue(
				NativeKey(feed.Exchange, feed.Token), out var known))
				return known;
		return new(feed.Exchange, feed.Token, feed.Token,
			feed.Token, 1);
	}

	private (long Id, MStockInstrumentRef Instrument)[] FindTargets(
		Dictionary<long, MStockInstrumentRef> subscriptions,
		MStockInstrumentRef instrument)
	{
		using (_sync.EnterScope())
			return subscriptions
				.Where(pair => pair.Value.Key.EqualsIgnoreCase(
					instrument.Key))
				.Select(static pair => (pair.Key, pair.Value))
				.ToArray();
	}

	private static string NativeKey(string exchange, string token)
		=> $"{exchange}:{token}";

	private static string SymbolKey(string exchange, string symbol)
		=> $"{exchange}:{symbol}";

	private static SecurityId ToSecurityId(
		MStockInstrumentRef instrument)
		=> new()
		{
			SecurityCode = instrument.TradingSymbol,
			BoardCode = instrument.Exchange,
			Native = $"{instrument.Exchange}/{instrument.Token}",
		};

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}
}
