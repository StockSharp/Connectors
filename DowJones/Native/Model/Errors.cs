namespace StockSharp.DowJones.Native.Model;

sealed class DowJonesErrorResponse
{
	[JsonProperty("errors")]
	public DowJonesError[] Errors { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }

	public string GetMessage()
	{
		var details = (Errors ?? []).Where(error => error != null)
			.Select(error => error.Detail.IsEmpty(error.Title).IsEmpty(error.Code))
			.Where(value => !value.IsEmpty()).Join("; ");
		return details.IsEmpty(ErrorDescription).IsEmpty(Error);
	}
}

sealed class DowJonesError
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }
}
