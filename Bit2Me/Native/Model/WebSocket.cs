namespace StockSharp.Bit2Me.Native.Model;

sealed class Bit2MeWsSubscription
{
	[JsonProperty("name")]
	public string Name { get; init; }
}

sealed class Bit2MeWsSubscriptionCommand
{
	[JsonProperty("event")]
	public string Event { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("subscription")]
	public Bit2MeWsSubscription Subscription { get; init; }
}

class Bit2MeWsHeader
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("subscription")]
	public Bit2MeWsSubscription Subscription { get; set; }

	[JsonProperty("result")]
	public string Result { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}

sealed class Bit2MeWsEnvelope<TData> : Bit2MeWsHeader
{
	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class Bit2MeWsTrade
{
	[JsonProperty("side")]
	public Bit2MeSides Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}
