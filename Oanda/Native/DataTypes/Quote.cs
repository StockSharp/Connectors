namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Quote
{
	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("liquidity")]
	public double Liquidity { get; set; }
}