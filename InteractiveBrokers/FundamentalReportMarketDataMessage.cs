namespace StockSharp.InteractiveBrokers;

/// <summary>
/// The message to receive market reports for the specified instrument. The results will come through the <see cref="FundamentalReportMessage"/> message.
/// </summary>
public class FundamentalReportMarketDataMessage : MarketDataMessage
{
	/// <summary>
	/// The report type.
	/// </summary>
	public FundamentalReports Report { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="FundamentalReportMarketDataMessage"/>.
	/// </summary>
	public FundamentalReportMarketDataMessage()
	{
		DataType2 = ExtendedDataTypes.FundamentalReport;
	}

	/// <summary>
	/// Create a copy of <see cref="FundamentalReportMarketDataMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var clone = new FundamentalReportMarketDataMessage { Report = Report };
		CopyTo(clone);
		return clone;
	}
}