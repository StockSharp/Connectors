namespace StockSharp.SpGlobal.Native.Model;

sealed class SpGlobalTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }
}

sealed class SpGlobalErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("errors")]
	public SpGlobalError[] Errors { get; set; }

	public string GetMessage()
	{
		var message = Message.IsEmpty(ErrorDescription).IsEmpty(Error);
		if (message.IsEmpty())
			message = Errors?.Select(item => item?.GetMessage())
				.Where(item => !item.IsEmpty()).Join("; ");
		if (!RequestId.IsEmpty())
			message = $"{message} Request ID: {RequestId}.";
		return message;
	}
}

sealed class SpGlobalError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	public string GetMessage() => Message.IsEmpty(Description).IsEmpty(Code);
}
