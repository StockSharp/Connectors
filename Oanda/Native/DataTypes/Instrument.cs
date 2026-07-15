namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Instrument
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("pipLocation")]
	public int PipLocation { get; set; }

	[JsonProperty("displayPrecision")]
	public int DisplayPrecision { get; set; }

	[JsonProperty("tradeUnitsPrecision")]
	public int TradeUnitsPrecision { get; set; }

	[JsonProperty("minimumTradeSize")]
	public double MinimumTradeSize { get; set; }

	[JsonProperty("maximumTrailingStopDistance")]
	public double MaximumTrailingStopDistance { get; set; }

	[JsonProperty("minimumTrailingStopDistance")]
	public double MinimumTrailingStopDistance { get; set; }

	[JsonProperty("maximumPositionSize")]
	public double MaximumPositionSize { get; set; }

	[JsonProperty("maximumOrderUnits")]
	public double MaximumOrderUnits { get; set; }

	[JsonProperty("marginRate")]
	public double MarginRate { get; set; }
}