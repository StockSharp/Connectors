namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OrdersResponse : BaseResponse
{
	[JsonProperty("orders")]
	public IEnumerable<Order> Orders { get; set; }
}