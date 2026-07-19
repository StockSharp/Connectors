namespace StockSharp.CoinDCX.Native.Model;

sealed class CoinDCXTimestampRequest : CoinDCXPrivateRequest
{
}

sealed class CoinDCXPlaceOrderRequest : CoinDCXPrivateRequest
{
	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXSides Side { get; init; }

	[JsonProperty("order_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXOrderTypes OrderType { get; init; }

	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("price_per_unit")]
	public decimal? PricePerUnit { get; init; }

	[JsonProperty("total_quantity")]
	public decimal TotalQuantity { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }
}

sealed class CoinDCXPlaceOrderResponse
{
	[JsonProperty("orders")]
	public CoinDCXOrder[] Orders { get; set; }
}

sealed class CoinDCXOrderIdRequest : CoinDCXPrivateRequest
{
	[JsonProperty("id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }
}

sealed class CoinDCXOrderIdsRequest : CoinDCXPrivateRequest
{
	[JsonProperty("ids")]
	public string[] OrderIds { get; init; }

	[JsonProperty("client_order_ids")]
	public string[] ClientOrderIds { get; init; }
}

sealed class CoinDCXActiveOrdersRequest : CoinDCXPrivateRequest
{
	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXSides? Side { get; init; }
}

sealed class CoinDCXCancelAllRequest : CoinDCXPrivateRequest
{
	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXSides? Side { get; init; }
}

sealed class CoinDCXEditOrderRequest : CoinDCXPrivateRequest
{
	[JsonProperty("id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("price_per_unit")]
	public decimal PricePerUnit { get; init; }
}

sealed class CoinDCXTradeHistoryRequest : CoinDCXPrivateRequest
{
	[JsonProperty("from_id")]
	public long? FromId { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("sort")]
	public string Sort { get; init; }

	[JsonProperty("from_timestamp")]
	public long? FromTimestamp { get; init; }

	[JsonProperty("to_timestamp")]
	public long? ToTimestamp { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class CoinDCXOrder
{
	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("order_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXOrderTypes OrderType { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXSides Side { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXOrderStatuses Status { get; set; }

	[JsonProperty("fee_amount")]
	public decimal FeeAmount { get; set; }

	[JsonProperty("total_quantity")]
	public decimal TotalQuantity { get; set; }

	[JsonProperty("remaining_quantity")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("avg_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("price_per_unit")]
	public decimal PricePerUnit { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }
}

sealed class CoinDCXAccountTrade
{
	[JsonProperty("id")]
	public long TradeId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXSides Side { get; set; }

	[JsonProperty("fee_amount")]
	public decimal FeeAmount { get; set; }

	[JsonProperty("ecode")]
	public string ExchangeCode { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class CoinDCXBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("locked_balance")]
	public decimal LockedBalance { get; set; }
}

sealed class CoinDCXPrivateBalance
{
	[JsonProperty("id")]
	public string WalletId { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("locked_balance")]
	public decimal LockedBalance { get; set; }

	[JsonProperty("currency_id")]
	public string CurrencyId { get; set; }

	[JsonProperty("currency_short_name")]
	public string Currency { get; set; }
}

sealed class CoinDCXPrivateOrder
{
	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("order_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXOrderTypes OrderType { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXSides Side { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinDCXOrderStatuses Status { get; set; }

	[JsonProperty("fee_amount")]
	public decimal FeeAmount { get; set; }

	[JsonProperty("total_quantity")]
	public decimal TotalQuantity { get; set; }

	[JsonProperty("remaining_quantity")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("avg_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("price_per_unit")]
	public decimal PricePerUnit { get; set; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("created_at")]
	public long CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public long UpdatedAt { get; set; }
}

sealed class CoinDCXPrivateTrade
{
	[JsonProperty("o")]
	public string OrderId { get; set; }

	[JsonProperty("c")]
	public string ClientOrderId { get; set; }

	[JsonProperty("t")]
	public string TradeId { get; set; }

	[JsonProperty("s")]
	public string Market { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("T")]
	public decimal Timestamp { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }

	[JsonProperty("f")]
	public decimal Fee { get; set; }

	[JsonProperty("x")]
	public string Status { get; set; }
}
