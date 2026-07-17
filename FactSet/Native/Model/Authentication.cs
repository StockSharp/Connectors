namespace StockSharp.FactSet.Native.Model;

sealed class FactSetOAuthConfiguration
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("clientAuthType")]
	public string ClientAuthType { get; set; }

	[JsonProperty("owners")]
	public string[] Owners { get; set; }

	[JsonProperty("jwk")]
	public FactSetJsonWebKey Jwk { get; set; }
}

sealed class FactSetJsonWebKey
{
	[JsonProperty("kty")]
	public string KeyType { get; set; }

	[JsonProperty("use")]
	public string Use { get; set; }

	[JsonProperty("alg")]
	public string Algorithm { get; set; }

	[JsonProperty("kid")]
	public string KeyId { get; set; }

	[JsonProperty("n")]
	public string Modulus { get; set; }

	[JsonProperty("e")]
	public string Exponent { get; set; }

	[JsonProperty("d")]
	public string D { get; set; }

	[JsonProperty("p")]
	public string P { get; set; }

	[JsonProperty("q")]
	public string Q { get; set; }

	[JsonProperty("dp")]
	public string DP { get; set; }

	[JsonProperty("dq")]
	public string DQ { get; set; }

	[JsonProperty("qi")]
	public string InverseQ { get; set; }

	public RSAParameters ToParameters()
		=> new()
		{
			Modulus = Decode(Modulus),
			Exponent = Decode(Exponent),
			D = Decode(D),
			P = Decode(P),
			Q = Decode(Q),
			DP = Decode(DP),
			DQ = Decode(DQ),
			InverseQ = Decode(InverseQ),
		};

	private static byte[] Decode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Replace('-', '+').Replace('_', '/');
		value += (value.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
		return Convert.FromBase64String(value);
	}
}

sealed class FactSetOpenIdConfiguration
{
	[JsonProperty("issuer")]
	public string Issuer { get; set; }

	[JsonProperty("token_endpoint")]
	public string TokenEndpoint { get; set; }
}

sealed class FactSetJwtHeader
{
	[JsonProperty("alg")]
	public string Algorithm { get; init; } = "RS256";

	[JsonProperty("kid")]
	public string KeyId { get; init; }

	[JsonProperty("typ")]
	public string Type { get; init; } = "JWT";
}

sealed class FactSetJwtPayload
{
	[JsonProperty("iss")]
	public string Issuer { get; init; }

	[JsonProperty("sub")]
	public string Subject { get; init; }

	[JsonProperty("aud")]
	public string Audience { get; init; }

	[JsonProperty("jti")]
	public string JwtId { get; init; }

	[JsonProperty("nbf")]
	public long NotBefore { get; init; }

	[JsonProperty("exp")]
	public long Expires { get; init; }

	[JsonProperty("iat")]
	public long IssuedAt { get; init; }
}

sealed class FactSetTokenRequest
{
	public string ClientId { get; init; }
	public string ClientAssertion { get; init; }

	public HttpContent ToContent()
	{
		static string Encode(string value) => Uri.EscapeDataString(value ?? string.Empty);
		const string assertionType =
			"urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
		var body = $"grant_type=client_credentials&client_id={Encode(ClientId)}" +
			$"&client_assertion_type={Encode(assertionType)}" +
			$"&client_assertion={Encode(ClientAssertion)}";
		return new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
	}
}

sealed class FactSetTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }
}
