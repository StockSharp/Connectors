namespace StockSharp.CoinCatch.Native.Model;

sealed class CoinCatchBalance
{
	[JsonProperty("coinName")]
	public string Coin { get; set; }

	[JsonProperty("marginCoin")]
	private string MarginCoin
	{
		set
		{
			if (!value.IsEmpty())
				Coin = value;
		}
	}

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("frozen")]
	public decimal Frozen { get; set; }

	[JsonProperty("lock")]
	public decimal Locked { get; set; }

	[JsonProperty("locked")]
	private decimal AlternativeLocked
	{
		set => Locked = value;
	}

	[JsonProperty("equity")]
	public decimal? Equity { get; set; }

	[JsonProperty("unrealizedPL")]
	public decimal? UnrealizedProfit { get; set; }

	[JsonProperty("uTime")]
	public long UpdateTime { get; set; }

	[JsonIgnore]
	public decimal Blocked => Frozen + Locked;

	[JsonIgnore]
	public decimal CurrentValue
		=> Equity ?? Available + Blocked;
}

sealed class CoinCatchOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("clientOid")]
	private string AlternativeClientOrderId
	{
		set
		{
			if (!value.IsEmpty())
				ClientOrderId = value;
		}
	}

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("priceAvg")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("fillPrice")]
	private decimal? SpotAveragePrice
	{
		set => AveragePrice = value;
	}

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("size")]
	private decimal? FuturesQuantity
	{
		set => Quantity = value;
	}

	[JsonProperty("fillQuantity")]
	public decimal? FilledQuantity { get; set; }

	[JsonProperty("filledQty")]
	private decimal? FuturesFilledQuantity
	{
		set => FilledQuantity = value;
	}

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("force")]
	public string TimeInForce { get; set; }

	[JsonProperty("timeInForce")]
	private string FuturesTimeInForce
	{
		set => TimeInForce = value;
	}

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("state")]
	private string FuturesStatus
	{
		set => Status = value;
	}

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("marginCoin")]
	public string MarginCoin { get; set; }

	[JsonProperty("reduceOnly")]
	public bool ReduceOnly { get; set; }

	[JsonProperty("cTime")]
	public long CreateTime { get; set; }

	[JsonProperty("uTime")]
	public long UpdateTime { get; set; }

	[JsonIgnore]
	public decimal? RemainingQuantity
		=> Quantity is decimal quantity
			? (quantity - (FilledQuantity ?? 0m)).Max(0)
			: null;
}

sealed class CoinCatchOrderPage
{
	[JsonProperty("nextFlag")]
	public bool HasNext { get; set; }

	[JsonProperty("endId")]
	public string EndId { get; set; }

	[JsonProperty("orderList")]
	public CoinCatchOrder[] Orders { get; set; }
}

sealed class CoinCatchPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("marginCoin")]
	public string MarginCoin { get; set; }

	[JsonProperty("holdSide")]
	public string Side { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }

	[JsonProperty("total")]
	public decimal Total { get; set; }

	[JsonProperty("averageOpenPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("unrealizedPL")]
	public decimal? UnrealizedProfit { get; set; }

	[JsonProperty("liquidationPrice")]
	public decimal? LiquidationPrice { get; set; }

	[JsonProperty("leverage")]
	public decimal? Leverage { get; set; }

	[JsonProperty("uTime")]
	public long UpdateTime { get; set; }
}

sealed class CoinCatchPlaceOrderResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("clientOid")]
	private string AlternativeClientOrderId
	{
		set
		{
			if (!value.IsEmpty())
				ClientOrderId = value;
		}
	}
}
