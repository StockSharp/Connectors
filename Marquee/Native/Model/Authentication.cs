namespace StockSharp.Marquee.Native.Model;

sealed class MarqueeTokenRequest
{
	public string GrantType { get; init; } = "client_credentials";
	public string ClientId { get; init; }
	public string ClientSecret { get; init; }
	public string Scope { get; init; }

	public HttpContent ToContent()
	{
		static string Encode(string value) => Uri.EscapeDataString(value ?? string.Empty);

		var body = $"grant_type={Encode(GrantType)}&client_id={Encode(ClientId)}" +
			$"&client_secret={Encode(ClientSecret)}&scope={Encode(Scope)}";
		return new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
	}
}

sealed class MarqueeTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }
}

sealed class MarqueeErrorResponse
{
	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }

	[JsonProperty("errorMessages")]
	public string[] ErrorMessages { get; set; }

	public string GetMessage()
		=> ErrorMessages?.Where(message => !message.IsEmpty()).JoinComma()
			.IsEmpty(Message).IsEmpty(ErrorDescription).IsEmpty(Status);
}
