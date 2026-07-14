namespace StockSharp.InteractiveBrokers;

/// <summary>
/// The message to receive WSH meta data reports. The results will come through the <see cref="WshMetaDataMessage"/> message.
/// </summary>
public class WshMetaMarketDataMessage : MarketDataMessage
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WshMetaMarketDataMessage"/>.
	/// </summary>
	public WshMetaMarketDataMessage()
	{
		DataType2 = ExtendedDataTypes.WshMetaData;
	}

	/// <summary>
	/// Create a copy of <see cref="WshMetaMarketDataMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var clone = new WshMetaMarketDataMessage();
		CopyTo(clone);
		return clone;
	}
}

/// <summary>
/// The message to receive WSH event data reports. The results will come through the <see cref="WshEventDataMessage"/> message.
/// </summary>
public class WshEventMarketDataMessage : MarketDataMessage
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WshEventMarketDataMessage"/>.
	/// </summary>
	public WshEventMarketDataMessage()
	{
		DataType2 = ExtendedDataTypes.WshEventData;
	}

	/// <summary>
	/// Create a copy of <see cref="WshEventMarketDataMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var clone = new WshEventMarketDataMessage();
		CopyTo(clone);
		return clone;
	}
}