namespace StockSharp.Bitget.Native;

/// <summary>
/// Envelope every Bitget REST endpoint wraps its payload into.
/// </summary>
class RestEnvelope
{
	public const string SuccessCode = "00000";

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public JToken Data { get; set; }
}
