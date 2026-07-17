namespace StockSharp.JpmDataQuery.Native.Model;

sealed class JpmDataQueryLink
{
	[JsonProperty("self")]
	public string Self { get; set; }

	[JsonProperty("next")]
	public string Next { get; set; }
}

abstract class JpmDataQueryPage
{
	[JsonProperty("items")]
	public long Items { get; set; }

	[JsonProperty("page-size")]
	public int PageSize { get; set; }

	[JsonProperty("links")]
	public JpmDataQueryLink[] Links { get; set; }

	public string GetNextLink()
		=> Links?.Select(link => link?.Next).FirstOrDefault(next => !next.IsEmpty());
}

class JpmDataQueryError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("x-dataquery-interaction-id")]
	public string InteractionId { get; set; }
}

sealed class JpmDataQueryErrorEnvelope : JpmDataQueryError
{
	[JsonProperty("info")]
	public JpmDataQueryError Info { get; set; }

	[JsonProperty("error")]
	public JpmDataQueryError Error { get; set; }

	[JsonProperty("errors")]
	public JpmDataQueryError[] Errors { get; set; }

	public JpmDataQueryError GetError()
	{
		if (!Description.IsEmpty() || !Code.IsEmpty())
			return this;
		return Info ?? Error ?? Errors?.FirstOrDefault();
	}
}
