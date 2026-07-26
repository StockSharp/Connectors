namespace StockSharp.LMAX.Native.Model;

class AccountTransaction
{
	[JsonProperty("transaction_category")]
	public string TransactionCategory { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("account_statement_id")]
	public string AccountStatementId { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("currency_balance")]
	public string CurrencyBalance { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }
}

class AccountTransactionResponse
{
	[JsonProperty("before_cursor")]
	public string BeforeCursor { get; set; }

	[JsonProperty("after_cursor")]
	public string AfterCursor { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("transactions")]
	public AccountTransaction[] Transactions { get; set; }
}
