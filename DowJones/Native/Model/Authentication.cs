namespace StockSharp.DowJones.Native.Model;

sealed class DowJonesPasswordGrantRequest
{
	[JsonProperty("client_id")]
	public string ClientId { get; init; }

	[JsonProperty("connection")]
	public string Connection { get; init; } = "service-account";

	[JsonProperty("device")]
	public string Device { get; init; } = "orion-tablet";

	[JsonProperty("grant_type")]
	public string GrantType { get; init; } = "password";

	[JsonProperty("username")]
	public string Username { get; init; }

	[JsonProperty("password")]
	public string Password { get; init; }

	[JsonProperty("scope")]
	public string Scope { get; init; } = "openid service_account_id offline_access";
}

sealed class DowJonesJwtGrantRequest
{
	[JsonProperty("assertion")]
	public string Assertion { get; init; }

	[JsonProperty("client_id")]
	public string ClientId { get; init; }

	[JsonProperty("grant_type")]
	public string GrantType { get; init; } =
		"urn:ietf:params:oauth:grant-type:jwt-bearer";

	[JsonProperty("scope")]
	public string Scope { get; init; } = "openid pib";
}

sealed class DowJonesRefreshGrantRequest
{
	[JsonProperty("client_id")]
	public string ClientId { get; init; }

	[JsonProperty("grant_type")]
	public string GrantType { get; init; } = "refresh_token";

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; init; }
}

sealed class DowJonesIdentityTokenResponse
{
	[JsonProperty("id_token")]
	public string IdToken { get; set; }

	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }
}

sealed class DowJonesAccessTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }
}
