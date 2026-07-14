namespace StockSharp.Webull.Native.Model;

abstract class MarketDataStreamRequest
{
	[JsonProperty("session_id")]
	public string SessionId { get; set; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("category")]
	public WebullInstrumentCategories Category { get; set; }

	[JsonProperty("sub_types")]
	public WebullMarketDataSubTypes[] SubTypes { get; set; }
}

sealed class SubscribeMarketDataRequest : MarketDataStreamRequest
{
	[JsonProperty("depth", NullValueHandling = NullValueHandling.Ignore)]
	public int? Depth { get; set; }
}

sealed class UnsubscribeMarketDataRequest : MarketDataStreamRequest
{
	[JsonProperty("unsubscribe_all")]
	public bool UnsubscribeAll { get; set; }
}
