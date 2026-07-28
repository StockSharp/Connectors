namespace StockSharp.MarketDataApp;

public partial class MarketDataAppMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, MarketDataAppInstrument>
		_instrumentsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MarketDataAppInstrument>
		_instrumentsBySymbol =
			new(StringComparer.OrdinalIgnoreCase);
	private MarketDataAppRestClient _restClient;

	/// <summary>Initialize the adapter.</summary>
	public MarketDataAppMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.MarketDataApp];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> false;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.IsAssociated(BoardCodes.MarketDataApp);

	private MarketDataAppRestClient RestClient =>
		_restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void Remember(
		IEnumerable<MarketDataAppInstrument> instruments)
	{
		using (_sync.EnterScope())
		{
			foreach (var instrument in instruments ?? [])
			{
				if (instrument?.Symbol.IsEmpty() != false)
					continue;
				_instrumentsByNative[instrument.NativeId] =
					instrument;
				_instrumentsBySymbol[instrument.Symbol] = instrument;
			}
		}
	}

	private MarketDataAppInstrument ResolveInstrument(
		SecurityId securityId)
	{
		var native = securityId.Native?.ToString();
		using (_sync.EnterScope())
		{
			if (!native.IsEmpty() &&
				_instrumentsByNative.TryGetValue(native,
					out var known))
				return known;
			if (!securityId.SecurityCode.IsEmpty() &&
				_instrumentsBySymbol.TryGetValue(
					securityId.SecurityCode, out known))
				return known;
		}
		var symbol = native.WithoutAssetPrefix()
			.IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var kind = native.ToAssetKind();
		if (native.IsEmpty() && symbol.IsOptionSymbol())
			kind = MarketDataAppAssetKinds.Option;
		return new()
		{
			Symbol = symbol,
			Kind = kind,
			SecurityType = kind switch
			{
				MarketDataAppAssetKinds.Option =>
					SecurityTypes.Option,
				MarketDataAppAssetKinds.Index =>
					SecurityTypes.Index,
				MarketDataAppAssetKinds.Fund =>
					SecurityTypes.Fund,
				_ => SecurityTypes.Stock,
			},
		};
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instrumentsByNative.Clear();
			_instrumentsBySymbol.Clear();
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_restClient?.Dispose();
		_restClient = null;
		base.DisposeManaged();
	}
}
