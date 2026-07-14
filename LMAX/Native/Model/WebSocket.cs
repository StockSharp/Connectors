namespace StockSharp.LMAX.Native.Model;

class WsSubscribeRequest
{
	[JsonProperty("type")]
	public string Type { get; set; } = "SUBSCRIBE";

	[JsonProperty("channels")]
	public WsChannel[] Channels { get; set; }
}

class WsUnsubscribeRequest
{
	[JsonProperty("type")]
	public string Type { get; set; } = "UNSUBSCRIBE";

	[JsonProperty("channels")]
	public WsChannel[] Channels { get; set; }
}

class WsChannel
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("instruments")]
	public string[] Instruments { get; set; }
}

class WsMessage
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }
}

class WsOrderBookMessage : WsMessage
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("bids")]
	public MarketDataEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public MarketDataEntry[] Asks { get; set; }
}

class WsTradeMessage : WsMessage
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("taker_side")]
	public string TakerSide { get; set; }
}

class WsTickerMessage : WsMessage
{
	[JsonProperty("best_bid")]
	public double? BestBid { get; set; }

	[JsonProperty("best_ask")]
	public double? BestAsk { get; set; }

	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }

	[JsonProperty("last_quantity")]
	public double? LastQuantity { get; set; }

	[JsonProperty("session_open")]
	public double? SessionOpen { get; set; }

	[JsonProperty("session_high")]
	public double? SessionHigh { get; set; }

	[JsonProperty("session_low")]
	public double? SessionLow { get; set; }

	[JsonProperty("daily_volume")]
	public double? DailyVolume { get; set; }
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

	[JsonProperty("limit_price")]
	public double? LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public double? StopPrice { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("unfilled_quantity")]
	public double? UnfilledQuantity { get; set; }

	[JsonProperty("matched_quantity")]
	public double? MatchedQuantity { get; set; }

	[JsonProperty("cancelled_quantity")]
	public double? CancelledQuantity { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }
}

class WsExecutionMessage : WsMessage
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("liquidity")]
	public string Liquidity { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }
}

class WsPositionMessage : WsMessage
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("open_quantity")]
	public double? OpenQuantity { get; set; }

	[JsonProperty("cumulative_cost")]
	public double? CumulativeCost { get; set; }

	[JsonProperty("open_cost")]
	public double? OpenCost { get; set; }

	[JsonProperty("unrealised_pnl")]
	public double? UnrealisedPnl { get; set; }
}

class WsWalletMessage : WsMessage
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public double? Balance { get; set; }

	[JsonProperty("available_to_trade")]
	public double? AvailableToTrade { get; set; }

	[JsonProperty("available_to_withdraw")]
	public string AvailableToWithdraw { get; set; }

	[JsonProperty("unrealised_pnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("margin")]
	public double? Margin { get; set; }
}

class WsRejectionMessage : WsMessage
{
	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("rejection_reason")]
	public string RejectionReason { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

class WsHeartbeatMessage : WsMessage
{
}

class WsErrorMessage
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("error_code")]
	public string ErrorCode { get; set; }

	[JsonProperty("error_message")]
	public string ErrorMessage { get; set; }
}

class WsSubscriptionConfirmation
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("channels")]
	public WsChannel[] Channels { get; set; }
}
