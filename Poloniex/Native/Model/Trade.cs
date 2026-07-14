namespace StockSharp.Poloniex.Native.Model;

class Trade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("date")]
	public double Date { get; set; }

	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("rate")]
	public double Rate { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("total")]
	public double Total { get; set; }
}

class HttpTrade
{
	[JsonProperty("globalTradeID")]
	public long GlobalId { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("rate")]
	public double Rate { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("total")]
	public double Total { get; set; }
}