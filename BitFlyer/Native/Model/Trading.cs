namespace StockSharp.BitFlyer.Native.Model;

sealed class BitFlyerBalance
{
	[JsonProperty("currency_code")]
	public string CurrencyCode { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }
}

sealed class BitFlyerCollateral
{
	[JsonProperty("collateral")]
	public decimal Collateral { get; set; }

	[JsonProperty("open_position_pnl")]
	public decimal OpenPositionPnL { get; set; }

	[JsonProperty("require_collateral")]
	public decimal RequiredCollateral { get; set; }

	[JsonProperty("keep_rate")]
	public decimal KeepRate { get; set; }

	[JsonProperty("margin_call_amount")]
	public decimal MarginCallAmount { get; set; }

	[JsonProperty("margin_call_due_date")]
	public string MarginCallDueDate { get; set; }
}

sealed class BitFlyerPosition
{
	[JsonProperty("product_code")]
	public string ProductCode { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerSides Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("commission")]
	public decimal Commission { get; set; }

	[JsonProperty("swap_point_accumulate")]
	public decimal SwapPoints { get; set; }

	[JsonProperty("require_collateral")]
	public decimal RequiredCollateral { get; set; }

	[JsonProperty("open_date")]
	public string OpenDate { get; set; }

	[JsonProperty("leverage")]
	public decimal Leverage { get; set; }

	[JsonProperty("pnl")]
	public decimal PnL { get; set; }

	[JsonProperty("sfd")]
	public decimal Sfd { get; set; }

	[JsonProperty("funding_fees")]
	public decimal FundingFees { get; set; }
}

sealed class BitFlyerChildOrderRequest
{
	[JsonProperty("product_code")]
	public string ProductCode { get; init; }

	[JsonProperty("child_order_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerChildOrderTypes Type { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerSides Side { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }

	[JsonProperty("size")]
	public decimal Size { get; init; }

	[JsonProperty("minute_to_expire")]
	public int? MinutesToExpire { get; init; }

	[JsonProperty("time_in_force")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerTimeInForces TimeInForce { get; init; }
}

sealed class BitFlyerParentOrderRequest
{
	[JsonProperty("order_method")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerOrderMethods Method { get; init; }

	[JsonProperty("minute_to_expire")]
	public int? MinutesToExpire { get; init; }

	[JsonProperty("time_in_force")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerTimeInForces TimeInForce { get; init; }

	[JsonProperty("parameters")]
	public BitFlyerParentOrderParameter[] Parameters { get; init; }
}

sealed class BitFlyerParentOrderParameter
{
	[JsonProperty("product_code")]
	public string ProductCode { get; init; }

	[JsonProperty("condition_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerConditionTypes Type { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerSides Side { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }

	[JsonProperty("size")]
	public decimal Size { get; init; }

	[JsonProperty("trigger_price")]
	public decimal? TriggerPrice { get; init; }

	[JsonProperty("offset")]
	public int? Offset { get; init; }
}

sealed class BitFlyerChildOrderAcceptance
{
	[JsonProperty("child_order_acceptance_id")]
	public string AcceptanceId { get; set; }
}

sealed class BitFlyerParentOrderAcceptance
{
	[JsonProperty("parent_order_acceptance_id")]
	public string AcceptanceId { get; set; }
}

sealed class BitFlyerCancelChildOrderRequest
{
	[JsonProperty("product_code")]
	public string ProductCode { get; init; }

	[JsonProperty("child_order_id")]
	public string OrderId { get; init; }

	[JsonProperty("child_order_acceptance_id")]
	public string AcceptanceId { get; init; }
}

sealed class BitFlyerCancelParentOrderRequest
{
	[JsonProperty("product_code")]
	public string ProductCode { get; init; }

	[JsonProperty("parent_order_id")]
	public string OrderId { get; init; }

	[JsonProperty("parent_order_acceptance_id")]
	public string AcceptanceId { get; init; }
}

sealed class BitFlyerCancelAllRequest
{
	[JsonProperty("product_code")]
	public string ProductCode { get; init; }
}

sealed class BitFlyerChildOrdersRequest
{
	public string ProductCode { get; init; }
	public int? Count { get; init; }
	public long? Before { get; init; }
	public long? After { get; init; }
	public BitFlyerOrderStates? State { get; init; }
	public string OrderId { get; init; }
	public string AcceptanceId { get; init; }
	public string ParentOrderId { get; init; }

	public string ToQueryString()
	{
		var builder = new StringBuilder();
		var hasValue = false;
		BitFlyerQueryWriter.Add(builder, ref hasValue, "product_code",
			ProductCode);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "count", Count);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "before", Before);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "after", After);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "child_order_state",
			State?.ToWire());
		BitFlyerQueryWriter.Add(builder, ref hasValue, "child_order_id", OrderId);
		BitFlyerQueryWriter.Add(builder, ref hasValue,
			"child_order_acceptance_id", AcceptanceId);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "parent_order_id",
			ParentOrderId);
		return builder.ToString();
	}
}

sealed class BitFlyerParentOrdersRequest
{
	public string ProductCode { get; init; }
	public int? Count { get; init; }
	public long? Before { get; init; }
	public long? After { get; init; }
	public BitFlyerOrderStates? State { get; init; }

	public string ToQueryString()
	{
		var builder = new StringBuilder();
		var hasValue = false;
		BitFlyerQueryWriter.Add(builder, ref hasValue, "product_code",
			ProductCode);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "count", Count);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "before", Before);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "after", After);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "parent_order_state",
			State?.ToWire());
		return builder.ToString();
	}
}

sealed class BitFlyerAccountExecutionsRequest
{
	public string ProductCode { get; init; }
	public int? Count { get; init; }
	public long? Before { get; init; }
	public long? After { get; init; }
	public string OrderId { get; init; }
	public string AcceptanceId { get; init; }

	public string ToQueryString()
	{
		var builder = new StringBuilder();
		var hasValue = false;
		BitFlyerQueryWriter.Add(builder, ref hasValue, "product_code",
			ProductCode);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "count", Count);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "before", Before);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "after", After);
		BitFlyerQueryWriter.Add(builder, ref hasValue, "child_order_id", OrderId);
		BitFlyerQueryWriter.Add(builder, ref hasValue,
			"child_order_acceptance_id", AcceptanceId);
		return builder.ToString();
	}
}

sealed class BitFlyerChildOrder
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("child_order_id")]
	public string OrderId { get; set; }

	[JsonProperty("product_code")]
	public string ProductCode { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerSides Side { get; set; }

	[JsonProperty("child_order_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerChildOrderTypes Type { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("child_order_state")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerOrderStates State { get; set; }

	[JsonProperty("expire_date")]
	public string ExpireDate { get; set; }

	[JsonProperty("child_order_date")]
	public string OrderDate { get; set; }

	[JsonProperty("child_order_acceptance_id")]
	public string AcceptanceId { get; set; }

	[JsonProperty("outstanding_size")]
	public decimal OutstandingSize { get; set; }

	[JsonProperty("cancel_size")]
	public decimal CanceledSize { get; set; }

	[JsonProperty("executed_size")]
	public decimal ExecutedSize { get; set; }

	[JsonProperty("total_commission")]
	public decimal TotalCommission { get; set; }

	[JsonProperty("time_in_force")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerTimeInForces? TimeInForce { get; set; }
}

sealed class BitFlyerParentOrder
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("parent_order_id")]
	public string OrderId { get; set; }

	[JsonProperty("product_code")]
	public string ProductCode { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerSides Side { get; set; }

	[JsonProperty("parent_order_type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerParentOrderTypes Type { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("parent_order_state")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerOrderStates State { get; set; }

	[JsonProperty("expire_date")]
	public string ExpireDate { get; set; }

	[JsonProperty("parent_order_date")]
	public string OrderDate { get; set; }

	[JsonProperty("parent_order_acceptance_id")]
	public string AcceptanceId { get; set; }

	[JsonProperty("outstanding_size")]
	public decimal OutstandingSize { get; set; }

	[JsonProperty("cancel_size")]
	public decimal CanceledSize { get; set; }

	[JsonProperty("executed_size")]
	public decimal ExecutedSize { get; set; }

	[JsonProperty("total_commission")]
	public decimal TotalCommission { get; set; }
}

sealed class BitFlyerAccountExecution
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("child_order_id")]
	public string OrderId { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitFlyerSides Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("commission")]
	public decimal Commission { get; set; }

	[JsonProperty("exec_date")]
	public string ExecutionDate { get; set; }

	[JsonProperty("child_order_acceptance_id")]
	public string AcceptanceId { get; set; }
}
