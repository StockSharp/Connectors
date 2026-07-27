namespace StockSharp.CoinTR.Native.Model;

sealed class CoinTRBalance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("frozen")]
	public decimal Frozen { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }

	[JsonProperty("limitAvailable")]
	public decimal LimitAvailable { get; set; }

	[JsonProperty("uTime")]
	public long UpdateTime { get; set; }
}

sealed class CoinTROrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instId")]
	private string InstrumentId
	{
		set
		{
			if (!value.IsEmpty())
				Symbol = value;
		}
	}

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOid")]
	public string ClientOrderId { get; set; }

	[JsonProperty("clientOId")]
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

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("force")]
	public string Force { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("baseVolume")]
	public decimal? BaseVolume { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("tpslType")]
	public string TriggerType { get; set; }

	[JsonProperty("cTime")]
	public long CreateTime { get; set; }

	[JsonProperty("uTime")]
	public long UpdateTime { get; set; }
}

sealed class CoinTRFill
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("priceAvg")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("feeDetail")]
	public JToken FeeDetail { get; set; }

	[JsonProperty("cTime")]
	public long CreateTime { get; set; }

	[JsonProperty("uTime")]
	public long UpdateTime { get; set; }
}

sealed class CoinTRFeeDetail
{
	[JsonProperty("feeCoin")]
	public string FeeCoin { get; set; }

	[JsonProperty("totalFee")]
	public decimal? TotalFee { get; set; }
}

sealed class CoinTRPlaceOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("orderType")]
	public string OrderType { get; init; }

	[JsonProperty("force")]
	public string Force { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("clientOid")]
	public string ClientOrderId { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }

	[JsonProperty("tpslType")]
	public string TriggerType { get; init; }
}

sealed class CoinTRPlaceOrderResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOid")]
	public string ClientOrderId { get; set; }
}
