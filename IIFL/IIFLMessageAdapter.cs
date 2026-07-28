namespace StockSharp.IIFL;

public partial class IIFLMessageAdapter
{
	private sealed class CandleSubscription
	{
		public IIFLInstrumentRef Instrument { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastTime { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, IIFLInstrumentRef>
		_instrumentsByNative = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, IIFLInstrument>
		_instrumentDetails = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, IIFLInstrumentRef>
		_instrumentsBySymbol = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _loadedExchanges =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, IIFLInstrumentRef>
		_level1Subscriptions = [];
	private readonly Dictionary<long, IIFLInstrumentRef>
		_depthSubscriptions = [];
	private readonly Dictionary<long, IIFLInstrumentRef>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, int> _feedTopics =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, int> _openInterestTopics =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _orderSubscriptions = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _transactionOrders = [];
	private readonly Dictionary<string,
		(DateTimeOffset Time, decimal Price, decimal Volume)> _lastTicks =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _tradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private IIFLRestClient _restClient;
	private IIFLMqttClient _mqttClient;
	private string _resolvedPortfolio;
	private DateTime _lastPolling;
	private bool _privateStreamSubscribed;

	/// <summary>Supported historical candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> IIFLExtensions.TimeFrames;

	/// <summary>
	/// Initialize <see cref="IIFLMessageAdapter"/>.
	/// </summary>
	public IIFLMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

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
	[
		"NSE",
		"BSE",
		"NFO",
		"BFO",
		"CDS",
		"BCD",
		"MCX",
		"NCO",
	];

	private IIFLRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private async ValueTask<IIFLInstrument[]> LoadExchangeAsync(
		string exchange, CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_loadedExchanges.Contains(exchange))
				return [];
		}
		var values = await RestClient.GetInstrumentsAsync(exchange,
			cancellationToken);
		using (_sync.EnterScope())
		{
			foreach (var instrument in values)
			{
				if (instrument.Exchange.IsEmpty() ||
					instrument.InstrumentId.IsEmpty())
					continue;
				var key = NativeKey(instrument.Exchange,
					instrument.InstrumentId);
				_instrumentDetails[key] = instrument;
				CacheInstrument(instrument.ToReference());
			}
			_loadedExchanges.Add(exchange);
		}
		return values;
	}

	private void CacheInstrument(IIFLInstrumentRef instrument)
	{
		_instrumentsByNative[NativeKey(instrument.Exchange,
			instrument.InstrumentId)] = instrument;
		_instrumentsBySymbol[SymbolKey(instrument.BoardCode,
			instrument.Symbol)] = instrument;
	}

	private async ValueTask<IIFLInstrumentRef> ResolveInstrumentAsync(
		SecurityId securityId, CancellationToken cancellationToken)
	{
		if (securityId.Native.TryParseIIFLNative(
			out var nativeExchange, out var nativeId))
		{
			using (_sync.EnterScope())
			{
				if (_instrumentsByNative.TryGetValue(
					NativeKey(nativeExchange, nativeId),
					out var known))
					return known;
			}
			return new(nativeExchange, nativeId,
				securityId.SecurityCode.IsEmpty(nativeId),
				securityId.BoardCode.IsEmpty(
					nativeExchange.ToBoardCode()), 1);
		}

		var symbol = securityId.SecurityCode
			.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (!securityId.BoardCode.IsEmpty())
		{
			using (_sync.EnterScope())
			{
				if (_instrumentsBySymbol.TryGetValue(SymbolKey(
					securityId.BoardCode, symbol), out var known))
					return known;
			}
			var exchange = securityId.BoardCode.ToIIFLExchange();
			await LoadExchangeAsync(exchange, cancellationToken);
			using (_sync.EnterScope())
			{
				if (_instrumentsBySymbol.TryGetValue(SymbolKey(
					securityId.BoardCode, symbol), out var loaded))
					return loaded;
			}
		}
		else
		{
			using (_sync.EnterScope())
			{
				var known = _instrumentsBySymbol.Values
					.FirstOrDefault(value =>
						value.Symbol.EqualsIgnoreCase(symbol));
				if (!known.InstrumentId.IsEmpty())
					return known;
			}
			foreach (var exchange in IIFLExtensions.Exchanges)
			{
				await LoadExchangeAsync(exchange, cancellationToken);
				using (_sync.EnterScope())
				{
					var loaded = _instrumentsBySymbol.Values
						.FirstOrDefault(value =>
							value.Symbol.EqualsIgnoreCase(symbol));
					if (!loaded.InstrumentId.IsEmpty())
						return loaded;
				}
			}
		}
		throw new InvalidOperationException(
			$"IIFL instrument '{securityId}' was not found.");
	}

	private IIFLInstrumentRef ResolveStreamInstrument(string topic)
	{
		var separator = topic.IndexOf('/');
		if (separator <= 0 || separator == topic.Length - 1)
			throw new InvalidDataException(
				$"Invalid IIFL stream topic '{topic}'.");
		var exchange = topic[..separator].ToUpperInvariant();
		var instrumentId = topic[(separator + 1)..];
		using (_sync.EnterScope())
		{
			if (_instrumentsByNative.TryGetValue(
				NativeKey(exchange, instrumentId), out var known))
				return known;
		}
		return new(exchange, instrumentId, instrumentId,
			exchange.ToBoardCode(), 1);
	}

	private static string NativeKey(string exchange, string instrumentId)
		=> $"{exchange}:{instrumentId}";

	private static string SymbolKey(string board, string symbol)
		=> $"{board}:{symbol}";

	private static SecurityId ToSecurityId(
		IIFLInstrumentRef instrument)
		=> new()
		{
			SecurityCode = instrument.Symbol,
			BoardCode = instrument.BoardCode,
			Native = $"{instrument.Exchange}/{instrument.InstrumentId}",
		};

	private (long Id, IIFLInstrumentRef Instrument)[] FindTargets(
		Dictionary<long, IIFLInstrumentRef> subscriptions,
		IIFLInstrumentRef instrument)
	{
		using (_sync.EnterScope())
			return subscriptions
				.Where(pair =>
					pair.Value.Exchange.EqualsIgnoreCase(
						instrument.Exchange) &&
					pair.Value.InstrumentId.EqualsIgnoreCase(
						instrument.InstrumentId))
				.Select(static pair => (pair.Key, pair.Value))
				.ToArray();
	}

	private static bool IsDerivative(IIFLInstrumentRef instrument)
		=> instrument.Exchange.Contains("FO",
				StringComparison.OrdinalIgnoreCase) ||
			instrument.Exchange.Contains("CURR",
				StringComparison.OrdinalIgnoreCase) ||
			instrument.Exchange.Contains("COMM",
				StringComparison.OrdinalIgnoreCase);

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}
}
