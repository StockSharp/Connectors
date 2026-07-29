namespace StockSharp.ByBitHistory;

partial class ByBitHistoryMessageAdapter
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ByBitHistoryMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public ByBitHistoryMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		base.DisposeManaged();

		_client.Dispose();
	}
}
