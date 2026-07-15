namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OrderCancelResponse : OrderBaseResponse
{
	[JsonProperty("orderCancelTransaction")]
	public Transaction OrderCancelTransaction { get; set; }

	[JsonProperty("orderRejectTransaction")]
	public Transaction OrderCancelRejectTransaction { get; set; }
}