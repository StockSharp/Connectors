namespace StockSharp.Coinstore.Native.Model;

sealed class CoinstoreBalance
{
	public string Currency { get; set; }
	public decimal Amount { get; set; }
	public decimal Available { get; set; }
}

sealed class CoinstoreBalanceEntry
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("typeName")]
	public string TypeName { get; set; }
}

class CoinstoreOrder
{
	[JsonProperty("ordId")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Pair { get; set; }

	[JsonProperty("clOrdId")]
	public string ClientOid { get; set; }

	[JsonProperty("side")]
	public string Action { get; set; }

	[JsonProperty("ordState")]
	public string Status { get; set; }

	[JsonProperty("ordStatus")]
	private string LegacyStatus
	{
		set
		{
			if (!value.IsEmpty())
				Status = value;
		}
	}

	[JsonProperty("ordType")]
	public string Type { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("ordPrice")]
	public decimal Price { get; set; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("ordQty")]
	public decimal OriginalAmount { get; set; }

	[JsonProperty("leavesQty")]
	public decimal RemainingAmount { get; set; }

	[JsonProperty("cumQty")]
	public decimal ExecutedAmount { get; set; }

	[JsonProperty("cumAmt")]
	public decimal ExecutedValue { get; set; }

	[JsonProperty("ordAmt")]
	public decimal OrderValue { get; set; }

	[JsonProperty("timestamp")]
	public long CreatedTimestamp { get; set; }

	[JsonIgnore]
	public long UpdatedTimestamp => CreatedTimestamp;

	[JsonIgnore]
	public long Timestamp => CreatedTimestamp;

	[JsonIgnore]
	public long? ClientId
	{
		get
		{
			var value = ClientOid;
			if (value?.StartsWith(
				"s", StringComparison.OrdinalIgnoreCase) == true)
				value = value[1..];
			return long.TryParse(value, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var id)
					? id
					: null;
		}
	}

	[JsonIgnore]
	public decimal? StopPrice => null;

	[JsonIgnore]
	public string Condition => null;
}

sealed class CoinstorePrivateTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("execQty")]
	public decimal BaseAmount { get; set; }

	[JsonProperty("execAmt")]
	public decimal QuoteAmount { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("matchTime")]
	public long CreatedTimestamp { get; set; }

	[JsonProperty("side")]
	public int NumericSide { get; set; }

	[JsonProperty("role")]
	public string Liquidity { get; set; }

	[JsonIgnore]
	public string Pair { get; set; }

	[JsonIgnore]
	public string Action
		=> NumericSide >= 0 ? "BUY" : "SELL";

	[JsonIgnore]
	public decimal Price
		=> BaseAmount == 0 ? 0 : QuoteAmount / BaseAmount;

	[JsonIgnore]
	public string FeeSymbol { get; set; }

	[JsonIgnore]
	public long Timestamp => CreatedTimestamp;
}

sealed class CoinstorePlaceOrderRequest
{
	[JsonProperty("clOrdId")]
	public string ClientOid { get; init; }

	[JsonProperty("symbol")]
	public string Market { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("ordType")]
	public string OrderType { get; init; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; init; }

	[JsonProperty("ordPrice")]
	public string Price { get; init; }

	[JsonProperty("ordQty")]
	public string Volume { get; init; }

	[JsonProperty("ordAmt")]
	public string QuoteAmount { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }
}

sealed class CoinstorePlaceOrderResult
{
	public string OrderId { get; init; }
}
