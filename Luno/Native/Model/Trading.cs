namespace StockSharp.Luno.Native.Model;

sealed class LunoBalancesResponse
{
	[JsonProperty("balance")]
	public LunoBalance[] Balances { get; init; }
}

sealed class LunoBalance
{
	[JsonProperty("account_id")]
	public string AccountId { get; init; }

	[JsonProperty("asset")]
	public string Asset { get; init; }

	[JsonProperty("balance")]
	public decimal Balance { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("reserved")]
	public decimal Reserved { get; init; }

	[JsonProperty("unconfirmed")]
	public decimal Unconfirmed { get; init; }
}

sealed class LunoOrdersResponse
{
	[JsonProperty("orders")]
	public LunoOrder[] Orders { get; init; }
}

sealed class LunoOrder
{
	[JsonProperty("base")]
	public decimal BaseFilled { get; init; }

	[JsonProperty("base_account_id")]
	public long BaseAccountId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("completed_timestamp")]
	public long CompletedTimestamp { get; init; }

	[JsonProperty("counter")]
	public decimal CounterFilled { get; init; }

	[JsonProperty("counter_account_id")]
	public long CounterAccountId { get; init; }

	[JsonProperty("creation_timestamp")]
	public long CreationTimestamp { get; init; }

	[JsonProperty("expiration_timestamp")]
	public long ExpirationTimestamp { get; init; }

	[JsonProperty("fee_base")]
	public decimal BaseFee { get; init; }

	[JsonProperty("fee_counter")]
	public decimal CounterFee { get; init; }

	[JsonProperty("limit_price")]
	public decimal LimitPrice { get; init; }

	[JsonProperty("limit_volume")]
	public decimal LimitVolume { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("side")]
	public LunoSides Side { get; init; }

	[JsonProperty("status")]
	public LunoOrderStatuses Status { get; init; }

	[JsonProperty("stop_direction")]
	public LunoStopDirections? StopDirection { get; init; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; init; }

	[JsonProperty("time_in_force")]
	public LunoTimeInForce? TimeInForce { get; init; }

	[JsonProperty("type")]
	public LunoOrderTypes Type { get; init; }
}

sealed class LunoUserTradesResponse
{
	[JsonProperty("trades")]
	public LunoUserTrade[] Trades { get; init; }
}

sealed class LunoUserTrade
{
	[JsonProperty("base")]
	public decimal Base { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("counter")]
	public decimal Counter { get; init; }

	[JsonProperty("fee_base")]
	public decimal BaseFee { get; init; }

	[JsonProperty("fee_counter")]
	public decimal CounterFee { get; init; }

	[JsonProperty("is_buy")]
	public bool IsBuy { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("sequence")]
	public long Sequence { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("type")]
	public LunoLimitSides Type { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; init; }
}

sealed class LunoOrderListRequest
{
	public string Pair { get; init; }
	public bool IsClosed { get; init; }
	public long? CreatedBefore { get; init; }
	public int? Limit { get; init; }
}

sealed class LunoOrderLookupRequest
{
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }
}

sealed class LunoUserTradesRequest
{
	public string Pair { get; init; }
	public long? Since { get; init; }
	public long? Before { get; init; }
	public long? AfterSequence { get; init; }
	public long? BeforeSequence { get; init; }
	public bool? IsDescending { get; init; }
	public int? Limit { get; init; }
}

sealed class LunoLimitOrderRequest
{
	public string Pair { get; init; }
	public LunoLimitSides Side { get; init; }
	public LunoTimeInForce TimeInForce { get; init; }
	public bool IsPostOnly { get; init; }
	public decimal Volume { get; init; }
	public decimal Price { get; init; }
	public decimal? StopPrice { get; init; }
	public LunoStopDirections? StopDirection { get; init; }
	public long Timestamp { get; init; }
	public long TimeToLive { get; init; }
	public string ClientOrderId { get; init; }
}

sealed class LunoMarketOrderRequest
{
	public string Pair { get; init; }
	public LunoSides Side { get; init; }
	public decimal? CounterVolume { get; init; }
	public decimal? BaseVolume { get; init; }
	public long Timestamp { get; init; }
	public long TimeToLive { get; init; }
	public string ClientOrderId { get; init; }
}

sealed class LunoCancelOrderRequest
{
	public string OrderId { get; init; }
}
