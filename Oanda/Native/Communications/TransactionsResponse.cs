namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class TransactionsResponse : BaseResponse
{
	[JsonProperty("transactions")]
	public IEnumerable<Transaction> Transactions { get; set; }
}