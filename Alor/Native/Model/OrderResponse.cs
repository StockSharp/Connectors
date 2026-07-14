namespace StockSharp.Alor.Native.Model;

class OrderResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("orderNumber")]
	public long OrderNumber { get; set; }
}