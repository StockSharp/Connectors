namespace StockSharp.MoexISS.Native.Requests;

class EngineMarketSecurityCandlesResponse
{
	[JsonProperty("candles")]
	public IssResponsePayload Candles { get; set; }
}