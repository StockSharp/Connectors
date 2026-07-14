namespace StockSharp.InteractiveBrokers;

/// <summary>
/// The message with the results of scanner starting by the message <see cref="ScannerMarketDataMessage"/>.
/// </summary>
public class ScannerResultMessage : BaseSubscriptionIdMessage<ScannerResultMessage>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ScannerResultMessage"/>.
	/// </summary>
	public ScannerResultMessage()
		: base(ExtendedMessageTypes.Scanner)
	{
	}

	/// <summary>
	/// The results.
	/// </summary>
	public IEnumerable<ScannerResult> Results { get; set; }

	/// <inheritdoc />
	public override DataType DataType => ExtendedDataTypes.Scanner;

	/// <summary>
	/// Create a copy of <see cref="ScannerResultMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var copy = new ScannerResultMessage
		{
			Results = Results?.Select(r => r.Clone()).ToArray(),
		};

		CopyTo(copy);

		return copy;
	}
}