namespace StockSharp.TastyTrade.Native.Model;

sealed class TastyDataResponse<T>
{
	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("context")]
	public string Context { get; set; }
}

sealed class TastyItems<T>
{
	[JsonProperty("items")]
	public T[] Items { get; set; }
}

sealed class TastyErrorResponse
{
	[JsonProperty("error")]
	public TastyError Error { get; set; }

	[JsonProperty("errors")]
	public TastyError[] Errors { get; set; }
}

sealed class TastyError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class TastyTokenRequest
{
	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }

	[JsonProperty("client_secret")]
	public string ClientSecret { get; set; }

	[JsonProperty("scope")]
	public string Scope { get; set; }

	[JsonProperty("grant_type")]
	public string GrantType { get; set; }
}

sealed class TastyTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }
}

sealed class TastyQuoteToken
{
	[JsonProperty("dxlink-url")]
	public string DxLinkUrl { get; set; }

	[JsonProperty("websocket-url")]
	public string WebSocketUrl { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("level")]
	public string Level { get; set; }

	[JsonProperty("expires-at")]
	public DateTime? ExpiresAt { get; set; }
}
