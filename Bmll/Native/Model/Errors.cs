namespace StockSharp.Bmll.Native.Model;

sealed class BmllErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("errors")]
	public BmllErrorItem[] Errors { get; set; }

	public string GetMessage()
	{
		var errors = (Errors ?? []).Where(item => item != null)
			.Select(item => item.Message.IsEmpty(item.Detail).IsEmpty(item.Code))
			.Where(value => !value.IsEmpty()).Join("; ");
		return errors.IsEmpty(Detail).IsEmpty(Message).IsEmpty(Error);
	}
}

sealed class BmllErrorItem
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }
}
