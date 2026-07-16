namespace StockSharp.Kiwoom.Native.Model;

sealed class KiwoomTokenRequest
{
	[JsonProperty("grant_type")]
	public string GrantType { get; set; } = "client_credentials";

	[JsonProperty("appkey")]
	public string AppKey { get; set; }

	[JsonProperty("secretkey")]
	public string SecretKey { get; set; }
}

sealed class KiwoomTokenResponse
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_dt")]
	public string ExpiresAt { get; set; }

	[JsonProperty("return_code")]
	public int ReturnCode { get; set; }

	[JsonProperty("return_msg")]
	public string ReturnMessage { get; set; }
}

abstract class KiwoomResponse
{
	[JsonProperty("return_code")]
	public int ReturnCode { get; set; }

	[JsonProperty("return_msg")]
	public string ReturnMessage { get; set; }

	public bool IsSuccess => ReturnCode == 0;
}

sealed class KiwoomErrorResponse : KiwoomResponse
{
}

sealed record KiwoomQuoteSnapshot(
	decimal? LastPrice,
	decimal? OpenPrice,
	decimal? HighPrice,
	decimal? LowPrice,
	decimal? PreviousClose,
	decimal? Volume,
	decimal? Turnover,
	decimal? BidPrice,
	decimal? BidVolume,
	decimal? AskPrice,
	decimal? AskVolume,
	DateTime ServerTime);

sealed record KiwoomSecurityDefinition(
	KiwoomSecurityInfo Security,
	string Name,
	string ShortName,
	SecurityTypes SecurityType);

sealed record KiwoomDepthSnapshot(
	decimal[] BidPrices,
	decimal[] BidVolumes,
	decimal[] AskPrices,
	decimal[] AskVolumes,
	DateTime ServerTime);

sealed record KiwoomCandleBar(
	DateTime OpenTime,
	decimal Open,
	decimal High,
	decimal Low,
	decimal Close,
	decimal Volume,
	decimal? Turnover);

sealed record KiwoomPosition(
	KiwoomSecurityInfo Security,
	decimal Quantity,
	decimal? AveragePrice,
	decimal? CurrentPrice,
	decimal? UnrealizedPnL,
	decimal? MarketValue);

sealed record KiwoomOrderExecution(
	string OrderNumber,
	string OriginalOrderNumber,
	KiwoomSecurityInfo Security,
	Sides Side,
	decimal OrderQuantity,
	decimal? OrderPrice,
	decimal FilledQuantity,
	decimal? AveragePrice,
	decimal Balance,
	DateTime Time,
	string Status,
	string TradeNumber);

sealed record KiwoomRestPage<T>(T Body, string Continuation, string NextKey);
