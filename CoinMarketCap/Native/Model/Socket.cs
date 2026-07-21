namespace StockSharp.CoinMarketCap.Native.Model;

sealed class CoinMarketCapSocketRequest
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("method")]
	public CoinMarketCapSocketMethods Method { get; set; }

	[JsonProperty("channel", NullValueHandling = NullValueHandling.Ignore)]
	public CoinMarketCapSocketChannels? Channel { get; set; }

	[JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
	public CoinMarketCapSocketRequestParameters Parameters { get; set; }
}

sealed class CoinMarketCapSocketRequestParameters
{
	[JsonProperty("crypto_ids")]
	public int[] CryptoIds { get; set; }
}

sealed class CoinMarketCapSocketEnvelope
{
	[JsonProperty("type")]
	public CoinMarketCapSocketMessageTypes? Type { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("channel")]
	public CoinMarketCapSocketChannels? Channel { get; set; }

	[JsonProperty("params")]
	public CoinMarketCapSocketResponseParameters Parameters { get; set; }

	[JsonProperty("data")]
	public CoinMarketCapSocketPrice Data { get; set; }

	[JsonProperty("status")]
	public CoinMarketCapStatus Status { get; set; }

	[JsonProperty("ts")]
	public long? Timestamp { get; set; }

	[JsonProperty("ping_interval_ms")]
	public int? PingIntervalMilliseconds { get; set; }

	[JsonProperty("sub_count")]
	public int? SubscriptionCount { get; set; }

	[JsonProperty("sub_limit")]
	public int? SubscriptionLimit { get; set; }

	[JsonProperty("duplicate")]
	public bool? IsDuplicate { get; set; }
}

sealed class CoinMarketCapSocketResponseParameters
{
	[JsonProperty("crypto_ids")]
	public int? CryptoId { get; set; }
}

sealed class CoinMarketCapSocketPrice
{
	[JsonProperty("cid")]
	public int CryptoId { get; set; }

	[JsonProperty("p")]
	public decimal? Price { get; set; }

	[JsonProperty("vu")]
	public decimal? Volume24Hours { get; set; }

	[JsonProperty("mc")]
	public decimal? MarketCapitalization { get; set; }

	[JsonProperty("cs")]
	public decimal? CirculatingSupply { get; set; }

	[JsonProperty("p24h")]
	public decimal? PriceChange24Hours { get; set; }

	[JsonProperty("p7d")]
	public decimal? PriceChange7Days { get; set; }

	[JsonProperty("p30d")]
	public decimal? PriceChange30Days { get; set; }

	[JsonProperty("p60d")]
	public decimal? PriceChange60Days { get; set; }

	[JsonProperty("p3m")]
	public decimal? PriceChange3Months { get; set; }

	[JsonProperty("p1y")]
	public decimal? PriceChange1Year { get; set; }

	[JsonProperty("pytd")]
	public decimal? PriceChangeYearToDate { get; set; }

	[JsonProperty("pall")]
	public decimal? PriceChangeAllTime { get; set; }

	[JsonProperty("fdv24h")]
	public decimal? FullyDilutedValueChange24Hours { get; set; }
}
