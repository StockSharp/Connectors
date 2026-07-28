namespace StockSharp.AltCoinTrader.Native.Model;

sealed class AltCoinTraderBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("reserved")]
	public decimal Reserved { get; set; }

	[JsonProperty("total")]
	public decimal Total { get; set; }
}

sealed class AltCoinTraderOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("filled")]
	public decimal Filled { get; set; }

	[JsonProperty("remaining")]
	public decimal Remaining { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("created_at")]
	public long CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public long UpdatedAt { get; set; }

	[JsonProperty("cancelled_at")]
	public long? CancelledAt { get; set; }

	[JsonIgnore]
	public long? TransactionId
		=> AltCoinTraderExtensions.ParseClientOrderId(
			ClientOrderId);
}

sealed class AltCoinTraderUserTrade
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("fill_delta")]
	public decimal? FillDelta { get; set; }

	[JsonProperty("filled")]
	public decimal? Filled { get; set; }

	[JsonProperty("remaining")]
	public decimal? Remaining { get; set; }

	[JsonProperty("total_value")]
	public decimal? TotalValue { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public decimal ExecutionQuantity => FillDelta ?? Quantity;

	[JsonIgnore]
	public long? TransactionId
		=> AltCoinTraderExtensions.ParseClientOrderId(
			ClientOrderId);
}

sealed class AltCoinTraderLimitOrderRequest
{
	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }
}

sealed class AltCoinTraderMarketOrderRequest
{
	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }
}
