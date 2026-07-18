namespace StockSharp.ThetaData.Native.Model;

sealed class ThetaStreamRequest
{
	[JsonProperty("msg_type")]
	public string MessageType { get; set; }

	[JsonProperty("sec_type")]
	public string SecurityType { get; set; }

	[JsonProperty("req_type")]
	public string RequestType { get; set; }

	[JsonProperty("add")]
	public bool IsAdd { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("contract")]
	public ThetaStreamContract Contract { get; set; }
}

sealed class ThetaStreamContract
{
	[JsonProperty("security_type")]
	public string SecurityType { get; set; }

	[JsonProperty("root")]
	public string Root { get; set; }

	[JsonProperty("expiration")]
	public int? Expiration { get; set; }

	[JsonProperty("strike")]
	public long? Strike { get; set; }

	[JsonProperty("right")]
	public string Right { get; set; }
}

sealed class ThetaStreamMessage
{
	[JsonProperty("header")]
	public ThetaStreamHeader Header { get; set; }

	[JsonProperty("contract")]
	public ThetaStreamContract Contract { get; set; }

	[JsonProperty("trade")]
	public ThetaStreamTrade Trade { get; set; }

	[JsonProperty("quote")]
	public ThetaStreamQuote Quote { get; set; }

	[JsonProperty("ohlc")]
	public ThetaStreamOhlc Ohlc { get; set; }
}

sealed class ThetaStreamHeader
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("response")]
	public string Response { get; set; }

	[JsonProperty("req_id")]
	public long? RequestId { get; set; }
}

sealed class ThetaStreamTrade
{
	[JsonProperty("ms_of_day")]
	public long? MillisecondsOfDay { get; set; }

	[JsonProperty("sequence")]
	public long? Sequence { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("condition")]
	public int? Condition { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("exchange")]
	public int? Exchange { get; set; }

	[JsonProperty("date")]
	public int? Date { get; set; }
}

sealed class ThetaStreamQuote
{
	[JsonProperty("ms_of_day")]
	public long? MillisecondsOfDay { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("bid_exchange")]
	public int? BidExchange { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bid_condition")]
	public int? BidCondition { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("ask_exchange")]
	public int? AskExchange { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("ask_condition")]
	public int? AskCondition { get; set; }

	[JsonProperty("date")]
	public int? Date { get; set; }
}

sealed class ThetaStreamOhlc
{
	[JsonProperty("ms_of_day")]
	public long? MillisecondsOfDay { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("count")]
	public long? Count { get; set; }

	[JsonProperty("date")]
	public int? Date { get; set; }
}
