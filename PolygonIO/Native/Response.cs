namespace StockSharp.PolygonIO.Native;

class Response<T>
{
	[JsonProperty("results")]
	public T[] Results { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("request_id")]
	public string RequestId { get; set; }

	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("next_url")]
	public string NextUrl { get; set; }
}