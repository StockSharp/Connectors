namespace StockSharp.Alor.Native.Model;

class RequestResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("httpCode")]
	public int HttpCode { get; set; }

	[JsonProperty("requestGuid")]
	public string RequestGuid { get; set; }

	[JsonProperty("orderNumber")]
	public long? OrderNumber { get; set; }
}