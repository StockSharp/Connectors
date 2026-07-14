namespace StockSharp.Yobit.Native.Model;

class Ticker
{
	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("avg")]
	public decimal? Avg { get; set; }

	[JsonProperty("vol")]
	public decimal? Volume { get; set; }

	[JsonProperty("vol_cur")]
	public decimal? VolumeInCurrency { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("buy")]
	public decimal? Buy { get; set; }

	[JsonProperty("sell")]
	public decimal? Sell { get; set; }

	[JsonProperty("updated")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Updated { get; set; }
}