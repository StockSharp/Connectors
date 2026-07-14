namespace StockSharp.GateIO.Native.Spot.Model;

class Symbol
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("min_base_amount")]
	public double? MinBaseAmount { get; set; }

	[JsonProperty("min_quote_amount")]
	public double? MinQuoteAmount { get; set; }

	[JsonProperty("amount_precision")]
	public int AmountPrecision { get; set; }

	[JsonProperty("precision")]
	public int Precision { get; set; }

	[JsonProperty("trade_status")]
	public string TradeStatus { get; set; }

	[JsonProperty("sell_start")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? SellStart { get; set; }

	[JsonProperty("buy_start")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? BuyStart { get; set; }
}