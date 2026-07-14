namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Request option parameters message.
/// </summary>
public class OptionParametersMarketDataMessage : MarketDataMessage
{
	/// <summary>
	/// Initializes a new instance of the <see cref="OptionParametersMarketDataMessage"/>.
	/// </summary>
	public OptionParametersMarketDataMessage()
	{
		DataType2 = ExtendedDataTypes.OptionParameters;
	}

	/// <summary>
	/// Create a copy of <see cref="OptionParametersMarketDataMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var clone = new OptionParametersMarketDataMessage();
		CopyTo(clone);
		return clone;
	}
}

/// <summary>
/// The result message for the <see cref="OptionParametersMarketDataMessage"/>.
/// </summary>
public class OptionParametersMessage : SecurityMessage
{
	/// <summary>
	/// Initializes a new instance of the <see cref="OptionParametersMessage"/>.
	/// </summary>
	public OptionParametersMessage()
		: base(ExtendedMessageTypes.OptionParameters)
	{
	}

	/// <summary>
	/// Strikes.
	/// </summary>
	public IEnumerable<decimal> Strikes { get; set; }

	/// <summary>
	/// Expirations.
	/// </summary>
	public IEnumerable<DateTime> Expirations { get; set; }

	/// <inheritdoc />
	public override DataType DataType => ExtendedDataTypes.OptionParameters;

	/// <summary>
	/// Create a copy of <see cref="OptionParametersMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var copy = new OptionParametersMessage
		{
			Strikes = Strikes?.ToArray(),
			Expirations = Expirations?.ToArray(),
		};

		CopyTo(copy);

		return copy;
	}
}