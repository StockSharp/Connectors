namespace StockSharp.Coincheck.Native.Model;

class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("order_type")]
	public string Type { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("rate")]
	public double? Price { get; set; }

	[JsonProperty("stop_loss_rate")]
	public double? StopLossRate { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("pending_amount")]
	public double? PendingAmount { get; set; }

	[JsonProperty("pending_market_buy_amount")]
	public double? PendingMarketBuyAmount { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }
}