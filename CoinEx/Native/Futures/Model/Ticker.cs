namespace StockSharp.CoinEx.Native.Futures.Model;

class Ticker
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("last")]
	public double? Last { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("volume_sell")]
	public double? VolumeSell { get; set; }

	[JsonProperty("volume_buy")]
	public double? VolumeBuy { get; set; }

	[JsonProperty("value")]
	public double? Value { get; set; }

	[JsonProperty("period")]
	public int? Period { get; set; }
}
