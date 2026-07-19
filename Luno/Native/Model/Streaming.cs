namespace StockSharp.Luno.Native.Model;

sealed class LunoStreamCredentials
{
	[JsonProperty("api_key_id")]
	public string ApiKeyId { get; init; }

	[JsonProperty("api_key_secret")]
	public string ApiKeySecret { get; init; }
}

sealed class LunoMarketStreamEnvelope
{
	[JsonProperty("sequence")]
	public long Sequence { get; init; }

	[JsonProperty("asks")]
	public LunoMarketStreamOrder[] Asks { get; init; }

	[JsonProperty("bids")]
	public LunoMarketStreamOrder[] Bids { get; init; }

	[JsonProperty("status")]
	public LunoStreamStatuses? Status { get; init; }

	[JsonProperty("trade_updates")]
	public LunoMarketTradeUpdate[] TradeUpdates { get; init; }

	[JsonProperty("create_update")]
	public LunoMarketCreateUpdate CreateUpdate { get; init; }

	[JsonProperty("delete_update")]
	public LunoMarketDeleteUpdate DeleteUpdate { get; init; }

	[JsonProperty("status_update")]
	public LunoMarketStatusUpdate StatusUpdate { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }
}

sealed class LunoMarketStreamOrder
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

sealed class LunoMarketTradeUpdate
{
	[JsonProperty("sequence")]
	public long Sequence { get; init; }

	[JsonProperty("base")]
	public decimal Base { get; init; }

	[JsonProperty("counter")]
	public decimal Counter { get; init; }

	[JsonProperty("maker_order_id")]
	public string MakerOrderId { get; init; }

	[JsonProperty("taker_order_id")]
	public string TakerOrderId { get; init; }

	[JsonProperty("order_id")]
	public string LegacyOrderId { get; init; }
}

sealed class LunoMarketCreateUpdate
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("type")]
	public LunoLimitSides Type { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; init; }
}

sealed class LunoMarketDeleteUpdate
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }
}

sealed class LunoMarketStatusUpdate
{
	[JsonProperty("status")]
	public LunoStreamStatuses Status { get; init; }
}

sealed class LunoStreamPriceLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class LunoStreamTrade
{
	public long Sequence { get; init; }
	public DateTime Timestamp { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides TakerSide { get; init; }
	public string MakerOrderId { get; init; }
	public string TakerOrderId { get; init; }
}

sealed class LunoMarketStreamState
{
	public string Pair { get; init; }
	public long Sequence { get; init; }
	public DateTime Timestamp { get; init; }
	public LunoStreamStatuses Status { get; init; }
	public LunoStreamPriceLevel[] Bids { get; init; }
	public LunoStreamPriceLevel[] Asks { get; init; }
	public LunoStreamTrade[] Trades { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum LunoUserEventTypes
{
	[EnumMember(Value = "order_status")]
	OrderStatus,

	[EnumMember(Value = "order_fill")]
	OrderFill,

	[EnumMember(Value = "balance_update")]
	Balance,
}

sealed class LunoUserStreamEnvelope
{
	[JsonProperty("type")]
	public LunoUserEventTypes? Type { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("order_status_update")]
	public LunoUserOrderStatusUpdate OrderStatusUpdate { get; init; }

	[JsonProperty("order_fill_update")]
	public LunoUserOrderFillUpdate OrderFillUpdate { get; init; }

	[JsonProperty("balance_update")]
	public LunoUserBalanceUpdate BalanceUpdate { get; init; }
}

sealed class LunoUserOrderStatusUpdate
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("market_id")]
	public string MarketId { get; init; }

	[JsonProperty("status")]
	public LunoOrderStatuses Status { get; init; }
}

sealed class LunoUserOrderFillUpdate
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("market_id")]
	public string MarketId { get; init; }

	[JsonProperty("base_fill")]
	public decimal BaseFill { get; init; }

	[JsonProperty("counter_fill")]
	public decimal CounterFill { get; init; }

	[JsonProperty("base_delta")]
	public decimal BaseDelta { get; init; }

	[JsonProperty("counter_delta")]
	public decimal CounterDelta { get; init; }

	[JsonProperty("base_fee")]
	public decimal BaseFee { get; init; }

	[JsonProperty("counter_fee")]
	public decimal CounterFee { get; init; }

	[JsonProperty("base_fee_delta")]
	public decimal BaseFeeDelta { get; init; }

	[JsonProperty("counter_fee_delta")]
	public decimal CounterFeeDelta { get; init; }
}

sealed class LunoUserBalanceUpdate
{
	[JsonProperty("account_id")]
	public long AccountId { get; init; }

	[JsonProperty("row_index")]
	public long RowIndex { get; init; }

	[JsonProperty("balance")]
	public decimal Balance { get; init; }

	[JsonProperty("balance_delta")]
	public decimal BalanceDelta { get; init; }

	[JsonProperty("available")]
	public decimal Available { get; init; }

	[JsonProperty("available_delta")]
	public decimal AvailableDelta { get; init; }
}
