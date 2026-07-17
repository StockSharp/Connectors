namespace StockSharp.Qmt.Native.Model;

internal static class QmtGatewayProtocol
{
	public const int Version = 1;
	public const int MaxFrameSize = 16 * 1024 * 1024;
}

internal static class QmtGatewayKinds
{
	public const string Hello = "hello";
	public const string Ping = "ping";
	public const string Search = "search";
	public const string Security = "security";
	public const string Subscribe = "subscribe";
	public const string Unsubscribe = "unsubscribe";
	public const string History = "history";
	public const string Accounts = "accounts";
	public const string Positions = "positions";
	public const string Orders = "orders";
	public const string Fills = "fills";
	public const string PlaceOrder = "place_order";
	public const string CancelOrder = "cancel_order";
	public const string Level1 = "level1";
	public const string Depth = "depth";
	public const string Trade = "trade";
	public const string Candle = "candle";
	public const string Order = "order";
	public const string Fill = "fill";
	public const string Asset = "asset";
	public const string Position = "position";
	public const string Connection = "connection";
	public const string Error = "error";
}

internal sealed class QmtGatewayEnvelope
{
	[JsonProperty("version")]
	public int Version { get; set; } = QmtGatewayProtocol.Version;

	[JsonProperty("kind")]
	public string Kind { get; set; }

	[JsonProperty("request_id")]
	public long RequestId { get; set; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("error")]
	public QmtGatewayError Error { get; set; }

	[JsonProperty("hello_request")]
	public QmtHelloRequest HelloRequest { get; set; }

	[JsonProperty("search_request")]
	public QmtSearchRequest SearchRequest { get; set; }

	[JsonProperty("security_request")]
	public QmtSecurityRequest SecurityRequest { get; set; }

	[JsonProperty("subscription_request")]
	public QmtSubscriptionRequest SubscriptionRequest { get; set; }

	[JsonProperty("history_request")]
	public QmtHistoryRequest HistoryRequest { get; set; }

	[JsonProperty("order_request")]
	public QmtOrderRequest OrderRequest { get; set; }

	[JsonProperty("cancel_request")]
	public QmtCancelRequest CancelRequest { get; set; }

	[JsonProperty("hello")]
	public QmtHello Hello { get; set; }

	[JsonProperty("securities")]
	public QmtSecurity[] Securities { get; set; }

	[JsonProperty("subscription")]
	public QmtSubscriptionResult Subscription { get; set; }

	[JsonProperty("candles")]
	public QmtCandle[] Candles { get; set; }

	[JsonProperty("accounts")]
	public QmtAccount[] Accounts { get; set; }

	[JsonProperty("assets")]
	public QmtAsset[] Assets { get; set; }

	[JsonProperty("positions")]
	public QmtPosition[] Positions { get; set; }

	[JsonProperty("orders")]
	public QmtOrder[] Orders { get; set; }

	[JsonProperty("fills")]
	public QmtFill[] Fills { get; set; }

	[JsonProperty("order_id")]
	public long? OrderId { get; set; }

	[JsonProperty("subscription_id")]
	public long SubscriptionId { get; set; }

	[JsonProperty("quote")]
	public QmtQuote Quote { get; set; }

	[JsonProperty("trade")]
	public QmtMarketTrade Trade { get; set; }

	[JsonProperty("candle")]
	public QmtCandle Candle { get; set; }

	[JsonProperty("order")]
	public QmtOrder Order { get; set; }

	[JsonProperty("fill")]
	public QmtFill Fill { get; set; }

	[JsonProperty("asset")]
	public QmtAsset Asset { get; set; }

	[JsonProperty("position")]
	public QmtPosition Position { get; set; }

	[JsonProperty("connection")]
	public QmtConnection Connection { get; set; }
}

internal sealed class QmtGatewayError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

internal sealed class QmtHelloRequest
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("client")]
	public string Client { get; set; }
}

internal sealed class QmtHello
{
	[JsonProperty("gateway_version")]
	public string GatewayVersion { get; set; }

	[JsonProperty("xtquant_version")]
	public string XtQuantVersion { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("account_type")]
	public string AccountType { get; set; }
}

internal sealed class QmtSearchRequest
{
	[JsonProperty("query")]
	public string Query { get; set; }

	[JsonProperty("markets")]
	public string[] Markets { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }
}

internal sealed class QmtSecurityRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

internal sealed class QmtSubscriptionRequest
{
	[JsonProperty("subscription_id")]
	public long SubscriptionId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("data_kind")]
	public string DataKind { get; set; }

	[JsonProperty("period")]
	public string Period { get; set; }
}

internal sealed class QmtSubscriptionResult
{
	[JsonProperty("subscription_id")]
	public long SubscriptionId { get; set; }

	[JsonProperty("native_id")]
	public long NativeId { get; set; }
}

internal sealed class QmtHistoryRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("period")]
	public string Period { get; set; }

	[JsonProperty("from")]
	public long? From { get; set; }

	[JsonProperty("to")]
	public long? To { get; set; }

	[JsonProperty("count")]
	public int Count { get; set; }
}

internal sealed class QmtOrderRequest
{
	[JsonProperty("client_order_id")]
	public long ClientOrderId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("volume")]
	public long Volume { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }
}

internal sealed class QmtCancelRequest
{
	[JsonProperty("client_order_id")]
	public long ClientOrderId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }
}

internal sealed class QmtSecurity
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("security_type")]
	public string SecurityType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("price_step")]
	public decimal? PriceStep { get; set; }

	[JsonProperty("volume_step")]
	public decimal? VolumeStep { get; set; }

	[JsonProperty("multiplier")]
	public decimal? Multiplier { get; set; }

	[JsonProperty("expiry")]
	public long? Expiry { get; set; }

	[JsonProperty("is_trading")]
	public bool? IsTrading { get; set; }
}

internal sealed class QmtPriceLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

internal sealed class QmtQuote
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("last_price")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("open_price")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("high_price")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low_price")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("previous_close")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("settlement_price")]
	public decimal? SettlementPrice { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("turnover")]
	public decimal? Turnover { get; set; }

	[JsonProperty("open_interest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("bids")]
	public QmtPriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public QmtPriceLevel[] Asks { get; set; }
}

internal sealed class QmtMarketTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

internal sealed class QmtCandle
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("period")]
	public string Period { get; set; }

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

	[JsonProperty("turnover")]
	public decimal? Turnover { get; set; }

	[JsonProperty("open_interest")]
	public decimal? OpenInterest { get; set; }
}

internal sealed class QmtAccount
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("account_type")]
	public string AccountType { get; set; }
}

internal sealed class QmtAsset
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("cash")]
	public decimal Cash { get; set; }

	[JsonProperty("frozen_cash")]
	public decimal FrozenCash { get; set; }

	[JsonProperty("market_value")]
	public decimal MarketValue { get; set; }

	[JsonProperty("total_asset")]
	public decimal TotalAsset { get; set; }
}

internal sealed class QmtPosition
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("available_volume")]
	public decimal AvailableVolume { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("market_value")]
	public decimal MarketValue { get; set; }
}

internal sealed class QmtOrder
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("order_sys_id")]
	public string OrderSystemId { get; set; }

	[JsonProperty("client_order_id")]
	public long ClientOrderId { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("filled_volume")]
	public decimal FilledVolume { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("status_message")]
	public string StatusMessage { get; set; }
}

internal sealed class QmtFill
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("order_sys_id")]
	public string OrderSystemId { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

internal sealed class QmtConnection
{
	[JsonProperty("is_connected")]
	public bool IsConnected { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
