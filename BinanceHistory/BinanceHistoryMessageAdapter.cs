namespace StockSharp.BinanceHistory;

public partial class BinanceHistoryMessageAdapter
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BinanceHistoryMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BinanceHistoryMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();

		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	protected override ValueTask ResetAsync(ResetMessage msg, CancellationToken token)
	{
		_rangeCache.Clear();
		return base.ResetAsync(msg, token);
	}
}