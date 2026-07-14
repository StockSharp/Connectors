namespace StockSharp.LMAX.Native.Model;

class AccountTransaction
{
	[JsonProperty("transaction_id")]
	public string TransactionId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("balance")]
	public double? Balance { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }
}

class AccountTransactionResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("transactions")]
	public AccountTransaction[] Transactions { get; set; }

	[JsonProperty("paging")]
	public PagingInfo Paging { get; set; }
}
