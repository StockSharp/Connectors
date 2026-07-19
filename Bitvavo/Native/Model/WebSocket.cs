namespace StockSharp.Bitvavo.Native.Model;

sealed class BitvavoWsHeader
{
	[JsonProperty("event")]
	public BitvavoEvents? Event { get; set; }

	[JsonProperty("errorCode")]
	public int? ErrorCode { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("authenticated")]
	public bool? IsAuthenticated { get; set; }
}

sealed class BitvavoWsAuthenticateCommand
{
	[JsonProperty("action")]
	public BitvavoActions Action { get; init; } = BitvavoActions.Authenticate;

	[JsonProperty("key")]
	public string Key { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("window")]
	public long Window { get; init; }
}

sealed class BitvavoWsSubscriptionCommand
{
	[JsonProperty("action")]
	public BitvavoActions Action { get; init; }

	[JsonProperty("channels")]
	public BitvavoWsChannel[] Channels { get; init; }
}

sealed class BitvavoWsChannel
{
	[JsonProperty("name")]
	public BitvavoChannels Name { get; init; }

	[JsonProperty("markets")]
	public string[] Markets { get; init; }

	[JsonProperty("interval")]
	public string[] Intervals { get; init; }
}

sealed class BitvavoWsTicker
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("bestBid")]
	public decimal? BestBid { get; set; }

	[JsonProperty("bestBidSize")]
	public decimal? BestBidSize { get; set; }

	[JsonProperty("bestAsk")]
	public decimal? BestAsk { get; set; }

	[JsonProperty("bestAskSize")]
	public decimal? BestAskSize { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }
}

sealed class BitvavoWsTicker24Envelope
{
	[JsonProperty("data")]
	public BitvavoTicker Data { get; set; }
}

sealed class BitvavoWsCandles
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("candle")]
	public BitvavoCandle[] Candles { get; set; }
}
