namespace StockSharp.InteractiveBrokers;

/// <summary>
/// The message about start of the instruments scanner based on specified parameters. The results will come through the <see cref="ScannerResultMessage"/> message.
/// </summary>
public class ScannerMarketDataMessage : MarketDataMessage
{
	/// <summary>
	/// Is parameters request.
	/// </summary>
	public bool IsParametersRequest { get; set; }

	/// <summary>
	/// Filter.
	/// </summary>
	public ScannerFilter Filter { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="ScannerMarketDataMessage"/>.
	/// </summary>
	public ScannerMarketDataMessage()
	{
		DataType2 = ExtendedDataTypes.Scanner;
	}

	/// <summary>
	/// Create a copy of <see cref="ScannerMarketDataMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var clone = new ScannerMarketDataMessage
		{
			IsParametersRequest = IsParametersRequest,
			Filter = Filter,
		};
		CopyTo(clone);
		return clone;
	}
}