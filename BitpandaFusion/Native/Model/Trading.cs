namespace StockSharp.BitpandaFusion.Native.Model;

sealed class BitpandaFusionBalance
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("available")]
	public string Available { get; set; }

	[JsonProperty("locked")]
	public string Locked { get; set; }
}

sealed class BitpandaFusionTier
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeMode")]
	public string FeeMode { get; set; }

	[JsonProperty("fee_mode")]
	private string LegacyFeeMode { set => FeeMode ??= value; }

	[JsonProperty("requiredVolume30d")]
	public string RequiredVolume30d { get; set; }

	[JsonProperty("required_volume30d")]
	private string LegacyRequiredVolume30d { set => RequiredVolume30d ??= value; }
}

sealed class BitpandaFusionAccount
{
	[JsonProperty("tradedVolume30d")]
	public string TradedVolume30d { get; set; }

	[JsonProperty("traded_volume30d")]
	private string LegacyTradedVolume30d { set => TradedVolume30d ??= value; }

	[JsonProperty("currentTier")]
	public BitpandaFusionTier CurrentTier { get; set; }

	[JsonProperty("current_tier")]
	private BitpandaFusionTier LegacyCurrentTier { set => CurrentTier ??= value; }

	[JsonProperty("nextTier")]
	public BitpandaFusionTier NextTier { get; set; }

	[JsonProperty("next_tier")]
	private BitpandaFusionTier LegacyNextTier { set => NextTier ??= value; }
}

sealed class BitpandaFusionFee
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class BitpandaFusionOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionOrderSides Side { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionOrderTypes Type { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionOrderStatuses Status { get; set; }

	[JsonProperty("pricedIn")]
	public string PricedIn { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("limitPrice")]
	public string LimitPrice { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("filledQuantity")]
	public string FilledQuantity { get; set; }

	[JsonProperty("filledAmount")]
	public string FilledAmount { get; set; }

	[JsonProperty("filledPercentage")]
	public string FilledPercentage { get; set; }

	[JsonProperty("filledAveragePrice")]
	public string FilledAveragePrice { get; set; }

	[JsonProperty("fee")]
	public BitpandaFusionFee Fee { get; set; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionTimeInForces TimeInForce { get; set; }

	[JsonProperty("updatedAt")]
	public DateTimeOffset? UpdatedAt { get; set; }

	[JsonProperty("createdAt")]
	public DateTimeOffset? CreatedAt { get; set; }

	[JsonProperty("executedAt")]
	public DateTimeOffset? ExecutedAt { get; set; }

	[JsonProperty("endTime")]
	public DateTimeOffset? EndTime { get; set; }
}

sealed class BitpandaFusionTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionTradeSides Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("fee")]
	public BitpandaFusionFee Fee { get; set; }

	[JsonProperty("totalAmount")]
	public string TotalAmount { get; set; }

	[JsonProperty("total")]
	public string Total { get; set; }

	[JsonProperty("executedAt")]
	public DateTimeOffset ExecutedAt { get; set; }
}

sealed class BitpandaFusionOrdersPage
{
	[JsonProperty("data")]
	public BitpandaFusionOrder[] Data { get; set; }

	[JsonProperty("meta")]
	public BitpandaFusionPageMeta Meta { get; set; }
}

sealed class BitpandaFusionTradesPage
{
	[JsonProperty("data")]
	public BitpandaFusionTrade[] Data { get; set; }

	[JsonProperty("meta")]
	public BitpandaFusionPageMeta Meta { get; set; }
}

sealed class BitpandaFusionOrdersFilter
{
	public string Pair { get; init; }
	public BitpandaFusionOrderStatuses? Status { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }
	public int Limit { get; init; }
	public string Cursor { get; init; }
}

sealed class BitpandaFusionTradesFilter
{
	public string Pair { get; init; }
	public string OrderId { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }
	public int Limit { get; init; }
	public string Cursor { get; init; }
}

sealed class BitpandaFusionCreateOrderRequest
{
	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionOrderSides Side { get; init; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionOrderTypes Type { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("limitPrice")]
	public decimal? LimitPrice { get; init; }

	[JsonProperty("triggerPrice")]
	public decimal? TriggerPrice { get; init; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionTimeInForces TimeInForce { get; init; }

	[JsonProperty("endTime")]
	public DateTimeOffset? EndTime { get; init; }
}
