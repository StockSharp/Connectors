namespace StockSharp.NovaDax.Native.Model;

sealed class NovaDaxBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public decimal Total { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("hold")]
	public decimal Hold { get; set; }

	[JsonIgnore]
	public decimal Amount => Total;
}

class NovaDaxOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Pair { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Action { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("averagePrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("amount")]
	public decimal OriginalAmount { get; set; }

	[JsonProperty("filledAmount")]
	public decimal ExecutedAmount { get; set; }

	[JsonProperty("value")]
	public decimal? Value { get; set; }

	[JsonProperty("filledValue")]
	public decimal FilledValue { get; set; }

	[JsonProperty("filledFee")]
	public decimal? Fee { get; set; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("operator")]
	public string Operator { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timestamp")]
	public long CreatedTimestamp { get; set; }

	[JsonIgnore]
	public long UpdatedTimestamp => CreatedTimestamp;

	[JsonIgnore]
	public long Timestamp => CreatedTimestamp;

	[JsonIgnore]
	public decimal RemainingAmount
		=> (OriginalAmount - ExecutedAmount).Max(0);

	[JsonIgnore]
	public long? ClientId
		=> ClientOrderId?.Length > 1 &&
			long.TryParse(
				ClientOrderId.AsSpan(1),
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var value)
					? value
					: null;

	[JsonIgnore]
	public string TimeInForce => null;
}

sealed class NovaDaxPrivateTrade
{
	[JsonProperty("id")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Pair { get; set; }

	[JsonProperty("side")]
	public string Action { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal BaseAmount { get; set; }

	[JsonProperty("feeAmount")]
	public decimal? Fee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeSymbol { get; set; }

	[JsonProperty("timestamp")]
	public long CreatedTimestamp { get; set; }

	[JsonIgnore]
	public long Timestamp => CreatedTimestamp;
}

sealed class NovaDaxPlaceOrderRequest
{
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("value")]
	public string Value { get; init; }

	[JsonProperty("operator")]
	public string Operator { get; init; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; init; }
}

sealed class NovaDaxPlaceOrderResult
{
	public string OrderId { get; init; }
	public NovaDaxOrder Order { get; init; }
}

sealed class NovaDaxCancelResult
{
	[JsonProperty("result")]
	public bool Result { get; set; }
}
