namespace StockSharp.Etoro.Native.Model;

sealed class EtoroErrorResponse
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("status")]
	public int? Status { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }

	[JsonProperty("instance")]
	public string Instance { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }
}

sealed class EtoroEmptyResponse
{
}
