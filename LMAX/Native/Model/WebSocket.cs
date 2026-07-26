namespace StockSharp.LMAX.Native.Model;

class WsMarketSubscribeRequest
{
	[JsonProperty("type")]
	public string Type { get; set; } = WsMessageTypes.Subscribe;

	[JsonProperty("channels")]
	public WsMarketChannel[] Channels { get; set; }
}

class WsMarketUnsubscribeRequest
{
	[JsonProperty("type")]
	public string Type { get; set; } = WsMessageTypes.Unsubscribe;

	[JsonProperty("channels")]
	public WsMarketChannel[] Channels { get; set; }
}

class WsAccountSubscribeRequest
{
	[JsonProperty("type")]
	public string Type { get; set; } = WsMessageTypes.Subscribe;

	[JsonProperty("channels")]
	public string[] Channels { get; set; }
}

class WsMarketChannel
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("instruments")]
	public string[] Instruments { get; set; }

	[JsonProperty("depth", NullValueHandling = NullValueHandling.Ignore)]
	public int? Depth { get; set; }
}

class WsMessage
{
	[JsonProperty("type")]
	public string Type { get; set; }
}

class WsOrderBookMessage : WsMessage
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("bids")]
	public MarketDataEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public MarketDataEntry[] Asks { get; set; }
}

class WsTradeEventMessage : WsMessage
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("trades")]
	public MarketDataEntry[] Trades { get; set; }
}

class WsOrderMessage : WsMessage
{
	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("limit_price")]
	public string LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("unfilled_quantity")]
	public string UnfilledQuantity { get; set; }

	[JsonProperty("matched_quantity")]
	public string MatchedQuantity { get; set; }

	[JsonProperty("cancelled_quantity")]
	public string CancelledQuantity { get; set; }

	[JsonProperty("commission")]
	public string Commission { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }
}

class WsExecutionMessage : WsMessage
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("execution_id")]
	public string ExecutionId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("commission")]
	public string Commission { get; set; }

	[JsonProperty("order_information")]
	public OrderInformation OrderInformation { get; set; }
}

class WsPositionMessage : WsMessage
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("open_quantity")]
	public string OpenQuantity { get; set; }

	[JsonProperty("open_cost")]
	public string OpenCost { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

class WsWalletMessage : WsMessage
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("wallets")]
	public WalletBalance[] Wallets { get; set; }
}

class WsSubscriptionRejectionMessage : WsMessage
{
	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

class WsErrorMessage : WsMessage
{
	[JsonProperty("error_code")]
	public string ErrorCode { get; set; }

	[JsonProperty("error_message")]
	public string ErrorMessage { get; set; }
}
