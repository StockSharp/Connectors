namespace StockSharp.Bithumb.Native.Model;

sealed class Order
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("uuid")]
	public string Uuid { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("ord_type")]
	private string LegacyOrderType
	{
		set => OrderType = value;
	}

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }

	[JsonProperty("remaining_volume")]
	public string RemainingVolume { get; set; }

	[JsonProperty("paid_fee")]
	public string PaidFee { get; set; }

	[JsonProperty("executed_volume")]
	public string ExecutedVolume { get; set; }

	[JsonProperty("executed_funds")]
	public string ExecutedFunds { get; set; }

	[JsonIgnore]
	public string Id => OrderId.IsEmpty() ? Uuid : OrderId;
}

sealed class OrdersPage
{
	[JsonProperty("data")]
	public Order[] Data { get; set; }

	[JsonProperty("has_next")]
	public bool HasNext { get; set; }

	[JsonProperty("next_key")]
	public string NextKey { get; set; }
}

sealed class RegisterOrderResponse
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

sealed class CancelOrderResponse
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

sealed class WithdrawResponse
{
	[JsonProperty("uuid")]
	public string Id { get; set; }
}

sealed class RegisterOrderRequest
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; set; }

	[JsonProperty("volume", NullValueHandling = NullValueHandling.Ignore)]
	public string Volume { get; set; }

	[JsonProperty("client_order_id", NullValueHandling = NullValueHandling.Ignore)]
	public string ClientOrderId { get; set; }
}

sealed class SearchOrdersRequest
{
	[JsonProperty("order_ids")]
	public string[] OrderIds { get; set; }
}

sealed class WithdrawRequest
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("net_type")]
	public string Network { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("secondary_address", NullValueHandling = NullValueHandling.Ignore)]
	public string SecondaryAddress { get; set; }
}
