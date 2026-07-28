namespace StockSharp.AscendEx.Native.Model;

sealed class AscendExBalance
{
	[JsonProperty("asset")]
	public string Currency { get; set; }

	[JsonProperty("totalBalance")]
	public decimal Amount { get; set; }

	[JsonProperty("availableBalance")]
	public decimal Available { get; set; }

	[JsonIgnore]
	public string SecurityCode { get; set; }

	[JsonIgnore]
	public bool IsPosition { get; set; }
}

sealed class AscendExFuturesAccount
{
	[JsonProperty("collaterals")]
	public AscendExFuturesCollateral[] Collaterals { get; set; }

	[JsonProperty("contracts")]
	public AscendExFuturesPosition[] Positions { get; set; }

	[JsonProperty("positions")]
	private AscendExFuturesPosition[] AlternatePositions
	{
		set
		{
			if (value is not null)
				Positions = value;
		}
	}
}

sealed class AscendExFuturesCollateral
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("availableForTransfer")]
	public decimal Available { get; set; }
}

sealed class AscendExFuturesPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("position")]
	public decimal Position { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("avgOpenPrice")]
	public decimal AverageOpenPrice { get; set; }

	[JsonProperty("markPrice")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("unrealizedPnl")]
	public decimal UnrealizedPnl { get; set; }
}

class AscendExOrder
{
	[JsonProperty("orderId")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Pair { get; set; }

	[JsonProperty("side")]
	public string Action { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("orderType")]
	public string Type { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("execInst")]
	public string ExecutionInstruction { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("orderPrice")]
	private decimal AlternatePrice
	{
		set => Price = value;
	}

	[JsonProperty("avgPx")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("avgFilledPx")]
	private decimal AlternateAveragePrice
	{
		set => AveragePrice = value;
	}

	[JsonProperty("orderQty")]
	public decimal OriginalAmount { get; set; }

	[JsonProperty("cumFilledQty")]
	public decimal ExecutedAmount { get; set; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("lastExecTime")]
	public long UpdatedTimestamp { get; set; }

	[JsonProperty("timestamp")]
	public long CreatedTimestamp { get; set; }

	[JsonProperty("cumFee")]
	public decimal? Fee { get; set; }

	[JsonProperty("feeAsset")]
	public string FeeAsset { get; set; }

	[JsonIgnore]
	public decimal RemainingAmount
		=> (OriginalAmount - ExecutedAmount).Max(0);

	[JsonIgnore]
	public long Timestamp
		=> UpdatedTimestamp > 0
			? UpdatedTimestamp
			: CreatedTimestamp;

	[JsonIgnore]
	public long? ClientId => null;

	[JsonIgnore]
	public string Condition => StopPrice is > 0 ? "stop" : null;
}

sealed class AscendExPrivateTrade
{
	public string TradeId { get; set; }
	public string OrderId { get; set; }
	public string Pair { get; set; }
	public string Action { get; set; }
	public decimal Price { get; set; }
	public decimal BaseAmount { get; set; }
	public decimal QuoteAmount { get; set; }
	public decimal? Fee { get; set; }
	public string FeeSymbol { get; set; }
	public long CreatedTimestamp { get; set; }
	public long Timestamp => CreatedTimestamp;
}

sealed class AscendExPlaceOrderRequest
{
	[JsonProperty("id")]
	public string ClientOid { get; init; }

	[JsonProperty("time")]
	public long Timestamp { get; init; }

	[JsonProperty("symbol")]
	public string Market { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("orderType")]
	public string OrderType { get; init; }

	[JsonProperty("orderQty")]
	public string Volume { get; init; }

	[JsonProperty("orderPrice")]
	public string Price { get; init; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; init; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; init; }

	[JsonProperty("execInst")]
	public string ExecutionInstruction { get; init; }

	[JsonProperty("postOnly")]
	public bool? PostOnly { get; init; }

	[JsonProperty("respInst")]
	public string ResponseInstruction { get; init; }
}

sealed class AscendExOrderAck
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("info")]
	public AscendExOrderAckInfo Info { get; set; }
}

sealed class AscendExOrderAckInfo
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class AscendExPlaceOrderResult
{
	public string OrderId { get; init; }
}
