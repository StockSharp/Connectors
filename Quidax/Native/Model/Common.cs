namespace StockSharp.Quidax.Native.Model;

sealed class QuidaxEnvelope
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public JToken Data { get; set; }
}

sealed class QuidaxError
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public JToken Error { get; set; }
}

sealed class QuidaxPage<T>
{
	[JsonProperty("models")]
	public T[] Models { get; set; }

	[JsonProperty("current_page")]
	public int CurrentPage { get; set; }

	[JsonProperty("total_pages")]
	public int TotalPages { get; set; }
}
