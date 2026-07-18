namespace StockSharp.MtNewswires.Native.Model;

sealed class MtNewswiresArticle
{
	[JsonProperty("body")]
	public string Body { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("headline")]
	public string Headline { get; set; }

	[JsonProperty("isPrimary")]
	public bool? IsPrimary { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("key")]
	public string Key { get; set; }

	[JsonProperty("metadata")]
	public string Metadata { get; set; }

	[JsonProperty("rawMetadataCodes")]
	public string RawMetadataCodes { get; set; }

	[JsonProperty("related")]
	public string Related { get; set; }

	[JsonProperty("releaseTime")]
	public string ReleaseTime { get; set; }

	[JsonProperty("storyType")]
	public string StoryType { get; set; }

	[JsonProperty("subkey")]
	public string Subkey { get; set; }
}

sealed class ViaNexusErrorResponse
{
	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }

	public string GetMessage()
		=> Detail.IsEmpty(Message).IsEmpty(Error).IsEmpty(Code);
}
