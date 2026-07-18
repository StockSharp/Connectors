namespace StockSharp.DowJones.Native.Model;

sealed class DowJonesSearchRequest
{
	[JsonProperty("data")]
	public DowJonesSearchRequestData Data { get; init; }
}

sealed class DowJonesSearchRequestData
{
	[JsonProperty("id")]
	public string Id { get; init; } = "Search";

	[JsonProperty("type")]
	public string Type { get; init; } = "content";

	[JsonProperty("attributes")]
	public DowJonesSearchAttributes Attributes { get; init; }
}

sealed class DowJonesSearchAttributes
{
	[JsonProperty("query")]
	public DowJonesSearchQuery Query { get; init; }

	[JsonProperty("formatting")]
	public DowJonesSearchFormatting Formatting { get; init; }

	[JsonProperty("navigation")]
	public DowJonesSearchNavigation Navigation { get; init; }

	[JsonProperty("page_offset")]
	public int PageOffset { get; init; }

	[JsonProperty("page_limit")]
	public int PageLimit { get; init; }
}

sealed class DowJonesSearchQuery
{
	[JsonProperty("search_string")]
	public DowJonesSearchString[] SearchStrings { get; init; }
}

sealed class DowJonesSearchString
{
	[JsonProperty("mode")]
	public string Mode { get; init; } = "Unified";

	[JsonProperty("value")]
	public string Value { get; init; }
}

sealed class DowJonesSearchFormatting
{
	[JsonProperty("sort_order")]
	public string SortOrder { get; init; }

	[JsonProperty("is_return_rich_article_id")]
	public bool IsReturnRichArticleId { get; init; } = true;
}

sealed class DowJonesSearchNavigation
{
	[JsonProperty("is_return_headline_coding")]
	public bool IsReturnHeadlineCoding { get; init; } = true;

	[JsonProperty("is_return_djn_headline_coding")]
	public bool IsReturnDjnHeadlineCoding { get; init; } = true;
}
