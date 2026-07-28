namespace StockSharp.CoinPaprika;

public partial class CoinPaprikaMessageAdapter
{
	private sealed class Level1Subscription
	{
		public CoinPaprikaInstrument Instrument { get; init; }
		public DateTime LastUpdate { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, CoinPaprikaInstrument>
		_instrumentsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CoinPaprikaInstrument>
		_instrumentsByCode =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private CoinPaprikaRestClient _restClient;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="CoinPaprikaMessageAdapter"/>.
	/// </summary>
	public CoinPaprikaMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.CoinPaprika];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.IsAssociated(BoardCodes.CoinPaprika);

	private CoinPaprikaRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private CoinPaprikaInstrument GetInstrument(SecurityId securityId)
	{
		using (_sync.EnterScope())
		{
			if (securityId.Native is string native &&
				!native.IsEmpty() &&
				_instrumentsByNative.TryGetValue(
					native, out var instrument))
				return instrument;
			if (!securityId.SecurityCode.IsEmpty() &&
				_instrumentsByCode.TryGetValue(
					securityId.SecurityCode, out instrument))
				return instrument;
		}
		throw new InvalidOperationException(
			$"Unknown CoinPaprika security " +
				$"'{securityId.SecurityCode}'. Run security lookup " +
				"or provide its native CoinPaprika id.");
	}

	private void RememberInstruments(
		IEnumerable<CoinPaprikaInstrument> instruments)
	{
		using (_sync.EnterScope())
		{
			foreach (var instrument in instruments ?? [])
			{
				if (instrument?.NativeId.IsEmpty() != false ||
					instrument.Symbol.IsEmpty())
					continue;
				_instrumentsByNative[instrument.NativeId] =
					instrument;
				_instrumentsByCode.TryAdd(
					instrument.Symbol, instrument);
			}
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instrumentsByNative.Clear();
			_instrumentsByCode.Clear();
			_level1Subscriptions.Clear();
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_restClient?.Dispose();
		_restClient = null;
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
