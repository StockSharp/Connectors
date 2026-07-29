namespace StockSharp.KucoinHistory;

partial class KucoinHistoryMessageAdapter
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Extensions.TimeFrames.Keys.ToArray();

	/// <summary>
	/// Initializes a new instance of the <see cref="KucoinHistoryMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public KucoinHistoryMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_client.Dispose();

		base.DisposeManaged();
	}
}
