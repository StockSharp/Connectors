namespace StockSharp.MoexISS.Native.Requests;

class SecurityDividendsResponse
{
	[JsonProperty("dividends")]
	public IssResponsePayload Dividends { get; set; }
}