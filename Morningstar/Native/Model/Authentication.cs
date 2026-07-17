namespace StockSharp.Morningstar.Native.Model;

sealed class MorningstarTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }
}

sealed class MorningstarErrorResponse
{
	[JsonProperty("statusCode")]
	public string StatusCode { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	public string GetMessage()
	{
		var message = Message.IsEmpty(ErrorCode);
		if (!RequestId.IsEmpty())
			message = $"{message} Request ID: {RequestId}.";
		return message;
	}
}
