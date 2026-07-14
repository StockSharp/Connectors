namespace StockSharp.Schwab.Native.Model;

sealed class StreamerRequestEnvelope
{
	[JsonProperty("requests")]
	public StreamerRequest[] Requests { get; set; }
}

sealed class StreamerRequest
{
	[JsonProperty("service")]
	public string Service { get; set; }

	[JsonProperty("requestid")]
	public string RequestId { get; set; }

	[JsonProperty("command")]
	public string Command { get; set; }

	[JsonProperty("SchwabClientCustomerId")]
	public string CustomerId { get; set; }

	[JsonProperty("SchwabClientCorrelId")]
	public string CorrelId { get; set; }

	[JsonProperty("parameters")]
	public object Parameters { get; set; }
}

sealed class LoginParameters
{
	[JsonProperty("Authorization")]
	public string Authorization { get; set; }

	[JsonProperty("SchwabClientChannel")]
	public string Channel { get; set; }

	[JsonProperty("SchwabClientFunctionId")]
	public string FunctionId { get; set; }
}

sealed class SubscriptionParameters
{
	[JsonProperty("keys")]
	public string Keys { get; set; }

	[JsonProperty("fields", NullValueHandling = NullValueHandling.Ignore)]
	public string Fields { get; set; }
}

sealed class StreamerResponseEnvelope
{
	[JsonProperty("response")]
	public StreamerResponse[] Responses { get; set; }
}

sealed class StreamerResponse
{
	[JsonProperty("requestid")]
	public string RequestId { get; set; }

	[JsonProperty("content")]
	public StreamerResponseContent Content { get; set; }
}

sealed class StreamerResponseContent
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class LevelOneEnvelope
{
	[JsonProperty("data")]
	public LevelOneData[] Data { get; set; }
}

sealed class LevelOneData
{
	[JsonProperty("service")]
	public string Service { get; set; }

	[JsonProperty("content")]
	public LevelOneContent[] Content { get; set; }
}

sealed class LevelOneContent
{
	[JsonProperty("key")]
	public string Key { get; set; }

	[JsonProperty("0")]
	public string Symbol { get; set; }

	[JsonProperty("1")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("2")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("3")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("4")]
	public decimal? BidSize { get; set; }

	[JsonProperty("5")]
	public decimal? AskSize { get; set; }

	[JsonProperty("8")]
	public decimal? Volume { get; set; }

	[JsonProperty("9")]
	public decimal? LastSize { get; set; }

	[JsonProperty("10")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("11")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("12")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("17")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("18")]
	public decimal? Change { get; set; }

	[JsonProperty("34")]
	public long? QuoteTime { get; set; }

	[JsonProperty("35")]
	public long? TradeTime { get; set; }
}

sealed class BookEnvelope
{
	[JsonProperty("data")]
	public BookData[] Data { get; set; }
}

sealed class BookData
{
	[JsonProperty("service")]
	public string Service { get; set; }

	[JsonProperty("content")]
	public BookContent[] Content { get; set; }
}

sealed class BookContent
{
	[JsonProperty("key")]
	public string Key { get; set; }

	[JsonProperty("0")]
	public string Symbol { get; set; }

	[JsonProperty("1")]
	public long? Timestamp { get; set; }

	[JsonProperty("2")]
	public BookLevel[] Bids { get; set; }

	[JsonProperty("3")]
	public BookLevel[] Asks { get; set; }
}

sealed class BookLevel
{
	[JsonProperty("0")]
	public decimal Price { get; set; }

	[JsonProperty("1")]
	public decimal Volume { get; set; }
}

sealed class AccountActivityEnvelope
{
	[JsonProperty("data")]
	public AccountActivityData[] Data { get; set; }
}

sealed class AccountActivityData
{
	[JsonProperty("service")]
	public string Service { get; set; }

	[JsonProperty("content")]
	public AccountActivityContent[] Content { get; set; }
}

sealed class AccountActivityContent
{
	[JsonProperty("1")]
	public string Account { get; set; }

	[JsonProperty("3")]
	public string Payload { get; set; }
}
