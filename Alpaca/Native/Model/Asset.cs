namespace StockSharp.Alpaca.Native.Model;

class Asset
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("class")]
	public string Class { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("tradable")]
	public bool? Tradable { get; set; }

	[JsonProperty("marginable")]
	public bool? Marginable { get; set; }

	[JsonProperty("maintenance_margin_requirement")]
	public int MaintenanceMarginRequirement { get; set; }

	[JsonProperty("shortable")]
	public bool? Shortable { get; set; }

	[JsonProperty("easy_to_borrow")]
	public bool? EasyToBorrow { get; set; }

	[JsonProperty("fractionable")]
	public bool? Fractionable { get; set; }

	[JsonProperty("attributes")]
	public object[] Attributes { get; set; }

	[JsonProperty("min_order_size")]
	public double? MinOrderSize { get; set; }

	[JsonProperty("min_trade_increment")]
	public double? MinTradeIncrement { get; set; }

	[JsonProperty("price_increment")]
	public double? PriceIncrement { get; set; }
}
