namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class TransactionPagesResponse : BaseResponse
{
	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }

	[JsonProperty("to")]
	public string To { get; set; }

	[JsonProperty("pages")]
	public string[] Pages { get; set; }
}