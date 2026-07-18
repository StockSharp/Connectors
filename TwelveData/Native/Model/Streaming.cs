namespace StockSharp.TwelveData.Native.Model;

sealed class TwelveDataStreamRequest
{
	[JsonProperty("action")]
	public string Action { get; set; }
}

sealed class TwelveDataSimpleStreamRequest
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("params")]
	public TwelveDataSimpleStreamParams Params { get; set; }
}

sealed class TwelveDataSimpleStreamParams
{
	[JsonProperty("symbols")]
	public string Symbols { get; set; }
}

sealed class TwelveDataExtendedStreamRequest
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("params")]
	public TwelveDataExtendedStreamParams Params { get; set; }
}

sealed class TwelveDataExtendedStreamParams
{
	[JsonProperty("symbols")]
	public TwelveDataStreamSymbol[] Symbols { get; set; }
}

sealed class TwelveDataStreamSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("mic_code")]
	public string MicCode { get; set; }
}

sealed class TwelveDataStreamMessage
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("currency_base")]
	public string CurrencyBase { get; set; }

	[JsonProperty("currency_quote")]
	public string CurrencyQuote { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("day_volume")]
	public decimal? DayVolume { get; set; }

	[JsonProperty("success")]
	public TwelveDataStreamStatusItem[] Success { get; set; }

	[JsonProperty("fails")]
	public TwelveDataStreamStatusItem[] Fails { get; set; }
}

sealed class TwelveDataStreamStatusItem
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("country")]
	public string Country { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
