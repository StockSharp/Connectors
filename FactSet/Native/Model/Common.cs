namespace StockSharp.FactSet.Native.Model;

sealed class FactSetErrorResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("path")]
	public string Path { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("subErrors")]
	public FactSetSubError SubErrors { get; set; }

	public string GetMessage()
		=> SubErrors?.Message.IsEmpty(Message).IsEmpty(Status);
}

sealed class FactSetSubError
{
	[JsonProperty("object")]
	public string Object { get; set; }

	[JsonProperty("field")]
	public string Field { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("rejectedValue")]
	public string[] RejectedValue { get; set; }
}
