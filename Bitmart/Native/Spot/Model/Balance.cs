namespace StockSharp.Bitmart.Native.Spot.Model;

class Balance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("frozen")]
	public double? Frozen { get; set; }
}

class SocketBalance
{
	[JsonProperty("ccy")]
	public string Currency { get; set; }

	[JsonProperty("av_bal")]
	public double? Available { get; set; }

	[JsonProperty("fz_bal")]
	public double? Frozen { get; set; }
}

class SocketBalanceData
{
	// Reason for change. Type
	// TRANSACTION_COMPLETED=Trade
	// ACCOUNT_RECHARGE=Recharge
	// ACCOUNT_WITHDRAWAL=Withdraw
	// ACCOUNT_TRANSFER=Transfer
	[JsonProperty("event_type")]
	public string EventType { get; set; }

	[JsonProperty("event_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime EventTime { get; set; }

	[JsonProperty("balance_details")]
	public SocketBalance[] Balances { get; set; }
}