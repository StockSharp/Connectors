namespace StockSharp.Bithumb.Native.Model;

sealed class Symbol
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("english_name")]
	public string EnglishName { get; set; }
}

sealed class ApiErrorResponse
{
	[JsonProperty("error")]
	public ApiError Error { get; set; }
}

sealed class ApiError
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class SocketEnvelope
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("error")]
	public ApiError Error { get; set; }
}

abstract class SocketRequestField
{
}

sealed class SocketTicket : SocketRequestField
{
	[JsonProperty("ticket")]
	public string Ticket { get; set; }
}

sealed class SocketSubscription : SocketRequestField
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("codes")]
	public string[] Codes { get; set; }

	[JsonProperty("isOnlyRealtime")]
	public bool IsOnlyRealtime { get; set; } = true;
}

sealed class SocketFormat : SocketRequestField
{
	[JsonProperty("format")]
	public string Format { get; set; } = "DEFAULT";
}

sealed class JwtHeader
{
	[JsonProperty("alg")]
	public string Algorithm { get; set; } = "HS256";

	[JsonProperty("typ")]
	public string Type { get; set; } = "JWT";
}

sealed class JwtPayload
{
	[JsonProperty("access_key")]
	public string AccessKey { get; set; }

	[JsonProperty("nonce")]
	public string Nonce { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("query_hash", NullValueHandling = NullValueHandling.Ignore)]
	public string QueryHash { get; set; }

	[JsonProperty("query_hash_alg", NullValueHandling = NullValueHandling.Ignore)]
	public string QueryHashAlgorithm { get; set; }
}
