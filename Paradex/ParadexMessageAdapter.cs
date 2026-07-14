namespace StockSharp.Paradex;

public partial class ParadexMessageAdapter
{
	private readonly Dictionary<ParadexSections, INativeAdapter> _nativeAdapters = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="ParadexMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public ParadexMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.ParadexSpot, BoardCodes.ParadexDerivatives];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId secId)
		=> secId.BoardCode.IsEmpty()
			|| secId.BoardCode.EqualsIgnoreCase(BoardCodes.ParadexSpot)
			|| secId.BoardCode.EqualsIgnoreCase(BoardCodes.ParadexDerivatives)
			|| secId.IsAssociated(BoardCodes.ParadexSpot)
			|| secId.IsAssociated(BoardCodes.ParadexDerivatives);

	private INativeAdapter EnsureGetAdapter(SecurityId secId)
	{
		if (secId == default || secId.BoardCode.IsEmpty())
			throw new InvalidOperationException("SecurityId.BoardCode must be specified to route request to a Paradex section.");

		var section =
			secId.BoardCode.EqualsIgnoreCase(BoardCodes.ParadexSpot) || secId.IsAssociated(BoardCodes.ParadexSpot)
				? ParadexSections.Spot
				: secId.BoardCode.EqualsIgnoreCase(BoardCodes.ParadexDerivatives) || secId.IsAssociated(BoardCodes.ParadexDerivatives)
					? ParadexSections.Derivatives
					: throw new InvalidOperationException($"Board '{secId.BoardCode}' is not supported by {nameof(ParadexMessageAdapter)}.");

		if (_nativeAdapters.TryGetValue(section, out var adapter))
			return adapter;

		throw new InvalidOperationException($"Section '{section}' is not enabled for board '{secId.BoardCode}'.");
	}

	private IReadOnlyDictionary<ParadexSections, INativeAdapter> NativeAdapters => _nativeAdapters;

	private INativeAdapter CreateNativeAdapter(ParadexSections section)
		=> section switch
		{
			ParadexSections.Derivatives => new Native.Derivatives.DerivativesAdapter(this),
			ParadexSections.Spot => new Native.Spot.SpotAdapter(this),
			_ => throw new InvalidOperationException($"Unknown section '{section}'."),
		};

	private void RecreateAdapters()
	{
		ClearAdapters();

		foreach (var section in Sections.Distinct())
			AddAdapter(section, CreateNativeAdapter(section));
	}

	private void AddAdapter(ParadexSections section, INativeAdapter adapter)
	{
		if (adapter is BaseLogReceiver logReceiver)
			logReceiver.Parent = this;

		adapter.NewOutMessage += SendOutMessageAsync;
		_nativeAdapters[section] = adapter;
	}

	private void ClearAdapters()
	{
		foreach (var adapter in _nativeAdapters.Values)
			adapter.NewOutMessage -= SendOutMessageAsync;

		_nativeAdapters.Clear();
	}
}
