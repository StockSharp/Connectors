namespace StockSharp.BitoPro.Native.Model;

sealed class BitoProBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("stake")]
	public decimal Stake { get; set; }

	[JsonProperty("tradable")]
	public bool IsTradable { get; set; }
}

sealed class BitoProOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("avgExecutionPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("originalAmount")]
	public decimal OriginalAmount { get; set; }

	[JsonProperty("remainingAmount")]
	public decimal RemainingAmount { get; set; }

	[JsonProperty("executedAmount")]
	public decimal ExecutedAmount { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("feeSymbol")]
	public string FeeSymbol { get; set; }

	[JsonProperty("bitoFee")]
	public decimal BitoFee { get; set; }

	[JsonProperty("total")]
	public decimal Total { get; set; }

	[JsonProperty("seq")]
	public string Sequence { get; set; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("condition")]
	public string Condition { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("clientId")]
	public long? ClientId { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("createdTimestamp")]
	public long CreatedTimestamp { get; set; }

	[JsonProperty("updatedTimestamp")]
	public long UpdatedTimestamp { get; set; }
}

sealed class BitoProPrivateTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("baseAmount")]
	public decimal BaseAmount { get; set; }

	[JsonProperty("quoteAmount")]
	public decimal QuoteAmount { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("feeSymbol")]
	public string FeeSymbol { get; set; }

	[JsonProperty("isTaker")]
	public bool IsTaker { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("createdTimestamp")]
	public long CreatedTimestamp { get; set; }
}

sealed class BitoProPlaceOrderRequest
{
	[JsonProperty("action")]
	public string Action { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; init; }

	[JsonProperty("condition")]
	public string Condition { get; init; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; init; }

	[JsonProperty("clientId")]
	public int? ClientId { get; init; }
}

sealed class BitoProPlaceOrderResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("clientId")]
	public long? ClientId { get; set; }
}
