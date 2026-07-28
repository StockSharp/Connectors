namespace StockSharp.CoinGlass;

public partial class CoinGlassMessageAdapter
{
	private sealed class Level1Subscription
	{
		public CoinGlassInstrument Instrument { get; init; }
		public DateTime LastUpdate { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, CoinGlassInstrument>
		_instrumentsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CoinGlassInstrument>
		_instrumentsByCode =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private CoinGlassRestClient _restClient;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="CoinGlassMessageAdapter"/>.
	/// </summary>
	public CoinGlassMessageAdapter(
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
		[BoardCodes.CoinGlass];

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
			securityId.IsAssociated(BoardCodes.CoinGlass);

	private CoinGlassRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private CoinGlassInstrument GetInstrument(
		SecurityId securityId)
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
			$"Unknown CoinGlass security " +
				$"'{securityId.SecurityCode}'. Run security lookup first.");
	}

	private void RememberInstruments(
		IEnumerable<CoinGlassInstrument> instruments)
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
				_instrumentsByCode[instrument.Symbol] =
					instrument;
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
