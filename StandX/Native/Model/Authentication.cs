namespace StockSharp.StandX.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXPrepareSignInRequest
{
	[JsonProperty("address", Required = Required.Always)]
	public string Address { get; set; }

	[JsonProperty("requestId", Required = Required.Always)]
	public string RequestId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXPrepareSignInResponse
{
	[JsonProperty("success", Required = Required.Always)]
	public bool IsSuccess { get; set; }

	[JsonProperty("signedData")]
	public string SignedData { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXJwtHeader
{
	[JsonProperty("alg", Required = Required.Always)]
	public string Algorithm { get; set; }

	[JsonProperty("kid", Required = Required.Always)]
	public string KeyId { get; set; }

	[JsonProperty("typ")]
	public string Type { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXSignedData
{
	[JsonProperty("domain", Required = Required.Always)]
	public string Domain { get; set; }

	[JsonProperty("uri", Required = Required.Always)]
	public string Uri { get; set; }

	[JsonProperty("statement")]
	public string Statement { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("chainId")]
	public int? ChainId { get; set; }

	[JsonProperty("nonce")]
	public string Nonce { get; set; }

	[JsonProperty("address", Required = Required.Always)]
	public string Address { get; set; }

	[JsonProperty("requestId", Required = Required.Always)]
	public string RequestId { get; set; }

	[JsonProperty("issuedAt")]
	public string IssuedAt { get; set; }

	[JsonProperty("message", Required = Required.Always)]
	public string Message { get; set; }

	[JsonProperty("exp", Required = Required.Always)]
	public long ExpiresAt { get; set; }

	[JsonProperty("iat", Required = Required.Always)]
	public long IssuedAtUnix { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXJsonWebKeySet
{
	[JsonProperty("keys", Required = Required.Always)]
	public StandXJsonWebKey[] Keys { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXJsonWebKey
{
	[JsonProperty("kty", Required = Required.Always)]
	public string KeyType { get; set; }

	[JsonProperty("use")]
	public string Usage { get; set; }

	[JsonProperty("alg", Required = Required.Always)]
	public string Algorithm { get; set; }

	[JsonProperty("crv", Required = Required.Always)]
	public string Curve { get; set; }

	[JsonProperty("x", Required = Required.Always)]
	public string X { get; set; }

	[JsonProperty("y", Required = Required.Always)]
	public string Y { get; set; }

	[JsonProperty("kid", Required = Required.Always)]
	public string KeyId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXLoginRequest
{
	[JsonProperty("signature", Required = Required.Always)]
	public string Signature { get; set; }

	[JsonProperty("signedData", Required = Required.Always)]
	public string SignedData { get; set; }

	[JsonProperty("expiresSeconds", Required = Required.Always)]
	public int ExpiresSeconds { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXLoginResponse
{
	[JsonProperty("token", Required = Required.Always)]
	public string Token { get; set; }

	[JsonProperty("address", Required = Required.Always)]
	public string Address { get; set; }

	[JsonProperty("alias")]
	public string Alias { get; set; }

	[JsonProperty("chain", Required = Required.Always)]
	public string Chain { get; set; }

	[JsonProperty("perpsAlpha")]
	public bool? IsPerpsAlpha { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXSolanaLoginEnvelope
{
	[JsonProperty("input", Required = Required.Always)]
	public StandXSignedData Input { get; set; }

	[JsonProperty("output", Required = Required.Always)]
	public StandXSolanaLoginOutput Output { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXSolanaLoginOutput
{
	[JsonProperty("signedMessage", Required = Required.Always)]
	public int[] SignedMessage { get; set; }

	[JsonProperty("signature", Required = Required.Always)]
	public int[] Signature { get; set; }

	[JsonProperty("account", Required = Required.Always)]
	public StandXSolanaLoginAccount Account { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXSolanaLoginAccount
{
	[JsonProperty("publicKey", Required = Required.Always)]
	public int[] PublicKey { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXRequestSignature
{
	[JsonProperty("x-request-id", Required = Required.Always)]
	public string RequestId { get; set; }

	[JsonProperty("x-request-timestamp", Required = Required.Always)]
	public string Timestamp { get; set; }

	[JsonProperty("x-request-signature", Required = Required.Always)]
	public string Signature { get; set; }

	[JsonProperty("x-request-sign-version", Required = Required.Always)]
	public string Version { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXError
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }
}
