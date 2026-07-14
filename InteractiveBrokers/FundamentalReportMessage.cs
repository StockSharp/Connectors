namespace StockSharp.InteractiveBrokers;

/// <summary>
/// The message with the market report initiated by the message <see cref="FundamentalReportMarketDataMessage"/>.
/// </summary>
public class FundamentalReportMessage : BaseSubscriptionIdMessage<FundamentalReportMessage>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FundamentalReportMessage"/>.
	/// </summary>
	public FundamentalReportMessage()
		: base(ExtendedMessageTypes.FundamentalReport)
	{
	}

	/// <summary>
	/// Text of report.
	/// </summary>
	public string Data { get; set; }

	/// <inheritdoc />
	public override DataType DataType => ExtendedDataTypes.FundamentalReport;

	/// <summary>
	/// Create a copy of <see cref="FundamentalReportMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var copy = new FundamentalReportMessage
		{
			Data = Data,
		};

		CopyTo(copy);

		return copy;
	}
}