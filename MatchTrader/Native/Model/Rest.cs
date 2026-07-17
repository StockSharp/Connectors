namespace StockSharp.MatchTrader.Native.Model;

internal sealed class MatchTraderLoginRequest
{
	[JsonProperty("email")]
	public string Email { get; set; }

	[JsonProperty("password")]
	public string Password { get; set; }

	[JsonProperty("brokerId")]
	public string BrokerId { get; set; }
}

internal sealed class MatchTraderLoginResponse
{
	[JsonProperty("email")]
	public string Email { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("tradingAccounts")]
	public MatchTraderAccount[] TradingAccounts { get; set; }

	[JsonProperty("accounts")]
	public MatchTraderAccount[] Accounts { get; set; }

	[JsonProperty("selectedTradingAccount")]
	public MatchTraderAccount SelectedTradingAccount { get; set; }
}

internal sealed class MatchTraderAccount
{
	[JsonProperty("tradingAccountId")]
	public string TradingAccountId { get; set; }

	[JsonProperty("uuid")]
	public string Uuid { get; set; }

	[JsonProperty("tradingApiToken")]
	public string TradingApiToken { get; set; }

	[JsonProperty("offer")]
	public MatchTraderOffer Offer { get; set; }
}

internal sealed class MatchTraderOffer
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("system")]
	public MatchTraderSystem System { get; set; }
}

internal sealed class MatchTraderSystem
{
	[JsonProperty("uuid")]
	public string Uuid { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("tradingApiDomain")]
	public string TradingApiDomain { get; set; }

	[JsonProperty("active")]
	public bool Active { get; set; }
}

internal sealed class MatchTraderInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("alias")]
	public string Alias { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("volumePrecision")]
	public int VolumePrecision { get; set; }

	[JsonProperty("volumeMin")]
	public decimal VolumeMin { get; set; }

	[JsonProperty("volumeMax")]
	public decimal VolumeMax { get; set; }

	[JsonProperty("volumeStep")]
	public decimal VolumeStep { get; set; }

	[JsonProperty("contractSize")]
	public decimal ContractSize { get; set; }

	[JsonProperty("sizeOfOnePoint")]
	public decimal SizeOfOnePoint { get; set; }

	[JsonProperty("sessionOpen")]
	public bool SessionOpen { get; set; }
}

internal sealed class MatchTraderQuote
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("alias")]
	public string Alias { get; set; }

	[JsonProperty("bid")]
	public decimal Bid { get; set; }

	[JsonProperty("ask")]
	public decimal Ask { get; set; }

	[JsonProperty("change")]
	public decimal Change { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("timestampMs")]
	public long TimestampMilliseconds { get; set; }

	[JsonProperty("timestampSec")]
	public long TimestampSeconds { get; set; }
}

internal sealed class MatchTraderCandlesResponse
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("candles")]
	public MatchTraderCandle[] Candles { get; set; }
}

internal sealed class MatchTraderCandle
{
	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

internal sealed class MatchTraderBalance
{
	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("equity")]
	public decimal Equity { get; set; }

	[JsonProperty("freeMargin")]
	public decimal FreeMargin { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("profit")]
	public decimal Profit { get; set; }

	[JsonProperty("netProfit")]
	public decimal NetProfit { get; set; }

	[JsonProperty("credit")]
	public decimal Credit { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}

internal sealed class MatchTraderPositionsResponse
{
	[JsonProperty("positions")]
	public MatchTraderPosition[] Positions { get; set; }
}

internal sealed class MatchTraderPosition
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("alias")]
	public string Alias { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("openTime")]
	public DateTime? OpenTime { get; set; }

	[JsonProperty("openTimeMillis")]
	public long OpenTimeMilliseconds { get; set; }

	[JsonProperty("openPrice")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("stopLoss")]
	public decimal? StopLoss { get; set; }

	[JsonProperty("takeProfit")]
	public decimal? TakeProfit { get; set; }

	[JsonProperty("profit")]
	public decimal Profit { get; set; }

	[JsonProperty("netProfit")]
	public decimal NetProfit { get; set; }

	[JsonProperty("currentPrice")]
	public decimal CurrentPrice { get; set; }
}

internal sealed class MatchTraderOrdersResponse
{
	[JsonProperty("orders")]
	public MatchTraderOrder[] Orders { get; set; }
}

internal sealed class MatchTraderOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("alias")]
	public string Alias { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("creationTime")]
	public DateTime? CreationTime { get; set; }

	[JsonProperty("activationPrice")]
	public decimal ActivationPrice { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("stopLoss")]
	public decimal? StopLoss { get; set; }

	[JsonProperty("takeProfit")]
	public decimal? TakeProfit { get; set; }

	[JsonProperty("comment")]
	public string Comment { get; set; }
}

internal sealed class MatchTraderClosedRequest
{
	[JsonProperty("from")]
	public DateTimeOffset From { get; set; }

	[JsonProperty("to")]
	public DateTimeOffset To { get; set; }
}

internal sealed class MatchTraderClosedResponse
{
	[JsonProperty("operations")]
	public MatchTraderClosedPosition[] Operations { get; set; }
}

internal sealed class MatchTraderClosedPosition
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("uid")]
	public string Uid { get; set; }

	[JsonProperty("closingOrderID")]
	public string ClosingOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("openTime")]
	public DateTime? OpenTime { get; set; }

	[JsonProperty("time")]
	public DateTime? CloseTime { get; set; }

	[JsonProperty("openPrice")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("closePrice")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("profit")]
	public decimal Profit { get; set; }

	[JsonProperty("netProfit")]
	public decimal NetProfit { get; set; }
}

internal class MatchTraderOpenPositionRequest
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("orderSide")]
	public string Side { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("slPrice")]
	public decimal StopLoss { get; set; }

	[JsonProperty("tpPrice")]
	public decimal TakeProfit { get; set; }

	[JsonProperty("isMobile")]
	public bool IsMobile { get; set; }
}

internal sealed class MatchTraderPendingOrderRequest : MatchTraderOpenPositionRequest
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

internal sealed class MatchTraderClosePositionRequest
{
	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("orderSide")]
	public string Side { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("isMobile")]
	public bool IsMobile { get; set; }
}

internal sealed class MatchTraderCancelOrderRequest
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("orderSide")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("isMobile")]
	public bool IsMobile { get; set; }
}

internal sealed class MatchTraderOperationResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("nativeCode")]
	public string NativeCode { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }
}
