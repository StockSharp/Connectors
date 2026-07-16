namespace StockSharp.CapitalCom.Native.Model;

internal sealed class CapitalComEmptyPayload
{
}

internal sealed class CapitalComSocketRequest<TPayload>
{
	[JsonProperty("destination")]
	public string Destination { get; set; }

	[JsonProperty("correlationId")]
	public string CorrelationId { get; set; }

	[JsonProperty("cst")]
	public string Cst { get; set; }

	[JsonProperty("securityToken")]
	public string SecurityToken { get; set; }

	[JsonProperty("payload", NullValueHandling = NullValueHandling.Ignore)]
	public TPayload Payload { get; set; }
}

internal sealed class CapitalComSocketHeader
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("destination")]
	public string Destination { get; set; }

	[JsonProperty("correlationId")]
	public string CorrelationId { get; set; }
}

internal sealed class CapitalComSocketMessage<TPayload>
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("destination")]
	public string Destination { get; set; }

	[JsonProperty("correlationId")]
	public string CorrelationId { get; set; }

	[JsonProperty("payload")]
	public TPayload Payload { get; set; }
}

internal sealed class CapitalComMarketDataPayload
{
	[JsonProperty("epics")]
	public string[] Epics { get; set; }
}

internal sealed class CapitalComOhlcSubscribePayload
{
	[JsonProperty("epics")]
	public string[] Epics { get; set; }

	[JsonProperty("resolutions")]
	public string[] Resolutions { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; } = "classic";
}

internal sealed class CapitalComOhlcUnsubscribePayload
{
	[JsonProperty("epics")]
	public string[] Epics { get; set; }

	[JsonProperty("resolutions")]
	public string[] Resolutions { get; set; }

	[JsonProperty("types")]
	public string[] Types { get; set; } = ["classic"];
}

internal sealed class CapitalComQuote
{
	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bidQty")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("ofr")]
	public decimal? Ask { get; set; }

	[JsonProperty("ofrQty")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

internal sealed class CapitalComOhlc
{
	[JsonProperty("resolution")]
	public string Resolution { get; set; }

	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("priceType")]
	public string PriceType { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("h")]
	public decimal? High { get; set; }

	[JsonProperty("l")]
	public decimal? Low { get; set; }

	[JsonProperty("o")]
	public decimal? Open { get; set; }

	[JsonProperty("c")]
	public decimal? Close { get; set; }
}
