namespace StockSharp.EdgeX;

public partial class EdgeXMessageAdapter
{
	private readonly CachedSynchronizedDictionary<string, INativeAdapter> _adapters = new(StringComparer.InvariantCultureIgnoreCase);

	/// <summary>
	/// Initializes a new instance of the <see cref="EdgeXMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public EdgeXMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.EdgeXSpot, BoardCodes.EdgeXDerivatives];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId secId)
		=> secId.BoardCode.IsEmpty()
			|| secId.BoardCode.EqualsIgnoreCase(BoardCodes.EdgeXSpot)
			|| secId.BoardCode.EqualsIgnoreCase(BoardCodes.EdgeXDerivatives)
			|| secId.IsAssociated(BoardCodes.EdgeXSpot)
			|| secId.IsAssociated(BoardCodes.EdgeXDerivatives);

	private INativeAdapter EnsureGetAdapter(SecurityId secId)
	{
		if (secId == default || secId.BoardCode.IsEmpty())
			throw new InvalidOperationException("SecurityId.BoardCode must be specified to route request to an edgeX section.");

		if (_adapters.TryGetValue(secId.BoardCode, out var adapter))
			return adapter;

		throw new InvalidOperationException($"Board '{secId.BoardCode}' is not enabled for {nameof(EdgeXMessageAdapter)}.");
	}

	private void RecreateAdapters()
	{
		_adapters.Clear();

		foreach (var section in Sections.Distinct())
		{
			switch (section)
			{
				case EdgeXSections.Spot:
					if (!EnableSpotSection)
						throw new NotSupportedException("edgeX spot section is disabled in adapter settings.");

					AddAdapter(new Native.Spot.SpotAdapter(Key, Secret, ClearingAccount, Passphrase, SpotRestEndpoint, SpotWsEndpoint, ReConnectionSettings.WorkingTime));
					break;

				case EdgeXSections.Derivatives:
					AddAdapter(new Native.Derivatives.DerivativesAdapter(Key, Secret, ClearingAccount, Passphrase, DerivativesRestEndpoint, DerivativesWsEndpoint, DerivativesPrivateWsEndpoint, ReConnectionSettings.WorkingTime));
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
