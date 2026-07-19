namespace StockSharp.Bitkub.Native.Model;

sealed class BitkubPlaceOrderRequest
{
	[JsonProperty("sym")]
	public string Symbol { get; init; }

	[JsonProperty("amt")]
	public decimal Amount { get; init; }

	[JsonProperty("rat")]
	public decimal Rate { get; init; }

	[JsonProperty("typ")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubOrderTypes Type { get; init; }

	[JsonProperty("client_id")]
	public string ClientId { get; init; }

	[JsonProperty("post_only")]
	public bool? IsPostOnly { get; init; }
}

sealed class BitkubPlaceOrderResult
{
	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("typ")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubOrderTypes Type { get; set; }

	[JsonProperty("amt")]
	public decimal Amount { get; set; }

	[JsonProperty("rat")]
	public decimal Rate { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("cre")]
	public decimal Credit { get; set; }

	[JsonProperty("rec")]
	public decimal Received { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("ci")]
	public string ClientId { get; set; }
}

sealed class BitkubCancelOrderRequest
{
	[JsonProperty("sym")]
	public string Symbol { get; init; }

	[JsonProperty("id")]
	public string OrderId { get; init; }

	[JsonProperty("sd")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubSides Side { get; init; }
}

sealed class BitkubOpenOrder
{
	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubSides Side { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubOrderTypes Type { get; set; }

	[JsonProperty("rate")]
	public decimal Rate { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("credit")]
	public decimal Credit { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("receive")]
	public decimal Receive { get; set; }

	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class BitkubOrderHistoryItem
{
	[JsonProperty("txn_id")]
	public string TransactionId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("taken_by_me")]
	public bool IsTakenByMe { get; set; }

	[JsonProperty("is_maker")]
	public bool IsMaker { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubSides Side { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubOrderTypes Type { get; set; }

	[JsonProperty("rate")]
	public decimal Rate { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("credit")]
	public decimal Credit { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("order_closed_at")]
	public long? ClosedAt { get; set; }
}

sealed class BitkubOrderInfo
{
	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("post_only")]
	public bool IsPostOnly { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("rate")]
	public decimal Rate { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("credit")]
	public decimal Credit { get; set; }

	[JsonProperty("filled")]
	public decimal Filled { get; set; }

	[JsonProperty("total")]
	public decimal Total { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubOrderStatuses Status { get; set; }

	[JsonProperty("partial_filled")]
	public bool IsPartiallyFilled { get; set; }

	[JsonProperty("remaining")]
	public decimal Remaining { get; set; }

	[JsonProperty("history")]
	public BitkubOrderInfoTrade[] History { get; set; }
}

sealed class BitkubOrderInfoTrade
{
	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("credit")]
	public decimal Credit { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("rate")]
	public decimal Rate { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("txn_id")]
	public string TransactionId { get; set; }
}

sealed class BitkubBalance
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
