namespace StockSharp.Huobi.Native.Spot.Model;

class Balance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("balance")]
	public double? Value { get; set; }
}

class SocketBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("accountId")]
	public long AccountId { get; set; }

	[JsonProperty("balance")]
	public double? Balance { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("changeType")]
	public string ChangeType { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("changeTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? ChangeTime { get; set; }
}