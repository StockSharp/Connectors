namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("type")]
	public TradierOrderTypes? Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("option_symbol")]
	public string OptionSymbol { get; set; }

	[JsonProperty("side")]
	public TradierOrderSides? Side { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("status")]
	public TradierOrderStatuses? Status { get; set; }

	[JsonProperty("duration")]
	public TradierOrderDurations? Duration { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("stop_price")]
	public double? StopPrice { get; set; }

	[JsonProperty("avg_fill_price")]
	public double? AvgFillPrice { get; set; }

	[JsonProperty("exec_quantity")]
	public double? ExecQuantity { get; set; }

	[JsonProperty("last_fill_price")]
	public double? LastFillPrice { get; set; }

	[JsonProperty("last_fill_quantity")]
	public double? LastFillQuantity { get; set; }

	[JsonProperty("remaining_quantity")]
	public double RemainingQuantity { get; set; }

	[JsonProperty("create_date")]
	public DateTime CreateDate { get; set; }

	[JsonProperty("transaction_date")]
	public DateTime? TransactionDate { get; set; }

	[JsonProperty("class")]
	public string Class { get; set; }

	[JsonProperty("reason_description")]
	public string ReasonDescription { get; set; }

	[JsonProperty("leg")]
	public Order[] Legs { get; set; }

	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }
}
