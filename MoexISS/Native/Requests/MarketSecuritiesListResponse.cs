namespace StockSharp.MoexISS.Native.Requests;

/// <summary>
/// Ответ на запрос списка инструментов рынка
/// </summary>
class MarketSecuritiesListResponse
{
	[JsonProperty("securities")]
	public IssResponsePayload Securities { get; set; }
}