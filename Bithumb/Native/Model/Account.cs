namespace StockSharp.Bithumb.Native.Model;

class Account
{
	[JsonProperty("created")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Created { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("trade_fee")]
	public decimal TradeFee { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }
}