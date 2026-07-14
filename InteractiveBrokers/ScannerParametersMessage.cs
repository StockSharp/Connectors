namespace StockSharp.InteractiveBrokers;

/// <summary>
/// The message with scanner parameters.
/// </summary>
public class ScannerParametersMessage : BaseSubscriptionIdMessage<ScannerParametersMessage>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ScannerParametersMessage"/>.
	/// </summary>
	public ScannerParametersMessage()
		: base(ExtendedMessageTypes.ScannerParameters)
	{
	}

	/// <summary>
	/// The parameters in the xml format.
	/// </summary>
	public string Parameters { get; set; }

	/// <inheritdoc />
	public override DataType DataType => ExtendedDataTypes.Scanner;

	/// <summary>
	/// Create a copy of <see cref="ScannerParametersMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var copy = new ScannerParametersMessage
		{
			Parameters = Parameters,
		};

		CopyTo(copy);

		return copy;
	}
}