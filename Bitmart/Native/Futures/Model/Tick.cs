namespace StockSharp.Bitmart.Native.Futures.Model;

class Tick
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("trade_id")]
	public long TradeId { get; set; }

	[JsonProperty("deal_price")]
	public double Price { get; set; }

	[JsonProperty("deal_vol")]
	public double Size { get; set; }

	[JsonProperty("created_at")]
	public DateTime Time { get; set; }
}