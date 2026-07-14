namespace StockSharp.Aster;

public partial class AsterMessageAdapter
{
	private readonly CachedSynchronizedDictionary<string, INativeAdapter> _adapters = new(StringComparer.InvariantCultureIgnoreCase);

	/// <summary>
	/// Initializes a new instance of the <see cref="AsterMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public AsterMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.AsterSpot, BoardCodes.AsterDerivatives];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId secId)
		=> secId.BoardCode.IsEmpty()
			|| secId.BoardCode.EqualsIgnoreCase(BoardCodes.AsterSpot)
			|| secId.BoardCode.EqualsIgnoreCase(BoardCodes.AsterDerivatives)
			|| secId.IsAssociated(BoardCodes.AsterSpot)
			|| secId.IsAssociated(BoardCodes.AsterDerivatives);

	private INativeAdapter EnsureGetAdapter(SecurityId secId)
	{
		if (secId == default || secId.BoardCode.IsEmpty())
			throw new InvalidOperationException("SecurityId.BoardCode must be specified to route request to an Aster section.");

		if (_adapters.TryGetValue(secId.BoardCode, out var adapter))
			return adapter;

		throw new InvalidOperationException($"Board '{secId.BoardCode}' is not enabled for {nameof(AsterMessageAdapter)}.");
	}

	private void RecreateAdapters()
	{
		_adapters.Clear();

		foreach (var section in Sections.Distinct())
		{
			switch (section)
			{
				case AsterSections.Spot:
					AddAdapter(new Native.Spot.SpotAdapter(Key, Secret, SpotRestEndpoint, SpotWsEndpoint, ReConnectionSettings.WorkingTime));
					break;

				case AsterSections.Derivatives:
					AddAdapter(new Native.Derivatives.DerivativesAdapter(Key, Secret, DerivativesRestEndpoint, DerivativesWsEndpoint, ReConnectionSettings.WorkingTime));
					break;

				default:
					throw new InvalidOperationException($"Unknown section '{section}'.");
			}
		}
	}

	private void AddAdapter(INativeAdapter adapter)
	{
		if (adapter is BaseLogReceiver logReceiver)
			logReceiver.Parent = this;

		adapter.NewOutMessage += SendOutMessageAsync;
		_adapters.Add(adapter.BoardCode, adapter);
	}

	private void EnsureConnected()
	{
		if (_adapters.Count == 0)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}
}
