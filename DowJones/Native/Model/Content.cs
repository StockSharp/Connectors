namespace StockSharp.DowJones.Native.Model;

sealed class DowJonesSearchResponse
{
	[JsonProperty("meta")]
	public DowJonesSearchMeta Meta { get; set; }

	[JsonProperty("links")]
	public DowJonesLinks Links { get; set; }

	[JsonProperty("data")]
	public DowJonesContentResource[] Data { get; set; }
}

sealed class DowJonesArticleResponse
{
	[JsonProperty("data")]
	public DowJonesContentResource Data { get; set; }
}

sealed class DowJonesSearchMeta
{
	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("duplicate_count")]
	public int DuplicateCount { get; set; }

	[JsonProperty("total_count")]
	public int TotalCount { get; set; }

	[JsonProperty("paging")]
	public DowJonesPaging Paging { get; set; }
}

sealed class DowJonesPaging
{
	[JsonProperty("offset")]
	public DowJonesOffsets Offset { get; set; }
}

sealed class DowJonesOffsets
{
	[JsonProperty("first")]
	public int First { get; set; }

	[JsonProperty("prev")]
	public int Previous { get; set; }

	[JsonProperty("next")]
	public int Next { get; set; }

	[JsonProperty("last")]
	public int Last { get; set; }

	[JsonProperty("current")]
	public int Current { get; set; }
}

sealed class DowJonesContentResource
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("links")]
	public DowJonesLinks Links { get; set; }

	[JsonProperty("meta")]
	public DowJonesResourceMeta Meta { get; set; }

	[JsonProperty("attributes")]
	public DowJonesContentAttributes Attributes { get; set; }
}

sealed class DowJonesLinks
{
	[JsonProperty("self")]
	public string Self { get; set; }
}

sealed class DowJonesContentAttributes
{
	[JsonProperty("content_type")]
	public string ContentType { get; set; }

	[JsonProperty("associations")]
	public DowJonesAssociations Associations { get; set; }

	[JsonProperty("body")]
	public DowJonesContentNode[] Body { get; set; }

	[JsonProperty("byline")]
	public DowJonesContentContainer Byline { get; set; }

	[JsonProperty("copyright")]
	public DowJonesText Copyright { get; set; }

	[JsonProperty("headline")]
	public DowJonesHeadline Headline { get; set; }

	[JsonProperty("snippet")]
	public DowJonesContentContainer Snippet { get; set; }

	[JsonProperty("summary")]
	public DowJonesSummary Summary { get; set; }

	[JsonProperty("hosted_url")]
	public string HostedUrl { get; set; }

	[JsonProperty("dist_publish_time")]
	public string DistributionPublishTime { get; set; }

	[JsonProperty("publication_time")]
	public string PublicationTime { get; set; }

	[JsonProperty("modification_time")]
	public string ModificationTime { get; set; }

	[JsonProperty("load_time")]
	public string LoadTime { get; set; }
}

sealed class DowJonesAssociations
{
	[JsonProperty("parent_id")]
	public string ParentId { get; set; }

	[JsonProperty("parent_id_ref")]
	public string ParentIdReference { get; set; }
}

sealed class DowJonesHeadline
{
	[JsonProperty("main")]
	public DowJonesContentNode Main { get; set; }

	[JsonProperty("deck")]
	public DowJonesContentNode Deck { get; set; }
}

sealed class DowJonesSummary
{
	[JsonProperty("body")]
	public DowJonesContentNode[] Body { get; set; }

	[JsonProperty("headline")]
	public DowJonesHeadline Headline { get; set; }
}

sealed class DowJonesContentContainer
{
	[JsonProperty("content")]
	public DowJonesContentNode[] Content { get; set; }
}

sealed class DowJonesContentNode
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("content")]
	public DowJonesContentNode[] Content { get; set; }

	[JsonProperty("ref")]
	public string Reference { get; set; }

	[JsonProperty("uri")]
	public string Uri { get; set; }
}

sealed class DowJonesText
{
	[JsonProperty("text")]
	public string Text { get; set; }
}

sealed class DowJonesResourceMeta
{
	[JsonProperty("alternate_document_id")]
	public string AlternateDocumentId { get; set; }

	[JsonProperty("alternate_document_ref")]
	public string AlternateDocumentReference { get; set; }

	[JsonProperty("code_sets")]
	public DowJonesCodeSet[] CodeSets { get; set; }

	[JsonProperty("emphasis")]
	public DowJonesEmphasis Emphasis { get; set; }

	[JsonProperty("is_translation_allowed")]
	public bool IsTranslationAllowed { get; set; }

	[JsonProperty("language")]
	public DowJonesLanguage Language { get; set; }

	[JsonProperty("original_doc_id")]
	public string OriginalDocumentId { get; set; }

	[JsonProperty("source")]
	public DowJonesSource Source { get; set; }
}

sealed class DowJonesEmphasis
{
	[JsonProperty("hot")]
	public bool IsHot { get; set; }

	[JsonProperty("dominant")]
	public bool IsDominant { get; set; }

	[JsonProperty("analysis")]
	public bool IsAnalysis { get; set; }
}

sealed class DowJonesLanguage
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("descriptor")]
	public string Descriptor { get; set; }
}

sealed class DowJonesSource
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

sealed class DowJonesCodeSet
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("codes")]
	public DowJonesCode[] Codes { get; set; }
}

sealed class DowJonesCode
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("code_scheme")]
	public string CodeScheme { get; set; }

	[JsonProperty("codeschema")]
	public string LegacyCodeScheme { get; set; }

	[JsonProperty("descriptor")]
	public string Descriptor { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("significance")]
	public string Significance { get; set; }
}
