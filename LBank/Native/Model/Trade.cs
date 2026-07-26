namespace StockSharp.LBank.Native.Model;

class Trade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal Amount { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; set; }
}

class SocketTrade
{
	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("TS")]
	[JsonConverter(typeof(LBankChinaDateTimeConverter))]
	public DateTime Time { get; set; }
}
