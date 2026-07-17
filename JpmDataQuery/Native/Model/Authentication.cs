namespace StockSharp.JpmDataQuery.Native.Model;

sealed class JpmDataQueryTokenRequest
{
	public string GrantType { get; init; } = "client_credentials";
	public string ClientId { get; init; }
	public string ClientSecret { get; init; }
	public string Audience { get; init; }

	public HttpContent ToContent()
	{
		static string Encode(string value) => Uri.EscapeDataString(value ?? string.Empty);

		var body = $"grant_type={Encode(GrantType)}&client_id={Encode(ClientId)}" +
			$"&client_secret={Encode(ClientSecret)}&aud={Encode(Audience)}";
		return new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
	}
}

sealed class JpmDataQueryTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }
}
