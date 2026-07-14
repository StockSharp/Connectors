namespace StockSharp.OkexHistory;

partial class OkexHistoryMessageAdapter : HistoricalMessageAdapter
{
	/// <summary>
	/// Initializes a new instance of the <see cref="OkexHistoryMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public OkexHistoryMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	protected override ValueTask ResetAsync(ResetMessage msg, CancellationToken token)
	{
		_datesCache.Clear();
		return base.ResetAsync(msg, token);
	}
}