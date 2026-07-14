namespace StockSharp.Bitmex.Native.Model;

class Trade
{
	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("tickDirection")]
	public string TickDirection { get; set; }

	[JsonProperty("trdMatchID")]
	public string MatchId { get; set; }

	[JsonProperty("grossValue")]
	public double? GrossValue { get; set; }

	[JsonProperty("homeNotional")]
	public double? HomeNotional { get; set; }

	[JsonProperty("foreignNotional")]
	public double? ForeignNotional { get; set; }
}