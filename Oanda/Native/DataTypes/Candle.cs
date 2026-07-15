namespace StockSharp.Oanda.Native.DataTypes;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class CandlePrice
{
	[JsonProperty("o")]
	public double Open { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("c")]
	public double Close { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Candle
{
	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("bid")]
	public CandlePrice Bid { get; set; }

	[JsonProperty("ask")]
	public CandlePrice Ask { get; set; }

	[JsonProperty("mid")]
	public CandlePrice Mid { get; set; }

	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("complete")]
	public bool Complete { get; set; }
}