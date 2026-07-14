namespace StockSharp.Alpaca.Native.Model;

class Quote : BaseEntity
{
	[JsonProperty("bp")]
	public double? BidPrice { get; set; }

	[JsonProperty("ap")]
	public double? AskPrice { get; set; }

	[JsonProperty("bs")]
	public double? BidSize { get; set; }

	[JsonProperty("as")]
	public double? AskSize { get; set; }

	[JsonProperty("bx")]
	public string BidExchange { get; set; }

	[JsonProperty("ax")]
	public string AskExchange { get; set; }

	[JsonProperty("s")]
	public double? TradeSize { get; set; }
}