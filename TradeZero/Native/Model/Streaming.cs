namespace StockSharp.TradeZero.Native.Model;

sealed class TradeZeroSocketSystemMessage
{
	[JsonProperty("@system")]
	public bool IsSystem { get; set; }
	[JsonProperty("ts")]
	public long Timestamp { get; set; }
	public TradeZeroSystemStatuses Status { get; set; }
	public string Message { get; set; }
}

sealed class TradeZeroSocketAuthRequest
{
	public string Key { get; set; }
	public string Secret { get; set; }
}

sealed class TradeZeroPortfolioSubscribeRequest
{
	public string AccountId { get; set; }
	public TradeZeroPortfolioSubscriptions[] Subscriptions { get; set; }
}

sealed class TradeZeroPnlSubscribeRequest
{
	public string Account { get; set; }
}

sealed class TradeZeroPortfolioMessage
{
	[JsonProperty("ts")]
	public long Timestamp { get; set; }
	public string AccountId { get; set; }
	public TradeZeroSocketActions? Action { get; set; }
	public TradeZeroPortfolioSubscriptions? Subscription { get; set; }
	[JsonProperty("requestConfirmed")]
	public bool? IsRequestConfirmed { get; set; }
	public TradeZeroOrder Order { get; set; }
	public TradeZeroPosition Position { get; set; }
}

sealed class TradeZeroPnlMessage
{
	[JsonProperty("ts")]
	public long Timestamp { get; set; }
	public string Account { get; set; }
	public TradeZeroSocketActions? Action { get; set; }
	public TradeZeroPnlTargets? Target { get; set; }
	public TradeZeroPnl PnlReturn { get; set; }
	public TradeZeroPnl AggCalcs { get; set; }
	public TradeZeroPnlPosition Position { get; set; }
	public string Message { get; set; }
}
