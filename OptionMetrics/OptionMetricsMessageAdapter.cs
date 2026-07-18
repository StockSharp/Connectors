namespace StockSharp.OptionMetrics;

public partial class OptionMetricsMessageAdapter
{
	private IvyDbCatalog _catalog;
	private IvyDbSecurityMaster _securityMaster;
	private TimeZoneInfo _marketTimeZone;

	/// <summary>Initializes a new instance.</summary>
	public OptionMetricsMessageAdapter(IdGenerator transactionIdGenerator)
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
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_catalog != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (DataDirectory.IsEmpty())
			throw new InvalidOperationException("IvyDB data directory is not specified.");
		if (!Enum.IsDefined(PriceAdjustment))
			throw new InvalidOperationException("IvyDB price adjustment is invalid.");
		if (SessionStart < TimeSpan.Zero || SessionEnd <= SessionStart ||
			SessionEnd >= TimeSpan.FromDays(1) || OptionSnapshotTime < TimeSpan.Zero ||
			OptionSnapshotTime >= TimeSpan.FromDays(1))
		{
			throw new InvalidOperationException(
				"IvyDB session and snapshot times must be within one day, and the session must increase.");
		}

		_marketTimeZone = Extensions.ResolveMarketTimeZone(MarketTimeZoneId);
		try
		{
			var catalog = await IvyDbCatalog.LoadAsync(DataDirectory, cancellationToken);
			var securityMaster = await catalog.LoadSecurityMasterAsync(cancellationToken);
			_catalog = catalog;
			_securityMaster = securityMaster;
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

	private IvyDbCatalog SafeCatalog()
		=> _catalog ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private IvyDbSecurityMaster SafeSecurityMaster()
		=> _securityMaster ??
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void ClearState()
	{
		_catalog = null;
		_securityMaster = null;
		_marketTimeZone = null;
	}
}
