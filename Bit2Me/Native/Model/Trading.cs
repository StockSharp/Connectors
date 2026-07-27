namespace StockSharp.Bit2Me.Native.Model;

sealed class Bit2MeOrderRequest
{
	[JsonProperty("side")]
	public Bit2MeSides Side { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("orderType")]
	public Bit2MeOrderTypes OrderType { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("postOnly")]
	public bool? IsPostOnly { get; init; }

	[JsonProperty("timeInForce")]
	public Bit2MeTimeInForces? TimeInForce { get; init; }
}

sealed class Bit2MeOrdersQuery : IBit2MeQuery
{
	public string Ids { get; init; }
	public string Symbol { get; init; }
	public Bit2MeSides? Side { get; init; }
	public Bit2MeOrderTypes? OrderType { get; init; }
	public string StatusIn { get; init; }
	public DateTime? StartTime { get; init; }
	public DateTime? EndTime { get; init; }
	public int Limit { get; init; }
	public int Offset { get; init; }
	public string ClientOrderId { get; init; }

	public Bit2MeParameter[] GetParameters()
	{
		var result = new List<Bit2MeParameter>();
		Add(result, "ids", Ids);
		Add(result, "symbol", Symbol);
		Add(result, "side", Side?.ToString().ToLowerInvariant());
		Add(result, "orderType", OrderType?.ToNative());
		Add(result, "status_in", StatusIn);
		if (StartTime is DateTime startTime)
			Add(result, "startTime", startTime.ToBit2MeDate());
		if (EndTime is DateTime endTime)
			Add(result, "endTime", endTime.ToBit2MeDate());
		if (Limit > 0)
			Add(result, "limit", Limit.Min(100)
				.ToString(CultureInfo.InvariantCulture));
		if (Offset > 0)
			Add(result, "offset", Offset.ToString(CultureInfo.InvariantCulture));
		Add(result, "sort", "createdAt");
		Add(result, "direction", "desc");
		Add(result, "clientOrderId", ClientOrderId);
		return [.. result];
	}

	private static void Add(ICollection<Bit2MeParameter> result, string name,
		string value)
	{
		if (!value.IsEmpty())
			result.Add(new(name, value));
	}
}

sealed class Bit2MeTradesQuery : IBit2MeQuery
{
	public string Ids { get; init; }
	public string Symbol { get; init; }
	public Bit2MeSides? Side { get; init; }
	public Bit2MeOrderTypes? OrderType { get; init; }
	public DateTime? StartTime { get; init; }
	public DateTime? EndTime { get; init; }
	public int Limit { get; init; }
	public int Offset { get; init; }

	public Bit2MeParameter[] GetParameters()
	{
		var result = new List<Bit2MeParameter>();
		Add(result, "ids", Ids);
		Add(result, "symbol", Symbol);
		Add(result, "side", Side?.ToString().ToLowerInvariant());
		Add(result, "orderType", OrderType?.ToNative());
		if (StartTime is DateTime startTime)
			Add(result, "startTime", startTime.ToBit2MeDate());
		if (EndTime is DateTime endTime)
			Add(result, "endTime", endTime.ToBit2MeDate());
		if (Limit > 0)
			Add(result, "limit", Limit.Min(50)
				.ToString(CultureInfo.InvariantCulture));
		if (Offset > 0)
			Add(result, "offset", Offset.ToString(CultureInfo.InvariantCulture));
		Add(result, "sort", "createdAt");
		Add(result, "direction", "desc");
		return [.. result];
	}

	private static void Add(ICollection<Bit2MeParameter> result, string name,
		string value)
	{
		if (!value.IsEmpty())
			result.Add(new(name, value));
	}
}

sealed class Bit2MeOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("side")]
	public Bit2MeSides Side { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("orderAmount")]
	public decimal? OrderAmount { get; set; }

	[JsonProperty("filledAmount")]
	public decimal FilledAmount { get; set; }

	[JsonProperty("dustAmount")]
	public decimal DustAmount { get; set; }

	[JsonProperty("feeAmount")]
	public decimal? FeeAmount { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("status")]
	public Bit2MeOrderStatuses Status { get; set; }

	[JsonProperty("orderType")]
	public Bit2MeOrderTypes OrderType { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("postOnly")]
	public bool? IsPostOnly { get; set; }

	[JsonProperty("timeInForce")]
	public Bit2MeTimeInForces? TimeInForce { get; set; }

	[JsonIgnore]
	public decimal EffectiveAmount => OrderAmount ?? Amount ?? 0m;
}

sealed class Bit2MeTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public Bit2MeSides Side { get; set; }

	[JsonProperty("orderType")]
	public Bit2MeOrderTypes OrderType { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("feeAmount")]
	public decimal? FeeAmount { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }
}

sealed class Bit2MeTradePage
{
	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("data")]
	public Bit2MeTrade[] Data { get; set; }
}

sealed class Bit2MeWallet
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("blockedBalance")]
	public decimal BlockedBalance { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }
}
