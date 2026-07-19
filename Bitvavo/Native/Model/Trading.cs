namespace StockSharp.Bitvavo.Native.Model;

sealed class BitvavoOrderRequest
{
	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("side")]
	public BitvavoSides Side { get; init; }

	[JsonProperty("orderType")]
	public BitvavoOrderTypes OrderType { get; init; }

	[JsonProperty("operatorId")]
	public long OperatorId { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("amountQuote")]
	public string QuoteAmount { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("triggerAmount")]
	public string TriggerAmount { get; init; }

	[JsonProperty("triggerType")]
	public BitvavoTriggerTypes? TriggerType { get; init; }

	[JsonProperty("triggerReference")]
	public BitvavoTriggerReferences? TriggerReference { get; init; }

	[JsonProperty("timeInForce")]
	public BitvavoTimeInForces? TimeInForce { get; init; }

	[JsonProperty("postOnly")]
	public bool? IsPostOnly { get; init; }

	[JsonProperty("selfTradePrevention")]
	public BitvavoSelfTradePreventions SelfTradePrevention { get; init; } =
		BitvavoSelfTradePreventions.DecrementAndCancel;
}

sealed class BitvavoUpdateOrderRequest
{
	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("operatorId")]
	public long OperatorId { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("amountQuote")]
	public string QuoteAmount { get; init; }

	[JsonProperty("amountRemaining")]
	public string AmountRemaining { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("triggerAmount")]
	public string TriggerAmount { get; init; }

	[JsonProperty("timeInForce")]
	public BitvavoTimeInForces? TimeInForce { get; init; }

	[JsonProperty("postOnly")]
	public bool? IsPostOnly { get; init; }

	[JsonProperty("selfTradePrevention")]
	public BitvavoSelfTradePreventions? SelfTradePrevention { get; init; }
}

sealed class BitvavoOrderLookupQuery : IBitvavoQuery
{
	public string Market { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }

	public BitvavoParameter[] GetParameters()
	{
		var result = new List<BitvavoParameter>
		{
			new("market", Market.ThrowIfEmpty(nameof(Market))),
		};
		if (!ClientOrderId.IsEmpty())
			result.Add(new("clientOrderId", ClientOrderId));
		else
			result.Add(new("orderId", OrderId.ThrowIfEmpty(nameof(OrderId))));
		return [.. result];
	}
}

sealed class BitvavoCancelOrderQuery : IBitvavoQuery
{
	public string Market { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }
	public long OperatorId { get; init; }

	public BitvavoParameter[] GetParameters()
	{
		var result = new List<BitvavoParameter>
		{
			new("market", Market.ThrowIfEmpty(nameof(Market))),
			new("operatorId", OperatorId.ToString(CultureInfo.InvariantCulture)),
		};
		if (!ClientOrderId.IsEmpty())
			result.Add(new("clientOrderId", ClientOrderId));
		else
			result.Add(new("orderId", OrderId.ThrowIfEmpty(nameof(OrderId))));
		return [.. result];
	}
}

sealed class BitvavoCancelOrdersQuery : IBitvavoQuery
{
	public string Market { get; init; }
	public long OperatorId { get; init; }

	public BitvavoParameter[] GetParameters()
	{
		var result = new List<BitvavoParameter>
		{
			new("operatorId", OperatorId.ToString(CultureInfo.InvariantCulture)),
		};
		if (!Market.IsEmpty())
			result.Add(new("market", Market));
		return [.. result];
	}
}

sealed class BitvavoOpenOrdersQuery : IBitvavoQuery
{
	public string Market { get; init; }
	public string Base { get; init; }

	public BitvavoParameter[] GetParameters()
	{
		var result = new List<BitvavoParameter>();
		if (!Market.IsEmpty())
			result.Add(new("market", Market));
		else if (!Base.IsEmpty())
			result.Add(new("base", Base));
		return [.. result];
	}
}

sealed class BitvavoOrdersQuery : IBitvavoQuery
{
	public string Market { get; init; }
	public int Limit { get; init; }
	public long? Start { get; init; }
	public long? End { get; init; }
	public string OrderIdFrom { get; init; }
	public string OrderIdTo { get; init; }

	public BitvavoParameter[] GetParameters()
	{
		var result = new List<BitvavoParameter>
		{
			new("market", Market.ThrowIfEmpty(nameof(Market))),
		};
		if (Limit > 0)
			result.Add(new("limit", Limit.ToString(CultureInfo.InvariantCulture)));
		if (Start is long start)
			result.Add(new("start", start.ToString(CultureInfo.InvariantCulture)));
		if (End is long end)
			result.Add(new("end", end.ToString(CultureInfo.InvariantCulture)));
		if (!OrderIdFrom.IsEmpty())
			result.Add(new("orderIdFrom", OrderIdFrom));
		if (!OrderIdTo.IsEmpty())
			result.Add(new("orderIdTo", OrderIdTo));
		return [.. result];
	}
}

sealed class BitvavoPrivateTradesQuery : IBitvavoQuery
{
	public string Market { get; init; }
	public int Limit { get; init; }
	public long? Start { get; init; }
	public long? End { get; init; }
	public string TradeIdFrom { get; init; }
	public string TradeIdTo { get; init; }

	public BitvavoParameter[] GetParameters()
	{
		var result = new List<BitvavoParameter>
		{
			new("market", Market.ThrowIfEmpty(nameof(Market))),
		};
		if (Limit > 0)
			result.Add(new("limit", Limit.ToString(CultureInfo.InvariantCulture)));
		if (Start is long start)
			result.Add(new("start", start.ToString(CultureInfo.InvariantCulture)));
		if (End is long end)
			result.Add(new("end", end.ToString(CultureInfo.InvariantCulture)));
		if (!TradeIdFrom.IsEmpty())
			result.Add(new("tradeIdFrom", TradeIdFrom));
		if (!TradeIdTo.IsEmpty())
			result.Add(new("tradeIdTo", TradeIdTo));
		return [.. result];
	}
}

sealed class BitvavoBalanceQuery : IBitvavoQuery
{
	public string Symbol { get; init; }

	public BitvavoParameter[] GetParameters()
		=> Symbol.IsEmpty() ? [] : [new("symbol", Symbol)];
}

sealed class BitvavoOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("created")]
	public long? Created { get; set; }

	[JsonProperty("updated")]
	public long? Updated { get; set; }

	[JsonProperty("createdNs")]
	public long? CreatedNanoseconds { get; set; }

	[JsonProperty("updatedNs")]
	public long? UpdatedNanoseconds { get; set; }

	[JsonProperty("status")]
	public BitvavoOrderStatuses? Status { get; set; }

	[JsonProperty("side")]
	public BitvavoSides? Side { get; set; }

	[JsonProperty("orderType")]
	public BitvavoOrderTypes? OrderType { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("amountRemaining")]
	public decimal? AmountRemaining { get; set; }

	[JsonProperty("amountQuote")]
	public decimal? QuoteAmount { get; set; }

	[JsonProperty("amountQuoteRemaining")]
	public decimal? QuoteAmountRemaining { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("triggerAmount")]
	public decimal? TriggerAmount { get; set; }

	[JsonProperty("triggerType")]
	public BitvavoTriggerTypes? TriggerType { get; set; }

	[JsonProperty("triggerReference")]
	public BitvavoTriggerReferences? TriggerReference { get; set; }

	[JsonProperty("filledAmount")]
	public decimal? FilledAmount { get; set; }

	[JsonProperty("filledAmountQuote")]
	public decimal? FilledQuoteAmount { get; set; }

	[JsonProperty("feePaid")]
	public decimal? FeePaid { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("fills")]
	public BitvavoFill[] Fills { get; set; }

	[JsonProperty("selfTradePrevention")]
	public BitvavoSelfTradePreventions? SelfTradePrevention { get; set; }

	[JsonProperty("visible")]
	public bool? IsVisible { get; set; }

	[JsonProperty("timeInForce")]
	public BitvavoTimeInForces? TimeInForce { get; set; }

	[JsonProperty("postOnly")]
	public bool? IsPostOnly { get; set; }

	[JsonProperty("operatorId")]
	public long? OperatorId { get; set; }

}

sealed class BitvavoFill
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("fillId")]
	public string FillId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("timestampNs")]
	public long? TimestampNanoseconds { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("side")]
	public BitvavoSides Side { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("taker")]
	public bool IsTaker { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("settled")]
	public bool? IsSettled { get; set; }

	[JsonIgnore]
	public string EffectiveId => FillId ?? Id;
}

sealed class BitvavoBalance
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("inOrder")]
	public decimal InOrder { get; set; }
}

sealed class BitvavoCancelResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("operatorId")]
	public long? OperatorId { get; set; }
}
