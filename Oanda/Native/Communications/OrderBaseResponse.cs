namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
abstract class OrderBaseResponse : ErrorResponse
{
	[JsonProperty("relatedTransactionIDs")]
	public IEnumerable<long> RelatedTransactionIds { get; set; }
}