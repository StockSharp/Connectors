namespace StockSharp.InteractiveBrokers;

/// <summary>
/// The message about subscription to the estimated option values getting.
/// </summary>
public class OptionCalcMarketDataMessage : MarketDataMessage
{
	/// <summary>
	/// Initializes a new instance of the <see cref="OptionCalcMarketDataMessage"/>.
	/// </summary>
	public OptionCalcMarketDataMessage()
	{
		DataType2 = ExtendedDataTypes.OptionCalc;
	}

	/// <summary>
	/// The implied volatility.
	/// </summary>
	public decimal ImpliedVolatility { get; set; }

	/// <summary>
	/// The option price.
	/// </summary>
	public decimal OptionPrice { get; set; }

	/// <summary>
	/// Underlying asset price.
	/// </summary>
	public decimal AssetPrice { get; set; }

	/// <summary>
	/// Create a copy of <see cref="OptionCalcMarketDataMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var clone = new OptionCalcMarketDataMessage
		{
			ImpliedVolatility = ImpliedVolatility,
			OptionPrice = OptionPrice,
			AssetPrice = AssetPrice,
		};
		CopyTo(clone);
		return clone;
	}
}