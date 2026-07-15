namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OrderReplaceResponse : OrderCreateResponse
{
	/// <summary>
	/// The Transaction that cancelled the replacing Order. Only provided when the replacing Order was immediately cancelled.
	/// </summary>
	[JsonProperty("replacingOrderCancelTransaction ")]
	public Transaction ReplacingOrderCancelTransaction { get; set; }
}