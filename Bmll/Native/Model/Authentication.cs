namespace StockSharp.Bmll.Native.Model;

sealed class BmllIdentityRequest
{
	[JsonProperty("iss")]
	public string Issuer { get; init; }
}

sealed class BmllIdentityResponse
{
	[JsonProperty("sid")]
	public string SessionId { get; set; }
}

sealed class BmllJwtHeader
{
	[JsonProperty("alg")]
	public string Algorithm { get; init; } = "RS256";

	[JsonProperty("typ")]
	public string Type { get; init; } = "JWT";
}

class BmllTokenClaims
{
	[JsonProperty("iss")]
	public string Issuer { get; init; }

	[JsonProperty("aud")]
	public string Audience { get; init; }

	[JsonProperty("exp")]
	public long ExpiresAt { get; init; }

	[JsonProperty("sid")]
	public string SessionId { get; init; }
}

sealed class BmllTokenRequest : BmllTokenClaims
{
	[JsonProperty("jws")]
	public string Jws { get; init; }
}

sealed class BmllTokenResponse
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("api-key")]
	public string ApiKey { get; set; }
}
