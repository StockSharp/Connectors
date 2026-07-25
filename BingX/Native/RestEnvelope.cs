namespace StockSharp.BingX.Native;

/// <summary>
/// Envelope every BingX REST endpoint wraps its payload into.
/// </summary>
class RestEnvelope
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public JToken Data { get; set; }
}
