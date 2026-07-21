namespace StockSharp.FalconX.Native.Model;

sealed class FalconXTokenPair
{
	[JsonProperty("base_token")]
	public string BaseToken { get; init; }

	[JsonProperty("quote_token")]
	public string QuoteToken { get; init; }
}

sealed class FalconXQuantity
{
	[JsonProperty("token")]
	public string Token { get; init; }

	[JsonProperty("value")]
	public decimal Value { get; init; }
}

sealed class FalconXApiError
{
	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("error_code")]
	public string ErrorCode { get; init; }

	[JsonProperty("reason")]
	public string Reason { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("errors")]
	public FalconXApiError Details { get; init; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; init; }
}

sealed class FalconXApiErrorEnvelope
{
	[JsonProperty("error")]
	public FalconXApiError Error { get; init; }

	[JsonProperty("errors")]
	public FalconXApiError Errors { get; init; }

	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("reason")]
	public string Reason { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}
