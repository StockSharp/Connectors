namespace StockSharp.QuantFeed;

public partial class QuantFeedMessageAdapter
{
	private QuantFeedCatalog _catalog;
	private TimeZoneInfo _defaultTimeZone;

	/// <summary>Initializes a new instance.</summary>
	public QuantFeedMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [QuantFeedExtensions.BoardCode];

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
			throw new InvalidOperationException("QuantHouse data directory is not specified.");

		_defaultTimeZone = QuantFeedExtensions.ResolveTimeZone(DefaultTimeZoneId);
		try
		{
			_catalog = await QuantFeedCatalog.LoadAsync(DataDirectory, IsRecursive,
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

	private QuantFeedCatalog SafeCatalog()
		=> _catalog ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void ClearState()
	{
		_catalog = null;
		_defaultTimeZone = null;
	}
}
