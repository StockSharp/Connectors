namespace StockSharp.Tokocrypto.Native.Model;

sealed class TokocryptoAccount
{
	[JsonProperty("accountAssets")]
	public TokocryptoBalance[] Assets { get; set; }
}

sealed class TokocryptoBalance
{
	[JsonProperty("asset")]
	public string Currency { get; set; }

	[JsonProperty("free")]
	public decimal Available { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }

	[JsonIgnore]
	public decimal Amount => Available + Locked;
}

sealed class TokocryptoOrder
{
	[JsonProperty("orderId")]
	public string Id { get; set; }

	[JsonProperty("clientId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Pair { get; set; }

	[JsonProperty("side")]
	public int SideCode { get; set; }

	[JsonProperty("type")]
	public int TypeCode { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("origQty")]
	public decimal OriginalAmount { get; set; }

	[JsonProperty("executedQty")]
	public decimal ExecutedAmount { get; set; }

	[JsonProperty("timeInForce")]
	public int TimeInForceCode { get; set; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("createTime")]
	public long CreatedTimestamp { get; set; }

	[JsonIgnore]
	public long UpdatedTimestamp => 0;

	[JsonIgnore]
	public long Timestamp => CreatedTimestamp;

	[JsonIgnore]
	public decimal RemainingAmount
		=> (OriginalAmount - ExecutedAmount).Max(0);

	[JsonIgnore]
	public string Action => SideCode == 0 ? "buy" : "sell";

	[JsonIgnore]
	public string Type
		=> TypeCode switch
		{
			2 => "market",
			3 => "stop_market",
			4 => "stop_limit",
			5 => "take_profit",
			6 => "take_profit_limit",
			7 => "post_only",
			_ => "limit",
		};

	[JsonIgnore]
	public string TimeInForce
		=> TimeInForceCode switch
		{
			2 => "IOC",
			3 => "FOK",
			4 => "POST_ONLY",
			_ => "GTC",
		};

	[JsonIgnore]
	public long? ClientId
		=> long.TryParse(
			ClientOrderId?.StartsWithIgnoreCase("ss-") == true
				? ClientOrderId[3..]
				: ClientOrderId,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var value)
				? value
				: null;
}

sealed class TokocryptoPrivateTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Pair { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal BaseAmount { get; set; }

	[JsonProperty("quoteQty")]
	public decimal QuoteAmount { get; set; }

	[JsonProperty("commission")]
	public decimal? Fee { get; set; }

	[JsonProperty("commissionAsset")]
	public string FeeSymbol { get; set; }

	[JsonProperty("isBuyer")]
	public int IsBuyer { get; set; }

	[JsonProperty("time")]
	public long CreatedTimestamp { get; set; }

	[JsonIgnore]
	public long Timestamp => CreatedTimestamp;

	[JsonIgnore]
	public string Action => IsBuyer == 1 ? "buy" : "sell";
}

sealed class TokocryptoPlaceOrderRequest
{
	public string Market { get; init; }
	public string Side { get; init; }
	public string Volume { get; init; }
	public string QuoteVolume { get; init; }
	public string Price { get; init; }
	public string ClientOid { get; init; }
	public string StopPrice { get; init; }
	public string OrderType { get; init; }
}

sealed class TokocryptoPlaceOrderResult
{
	public TokocryptoOrder Order { get; init; }

	public string OrderId => Order?.Id;
}
