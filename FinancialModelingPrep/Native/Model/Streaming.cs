namespace StockSharp.FinancialModelingPrep.Native.Model;

sealed class FmpLoginRequest
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("data")]
	public FmpLoginData Data { get; set; }
}

sealed class FmpLoginData
{
	[JsonProperty("apiKey")]
	public string ApiKey { get; set; }
}

sealed class FmpSubscriptionRequest
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("data")]
	public FmpSubscriptionData Data { get; set; }
}

sealed class FmpSubscriptionData
{
	[JsonProperty("ticker")]
	public string[] Tickers { get; set; }
}

sealed class FmpStreamMessage
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("status")]
	public int? Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long? Timestamp { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("ap")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("as")]
	public decimal? AskSize { get; set; }

	[JsonProperty("bp")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bs")]
	public decimal? BidSize { get; set; }

	[JsonProperty("lp")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("ls")]
	public decimal? LastSize { get; set; }
}
