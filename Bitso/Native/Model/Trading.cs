namespace StockSharp.Bitso.Native.Model;

sealed class BitsoOpenOrdersQuery
{
	public string Book { get; init; }
	public string Currency { get; init; }
	public int Limit { get; init; }
}

sealed class BitsoUserTradesQuery
{
	public string Book { get; init; }
	public int Limit { get; init; }
	public string Marker { get; init; }
	public bool IsAscending { get; init; }
}

sealed class BitsoPlaceOrderRequest
{
	[JsonProperty("book")]
	public string Book { get; init; }

	[JsonProperty("major")]
	public string Major { get; init; }

	[JsonProperty("minor")]
	public string Minor { get; init; }

	[JsonProperty("origin_id")]
	public string OriginId { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoSides Side { get; init; }

	[JsonProperty("stop")]
	public string Stop { get; init; }

	[JsonProperty("time_in_force")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoTimeInForces? TimeInForce { get; init; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoOrderTypes Type { get; init; }

	[JsonProperty("slippage_tolerance")]
	public decimal? SlippageTolerance { get; init; }
}

sealed class BitsoModifyOrderRequest
{
	[JsonProperty("major")]
	public string Major { get; init; }

	[JsonProperty("minor")]
	public string Minor { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("stop")]
	public string Stop { get; init; }
}

sealed class BitsoOrderId
{
	[JsonProperty("oid")]
	public string OrderId { get; set; }
}

sealed class BitsoOrder
{
	[JsonProperty("book")]
	public string Book { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }

	[JsonProperty("oid")]
	public string OrderId { get; set; }

	[JsonProperty("origin_id")]
	public string OriginId { get; set; }

	[JsonProperty("original_amount")]
	public decimal OriginalAmount { get; set; }

	[JsonProperty("unfilled_amount")]
	public decimal UnfilledAmount { get; set; }

	[JsonProperty("original_value")]
	public decimal OriginalValue { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("stop")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoSides Side { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoOrderStatuses Status { get; set; }

	[JsonProperty("time_in_force")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoTimeInForces? TimeInForce { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoOrderTypes Type { get; set; }
}

sealed class BitsoUserTrade
{
	[JsonProperty("book")]
	public string Book { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("fees_amount")]
	public decimal FeesAmount { get; set; }

	[JsonProperty("fees_currency")]
	public string FeesCurrency { get; set; }

	[JsonProperty("major")]
	public decimal Major { get; set; }

	[JsonProperty("major_currency")]
	public string MajorCurrency { get; set; }

	[JsonProperty("maker_side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoSides MakerSide { get; set; }

	[JsonProperty("minor")]
	public decimal Minor { get; set; }

	[JsonProperty("minor_currency")]
	public string MinorCurrency { get; set; }

	[JsonProperty("oid")]
	public string OrderId { get; set; }

	[JsonProperty("origin_id")]
	public string OriginId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoSides Side { get; set; }

	[JsonProperty("tid")]
	public string TradeId { get; set; }
}

sealed class BitsoBalances
{
	[JsonProperty("balances")]
	public BitsoBalance[] Items { get; set; }
}

sealed class BitsoBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("total")]
	public decimal Total { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("pending_deposit")]
	public decimal PendingDeposit { get; set; }

	[JsonProperty("pending_withdrawal")]
	public decimal PendingWithdrawal { get; set; }
}
