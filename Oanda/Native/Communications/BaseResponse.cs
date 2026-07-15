namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
abstract class BaseResponse : ErrorResponse
{
	[JsonProperty("lastTransactionID")]
	public long? LastTransactionId { get; set; }
}