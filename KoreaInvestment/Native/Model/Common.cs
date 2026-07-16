namespace StockSharp.KoreaInvestment.Native.Model;

sealed class KisTokenRequest
{
	[JsonProperty("grant_type")]
	public string GrantType { get; set; } = "client_credentials";

	[JsonProperty("appkey")]
	public string AppKey { get; set; }

	[JsonProperty("appsecret")]
	public string AppSecret { get; set; }
}

sealed class KisTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("access_token_token_expired")]
	public string ExpiresAt { get; set; }
}

sealed class KisApprovalRequest
{
	[JsonProperty("grant_type")]
	public string GrantType { get; set; } = "client_credentials";

	[JsonProperty("appkey")]
	public string AppKey { get; set; }

	[JsonProperty("secretkey")]
	public string SecretKey { get; set; }
}

sealed class KisApprovalResponse
{
	[JsonProperty("approval_key")]
	public string ApprovalKey { get; set; }
}

abstract class KisResponse
{
	[JsonProperty("rt_cd")]
	public string ReturnCode { get; set; }

	[JsonProperty("msg_cd")]
	public string MessageCode { get; set; }

	[JsonProperty("msg1")]
	public string Message { get; set; }

	public bool IsSuccess => ReturnCode == "0";
}

sealed class KisErrorResponse : KisResponse
{
}

sealed class KisOrderResponse : KisResponse
{
	[JsonProperty("output")]
	public KisOrderResult Output { get; set; }
}

sealed class KisOrderResult
{
	[JsonProperty("KRX_FWDG_ORD_ORGNO")]
	public string OrganizationNumber { get; set; }

	[JsonProperty("ODNO")]
	public string OrderNumber { get; set; }

	[JsonProperty("ORD_TMD")]
	public string OrderTime { get; set; }
}

sealed record KisQuoteSnapshot(
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
	decimal? OpenInterest,
	DateTime ServerTime);

sealed record KisCandleBar(
	DateTime OpenTime,
	decimal Open,
	decimal High,
	decimal Low,
	decimal Close,
	decimal Volume,
	decimal? Turnover);

sealed record KisPosition(
	KisSecurityInfo Security,
	decimal Quantity,
	decimal? AveragePrice,
	decimal? CurrentPrice,
	decimal? UnrealizedPnL,
	decimal? MarketValue,
	string Currency);

sealed record KisOrderExecution(
	string OrderNumber,
	string OriginalOrderNumber,
	KisSecurityInfo Security,
	Sides Side,
	decimal OrderQuantity,
	decimal? OrderPrice,
	decimal FilledQuantity,
	decimal? AveragePrice,
	DateTime Time,
	bool IsCanceled,
	string Name);
