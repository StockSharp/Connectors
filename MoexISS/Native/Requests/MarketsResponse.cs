namespace StockSharp.MoexISS.Native.Requests;

class MarketsResponse
{
	[JsonProperty("markets")]
	public IssResponsePayload Markets { get; set; }
}