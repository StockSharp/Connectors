namespace StockSharp.LMAX.Native.Model;

class AuthenticationRequest
{
	[JsonProperty("client_key_id")]
	public string ClientKeyId { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("nonce")]
	public string Nonce { get; set; }

	[JsonProperty("signature")]
	public string Signature { get; set; }
}

class AuthenticationResponse
{
	[JsonProperty("token")]
	public string Token { get; set; }
}

class ApiError
{
	[JsonProperty("error_code")]
	public string ErrorCode { get; set; }

	[JsonProperty("error_message")]
	public string ErrorMessage { get; set; }
}

class TimeResponse
{
	[JsonProperty("epoch_millis")]
	public string EpochMillis { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }
}

class VersionResponse
{
	[JsonProperty("version")]
	public string Version { get; set; }
}
