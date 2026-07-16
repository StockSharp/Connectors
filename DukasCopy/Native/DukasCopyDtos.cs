namespace StockSharp.DukasCopy.Native;

internal static class DukasCopyBridgeCommands
{
	public const string Connect = "connect";
	public const string Disconnect = "disconnect";
	public const string Instruments = "instruments";
	public const string Subscribe = "subscribe";
	public const string Unsubscribe = "unsubscribe";
	public const string HistoryTicks = "history_ticks";
	public const string HistoryBars = "history_bars";
	public const string PlaceOrder = "place_order";
	public const string ReplaceOrder = "replace_order";
	public const string CancelOrder = "cancel_order";
	public const string Orders = "orders";
	public const string Account = "account";
}

internal static class DukasCopyBridgeKinds
{
	public const string Response = "response";
	public const string Tick = "tick";
	public const string Bar = "bar";
	public const string Order = "order";
	public const string Account = "account";
	public const string Error = "error";
}

internal sealed class DukasCopyBridgeRequest
{
	[JsonProperty("request_id")]
	public long RequestId { get; set; }

	[JsonProperty("command")]
	public string Command { get; set; }

	[JsonProperty("user_name")]
	public string UserName { get; set; }

	[JsonProperty("password")]
	public string Password { get; set; }

	[JsonProperty("is_demo")]
	public bool? IsDemo { get; set; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("period")]
	public string Period { get; set; }

	[JsonProperty("from")]
	public long? From { get; set; }

	[JsonProperty("to")]
	public long? To { get; set; }

	[JsonProperty("count")]
	public int? Count { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("label")]
	public string Label { get; set; }

	[JsonProperty("order_command")]
	public string OrderCommand { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("slippage")]
	public decimal? Slippage { get; set; }

	[JsonProperty("stop_loss_price")]
	public decimal? StopLossPrice { get; set; }

	[JsonProperty("take_profit_price")]
	public decimal? TakeProfitPrice { get; set; }

	[JsonProperty("good_till_time")]
	public long? GoodTillTime { get; set; }

	[JsonProperty("comment")]
	public string Comment { get; set; }
}

internal sealed class DukasCopyBridgeMessage
{
	[JsonProperty("kind")]
	public string Kind { get; set; }

	[JsonProperty("request_id")]
	public long RequestId { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("instruments")]
	public DukasCopyInstrument[] Instruments { get; set; }

	[JsonProperty("ticks")]
	public DukasCopyTick[] Ticks { get; set; }

	[JsonProperty("bars")]
	public DukasCopyBar[] Bars { get; set; }

	[JsonProperty("orders")]
	public DukasCopyOrder[] Orders { get; set; }

	[JsonProperty("tick")]
	public DukasCopyTick Tick { get; set; }

	[JsonProperty("bar")]
	public DukasCopyBar Bar { get; set; }

	[JsonProperty("order")]
	public DukasCopyOrder Order { get; set; }

	[JsonProperty("account")]
	public DukasCopyAccount Account { get; set; }
}

internal sealed class DukasCopyInstrument
{
	[JsonProperty("symbol")] public string Symbol { get; set; }
	[JsonProperty("name")] public string Name { get; set; }
	[JsonProperty("type")] public string Type { get; set; }
	[JsonProperty("primary_currency")] public string PrimaryCurrency { get; set; }
	[JsonProperty("secondary_currency")] public string SecondaryCurrency { get; set; }
	[JsonProperty("pip_value")] public decimal PipValue { get; set; }
	[JsonProperty("pip_scale")] public int PipScale { get; set; }
	[JsonProperty("tick_scale")] public int TickScale { get; set; }
	[JsonProperty("min_trade_amount")] public decimal MinTradeAmount { get; set; }
}

internal sealed class DukasCopyTick
{
	[JsonProperty("symbol")] public string Symbol { get; set; }
	[JsonProperty("time")] public long Time { get; set; }
	[JsonProperty("ask")] public decimal Ask { get; set; }
	[JsonProperty("bid")] public decimal Bid { get; set; }
	[JsonProperty("ask_volume")] public decimal AskVolume { get; set; }
	[JsonProperty("bid_volume")] public decimal BidVolume { get; set; }
	[JsonProperty("ask_prices")] public decimal[] AskPrices { get; set; }
	[JsonProperty("ask_volumes")] public decimal[] AskVolumes { get; set; }
	[JsonProperty("bid_prices")] public decimal[] BidPrices { get; set; }
	[JsonProperty("bid_volumes")] public decimal[] BidVolumes { get; set; }
	[JsonProperty("total_ask_volume")] public decimal TotalAskVolume { get; set; }
	[JsonProperty("total_bid_volume")] public decimal TotalBidVolume { get; set; }
}

internal sealed class DukasCopyBar
{
	[JsonProperty("symbol")] public string Symbol { get; set; }
	[JsonProperty("period")] public string Period { get; set; }
	[JsonProperty("time")] public long Time { get; set; }
	[JsonProperty("bid_open")] public decimal BidOpen { get; set; }
	[JsonProperty("bid_high")] public decimal BidHigh { get; set; }
	[JsonProperty("bid_low")] public decimal BidLow { get; set; }
	[JsonProperty("bid_close")] public decimal BidClose { get; set; }
	[JsonProperty("bid_volume")] public decimal BidVolume { get; set; }
	[JsonProperty("ask_open")] public decimal AskOpen { get; set; }
	[JsonProperty("ask_high")] public decimal AskHigh { get; set; }
	[JsonProperty("ask_low")] public decimal AskLow { get; set; }
	[JsonProperty("ask_close")] public decimal AskClose { get; set; }
	[JsonProperty("ask_volume")] public decimal AskVolume { get; set; }
}

internal sealed class DukasCopyOrder
{
	[JsonProperty("id")] public string Id { get; set; }
	[JsonProperty("label")] public string Label { get; set; }
	[JsonProperty("symbol")] public string Symbol { get; set; }
	[JsonProperty("command")] public string Command { get; set; }
	[JsonProperty("state")] public string State { get; set; }
	[JsonProperty("amount")] public decimal Amount { get; set; }
	[JsonProperty("requested_amount")] public decimal RequestedAmount { get; set; }
	[JsonProperty("filled_amount")] public decimal FilledAmount { get; set; }
	[JsonProperty("open_price")] public decimal OpenPrice { get; set; }
	[JsonProperty("close_price")] public decimal ClosePrice { get; set; }
	[JsonProperty("creation_time")] public long CreationTime { get; set; }
	[JsonProperty("fill_time")] public long FillTime { get; set; }
	[JsonProperty("close_time")] public long CloseTime { get; set; }
	[JsonProperty("stop_loss_price")] public decimal StopLossPrice { get; set; }
	[JsonProperty("take_profit_price")] public decimal TakeProfitPrice { get; set; }
	[JsonProperty("good_till_time")] public long GoodTillTime { get; set; }
	[JsonProperty("profit_loss")] public decimal ProfitLoss { get; set; }
	[JsonProperty("comment")] public string Comment { get; set; }
	[JsonProperty("message")] public string Message { get; set; }
}

internal sealed class DukasCopyAccount
{
	[JsonProperty("account_id")] public string AccountId { get; set; }
	[JsonProperty("user_name")] public string UserName { get; set; }
	[JsonProperty("currency")] public string Currency { get; set; }
	[JsonProperty("balance")] public decimal Balance { get; set; }
	[JsonProperty("equity")] public decimal Equity { get; set; }
	[JsonProperty("used_margin")] public decimal UsedMargin { get; set; }
	[JsonProperty("use_of_leverage")] public decimal UseOfLeverage { get; set; }
	[JsonProperty("credit_line")] public decimal CreditLine { get; set; }
}
