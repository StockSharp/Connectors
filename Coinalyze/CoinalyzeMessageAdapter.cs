namespace StockSharp.Coinalyze;

public partial class CoinalyzeMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, CoinalyzeInstrument>
		_instruments =
			new(StringComparer.OrdinalIgnoreCase);
	private CoinalyzeRestClient _restClient;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="CoinalyzeMessageAdapter"/>.
	/// </summary>
	public CoinalyzeMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.Coinalyze];

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
			securityId.IsAssociated(BoardCodes.Coinalyze);

	private CoinalyzeRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void RememberInstruments(
		IEnumerable<CoinalyzeInstrument> instruments)
	{
		using (_sync.EnterScope())
			foreach (var instrument in instruments ?? [])
				if (instrument?.Symbol.IsEmpty() == false)
					_instruments[instrument.Symbol] = instrument;
	}

	private CoinalyzeInstrument GetInstrument(
		SecurityId securityId)
	{
		var key = securityId.Native as string;
		if (key.IsEmpty())
			key = securityId.SecurityCode;
		using (_sync.EnterScope())
			if (!key.IsEmpty() &&
				_instruments.TryGetValue(key, out var instrument))
				return instrument;
		throw new InvalidOperationException(
			$"Unknown Coinalyze security " +
				$"'{securityId.SecurityCode}'. Run security lookup first.");
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
			_instruments.Clear();
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_restClient?.Dispose();
		_restClient = null;
		base.DisposeManaged();
	}
}
