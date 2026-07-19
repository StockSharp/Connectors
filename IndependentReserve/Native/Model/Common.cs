namespace StockSharp.IndependentReserve.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum IndependentReserveOrderTypes
{
	MarketBid,
	MarketOffer,
	LimitBid,
	LimitOffer,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndependentReserveOrderStatuses
{
	Open,
	Filled,
	PartiallyFilled,
	PartiallyFilledAndCancelled,
	Cancelled,
	Expired,
	PartiallyFilledAndExpired,
	Failed,
	PartiallyFilledAndFailed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndependentReserveTimeInForce
{
	Gtc,
	Ioc,
	Fok,
	Moc,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndependentReserveVolumeCurrencyTypes
{
	Primary,
	Secondary,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndependentReserveTakers
{
	Bid,
	Offer,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndependentReserveTradeSides
{
	Unknown,
	Taker,
	Maker,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndependentReserveAccountStatuses
{
	Active,
	Inactive,
}

sealed class IndependentReserveErrorResponse
{
	[JsonProperty("Message")]
	public string Message { get; init; }

	[JsonProperty("ErrorCode")]
	public string ErrorCode { get; init; }
}

sealed class IndependentReserveApiException : InvalidOperationException
{
	public IndependentReserveApiException(HttpStatusCode statusCode,
		string errorCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
		ErrorCode = errorCode;
	}

	public HttpStatusCode StatusCode { get; }
	public string ErrorCode { get; }
}
