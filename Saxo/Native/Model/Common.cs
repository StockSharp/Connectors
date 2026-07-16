namespace StockSharp.Saxo.Native.Model;

sealed class SaxoError
{
	[JsonProperty("ErrorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("Message")]
	public string Message { get; set; }
}

class SaxoFeed<T>
{
	[JsonProperty("Data")]
	public T[] Data { get; set; }

	[JsonProperty("__next")]
	public string Next { get; set; }

	[JsonProperty("__nextPoll")]
	public string NextPoll { get; set; }

	[JsonProperty("__count")]
	public long? Count { get; set; }
}

sealed class SaxoSubscriptionResponse<T>
{
	[JsonProperty("ContextId")]
	public string ContextId { get; set; }

	[JsonProperty("ReferenceId")]
	public string ReferenceId { get; set; }

	[JsonProperty("RefreshRate")]
	public int RefreshRate { get; set; }

	[JsonProperty("InactivityTimeout")]
	public int InactivityTimeout { get; set; }

	[JsonProperty("State")]
	public string State { get; set; }

	[JsonProperty("Snapshot")]
	public T Snapshot { get; set; }
}

abstract class SaxoSubscriptionRequest
{
	[JsonProperty("ContextId")]
	public string ContextId { get; set; }

	[JsonProperty("ReferenceId")]
	public string ReferenceId { get; set; }

	[JsonProperty("RefreshRate", NullValueHandling = NullValueHandling.Ignore)]
	public int? RefreshRate { get; set; }

	[JsonProperty("Format")]
	public string Format { get; set; } = "application/json";

	[JsonProperty("ReplaceReferenceId", NullValueHandling = NullValueHandling.Ignore)]
	public string ReplaceReferenceId { get; set; }
}

sealed class SaxoJwtPayload
{
	[JsonProperty("exp")]
	public long Expiration { get; set; }
}

sealed class SaxoTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("refresh_token_expires_in")]
	public int RefreshTokenExpiresIn { get; set; }
}

sealed class SaxoUser
{
	[JsonProperty("ClientKey")]
	public string ClientKey { get; set; }

	[JsonProperty("UserId")]
	public string UserId { get; set; }

	[JsonProperty("MarketDataViaOpenApiTermsAccepted")]
	public bool MarketDataTermsAccepted { get; set; }
}

sealed class SaxoClient
{
	[JsonProperty("ClientKey")]
	public string ClientKey { get; set; }

	[JsonProperty("DefaultAccountKey")]
	public string DefaultAccountKey { get; set; }

	[JsonProperty("ClientId")]
	public string ClientId { get; set; }
}

sealed class SaxoAccount
{
	[JsonProperty("AccountId")]
	public string AccountId { get; set; }

	[JsonProperty("AccountKey")]
	public string AccountKey { get; set; }

	[JsonProperty("ClientKey")]
	public string ClientKey { get; set; }

	[JsonProperty("Currency")]
	public string Currency { get; set; }

	[JsonProperty("DisplayName")]
	public string DisplayName { get; set; }
}

sealed class SaxoSessionInfo
{
	public string ClientKey { get; set; }
	public string AccountKey { get; set; }
	public string AccountId { get; set; }
	public string Currency { get; set; }
}

sealed class SaxoResetSubscriptions
{
	[JsonProperty("TargetReferenceIds")]
	public string[] TargetReferenceIds { get; set; }

	[JsonProperty("Reason")]
	public string Reason { get; set; }
}

sealed class SaxoDisconnectControl
{
	[JsonProperty("Reason")]
	public string Reason { get; set; }
}

sealed class SaxoHeartbeatBatch
{
	[JsonProperty("Heartbeats")]
	public SaxoHeartbeat[] Heartbeats { get; set; }
}

sealed class SaxoHeartbeat
{
	[JsonProperty("OriginatingReferenceId")]
	public string OriginatingReferenceId { get; set; }

	[JsonProperty("Reason")]
	public string Reason { get; set; }
}
