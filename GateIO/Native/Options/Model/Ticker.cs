namespace StockSharp.GateIO.Native.Options.Model;

class Ticker
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }

	[JsonProperty("mark_price")]
	public double? MarkPrice { get; set; }

	[JsonProperty("index_price")]
	public double? IndexPrice { get; set; }

	[JsonProperty("position_size")]
	public double? PositionSize { get; set; }

	[JsonProperty("bid1_price")]
	public double? Bid1Price { get; set; }

	[JsonProperty("bid1_size")]
	public double? Bid1Size { get; set; }

	[JsonProperty("ask1_price")]
	public double? Ask1Price { get; set; }

	[JsonProperty("ask1_size")]
	public double? Ask1Size { get; set; }

	[JsonProperty("vega")]
	public double? Vega { get; set; }

	[JsonProperty("theta")]
	public double? Theta { get; set; }

	[JsonProperty("rho")]
	public double? Rho { get; set; }

	[JsonProperty("gamma")]
	public double? Gamma { get; set; }

	[JsonProperty("delta")]
	public double? Delta { get; set; }

	[JsonProperty("mark_iv")]
	public double? MarkIv { get; set; }

	[JsonProperty("bid_iv")]
	public double? BidIv { get; set; }

	[JsonProperty("ask_iv")]
	public double? AskIv { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }
}