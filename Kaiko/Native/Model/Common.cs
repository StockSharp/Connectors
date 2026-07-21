namespace StockSharp.Kaiko.Native.Model;

sealed class KaikoReferenceResponse
{
	[JsonProperty("result")]
	public KaikoResults Result { get; set; }

	[JsonProperty("count")]
	public int? Count { get; set; }

	[JsonProperty("data")]
	public KaikoInstrument[] Data { get; set; }
}

sealed class KaikoInstrument
{
	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }

	[JsonProperty("exchange_pair_code")]
	public string ExchangePairCode { get; set; }

	[JsonProperty("base_asset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote_asset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("class")]
	public KaikoInstrumentClasses InstrumentClass { get; set; }

	[JsonProperty("trade_count")]
	public long? TradeCount { get; set; }

	[JsonProperty("trade_expiry_timestamp")]
	public long? TradeExpiryTimestamp { get; set; }
}

sealed class KaikoMarketResponse<T>
{
	[JsonProperty("result")]
	public KaikoResults Result { get; set; }

	[JsonProperty("data")]
	public T[] Data { get; set; }

	[JsonProperty("continuation_token")]
	public string ContinuationToken { get; set; }

	[JsonProperty("next_url")]
	public string NextUrl { get; set; }
}

sealed class KaikoErrorResponse
{
	[JsonProperty("result")]
	public KaikoResults Result { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }
}
