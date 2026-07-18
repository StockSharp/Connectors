namespace StockSharp.AlgoSeek;

public partial class AlgoSeekMessageAdapter
{
	private AlgoSeekCatalog _catalog;
	private TimeZoneInfo _marketTimeZone;

	/// <summary>Initializes a new instance.</summary>
	public AlgoSeekMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(Extensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
	[
		Extensions.StockBoard,
		Extensions.OptionBoard,
		Extensions.FuturesBoard,
		Extensions.FutureOptionsBoard,
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_catalog != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (DataDirectory.IsEmpty())
			throw new InvalidOperationException("AlgoSeek data directory is not specified.");

		_marketTimeZone = Extensions.ResolveMarketTimeZone(MarketTimeZoneId);
		try
		{
			_catalog = await AlgoSeekCatalog.LoadAsync(DataDirectory, IsRecursive,
				cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			ClearState();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_catalog == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		ClearState();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private AlgoSeekCatalog SafeCatalog()
		=> _catalog ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void ClearState()
	{
		_catalog = null;
		_marketTimeZone = null;
	}
}
