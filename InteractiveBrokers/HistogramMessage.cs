namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Request histogram data message.
/// </summary>
public class HistogramMarketDataMessage : MarketDataMessage
{
	/// <summary>
	/// Initializes a new instance of the <see cref="HistogramMarketDataMessage"/>.
	/// </summary>
	public HistogramMarketDataMessage()
	{
		DataType2 = ExtendedDataTypes.Histogram;
	}

	/// <summary>
	/// Create a copy of <see cref="HistogramMarketDataMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var clone = new HistogramMarketDataMessage();
		CopyTo(clone);
		return clone;
	}
}

/// <summary>
/// Histogram data message.
/// </summary>
public class HistogramMessage : BaseSubscriptionIdMessage<HistogramMessage>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="HistogramMessage"/>.
	/// </summary>
	public HistogramMessage()
		: base(ExtendedMessageTypes.Histogram)
	{
	}

	/// <summary>
	/// Data.
	/// </summary>
	public IEnumerable<(decimal price, decimal size)> Data { get; set; }

	/// <inheritdoc />
	public override DataType DataType => ExtendedDataTypes.Histogram;

	/// <summary>
	/// Create a copy of <see cref="HistogramMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var copy = new HistogramMessage
		{
			Data = Data?.ToArray(),
		};

		CopyTo(copy);

		return copy;
	}
}