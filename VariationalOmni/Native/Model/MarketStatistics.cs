namespace StockSharp.VariationalOmni.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class VariationalOmniStatistics
{
	[JsonProperty("total_volume_24h")]
	public string TotalVolume24h { get; set; }

	[JsonProperty("cumulative_volume")]
	public string CumulativeVolume { get; set; }

	[JsonProperty("tvl")]
	public string TotalValueLocked { get; set; }

	[JsonProperty("open_interest")]
	public string OpenInterest { get; set; }

	[JsonProperty("num_markets")]
	public int MarketCount { get; set; }

	[JsonProperty("loss_refund")]
	public VariationalOmniLossRefund LossRefund { get; set; }

	[JsonProperty("listings", Required = Required.Always)]
	public VariationalOmniListing[] Listings { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class VariationalOmniLossRefund
{
	[JsonProperty("pool_size")]
	public string PoolSize { get; set; }

	[JsonProperty("refunded_24h")]
	public string Refunded24h { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class VariationalOmniListing
{
	[JsonProperty("ticker", Required = Required.Always)]
	public string Ticker { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }

	[JsonProperty("volume_24h")]
	public string Volume24h { get; set; }

	[JsonProperty("open_interest")]
	public VariationalOmniOpenInterest OpenInterest { get; set; }

	[JsonProperty("funding_rate")]
	public string FundingRate { get; set; }

	[JsonProperty("funding_interval_s")]
	public int? FundingIntervalSeconds { get; set; }

	[JsonProperty("base_spread_bps")]
	public string BaseSpreadBps { get; set; }

	[JsonProperty("quotes")]
	public VariationalOmniQuotes Quotes { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class VariationalOmniOpenInterest
{
	[JsonProperty("long_open_interest")]
	public string Long { get; set; }

	[JsonProperty("short_open_interest")]
	public string Short { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class VariationalOmniQuotes
{
	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }

	[JsonProperty("base")]
	public VariationalOmniQuote Base { get; set; }

	[JsonProperty("size_1k")]
	public VariationalOmniQuote Size1K { get; set; }

	[JsonProperty("size_100k")]
	public VariationalOmniQuote Size100K { get; set; }

	[JsonProperty("size_1m")]
	public VariationalOmniQuote Size1M { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class VariationalOmniQuote
{
	[JsonProperty("bid")]
	public string Bid { get; set; }

	[JsonProperty("ask")]
	public string Ask { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class VariationalOmniError
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }
}
