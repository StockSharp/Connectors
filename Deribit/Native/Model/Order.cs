namespace StockSharp.Deribit.Native.Model;

class Order
{
	[JsonProperty("order_id")]
	public string Id { get; set; }

	[JsonProperty("instrument_name")]
	public string Instrument { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("amount")]
	public double Quantity { get; set; }

	[JsonProperty("filled_amount")]
	public double? FilledQuantity { get; set; }

	[JsonProperty("max_show")]
	public double? MaxShow { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("order_type")]
	public string Type { get; set; }

	[JsonProperty("order_state")]
	public string State { get; set; }

	[JsonProperty("average_price")]
	public double? AveragePrice { get; set; }

	[JsonProperty("label")]
	public string Label { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("creation_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Created { get; set; }

	[JsonProperty("last_update_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Modified { get; set; }

	[JsonProperty("post_only")]
	public bool? PostOnly { get; set; }

	[JsonProperty("api")]
	public bool? Api { get; set; }

	[JsonProperty("adv")]
	public string Advanced { get; set; }

	[JsonProperty("implv")]
	public double? ImpliedVolatility { get; set; }

	[JsonProperty("usd")]
	public double? Usd { get; set; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("reduce_only")]
	public bool? ReduceOnly { get; set; }

	[JsonProperty("is_liquidation")]
	public bool IsLiquidation { get; set; }

	[JsonProperty("trigger")]
	public string Trigger { get; set; }

	[JsonProperty("profit_loss")]
	public double? ProfitLoss { get; set; }
}