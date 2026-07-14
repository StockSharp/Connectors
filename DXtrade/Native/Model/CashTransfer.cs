namespace StockSharp.DXtrade.Native.Model;

class CashTransfer
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("version")]
	public long Version { get; set; }

	[JsonProperty("transferCode")]
	public string TransferCode { get; set; }

	[JsonProperty("comment")]
	public string Comment { get; set; }

	[JsonProperty("transactionTime")]
	public DateTime TransactionTime { get; set; }

	[JsonProperty("cashTransactions")]
	public CashTransaction[] CashTransactions { get; set; }
}

class CashTransaction
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("transactionCode")]
	public string TransactionCode { get; set; }

	[JsonProperty("orderCode")]
	public string OrderCode { get; set; }

	[JsonProperty("tradeCode")]
	public string TradeCode { get; set; }

	[JsonProperty("positionCode")]
	public string PositionCode { get; set; }

	[JsonProperty("version")]
	public long Version { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("value")]
	public double Value { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("transactionTime")]
	public DateTime TransactionTime { get; set; }
}