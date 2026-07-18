namespace StockSharp.SpGlobal.Native.Model;

sealed class SpGlobalSymbolResponse
{
	[JsonProperty("results")]
	public SpGlobalSymbol[] Results { get; set; }

	[JsonProperty("metadata")]
	public SpGlobalSearchMetadata Metadata { get; set; }
}

sealed class SpGlobalSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("commodity")]
	public string Commodity { get; set; }

	[JsonProperty("contract_type")]
	public string ContractType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("uom")]
	public string UnitOfMeasure { get; set; }

	[JsonProperty("delivery_region_basis")]
	public string DeliveryRegionBasis { get; set; }

	[JsonProperty("curve_code")]
	public string CurveCode { get; set; }

	[JsonProperty("mdc")]
	public string MarketDataCategory { get; set; }

	[JsonProperty("quotation_style")]
	public string QuotationStyle { get; set; }

	[JsonProperty("assessment_frequency")]
	public string AssessmentFrequency { get; set; }

	[JsonProperty("modified_date")]
	public string ModifiedDate { get; set; }
}

sealed class SpGlobalSearchMetadata
{
	[JsonProperty("total_pages")]
	public int TotalPages { get; set; }

	[JsonProperty("totalPages")]
	public int TotalPagesCamel { get; set; }

	[JsonProperty("page")]
	public int Page { get; set; }

	[JsonProperty("pageNumber")]
	public int PageNumber { get; set; }

	[JsonProperty("total_records")]
	public long TotalRecords { get; set; }

	[JsonProperty("totalRecords")]
	public long TotalRecordsCamel { get; set; }

	public int GetTotalPages() => TotalPages > 0 ? TotalPages : TotalPagesCamel;
}

sealed class SpGlobalSymbolQuery
{
	public string Query { get; init; }
	public string Symbol { get; init; }
	public string Commodity { get; init; }
	public string ContractType { get; init; }
	public string MarketDataCategory { get; init; }
	public string AssessmentFrequency { get; init; }
	public int Page { get; init; } = 1;
	public int PageSize { get; init; } = 1000;

	public string ToQueryString()
	{
		var query = $"page={Page.Max(1)}&pageSize={PageSize.Min(1000).Max(1)}";
		if (!Query.IsEmpty())
			query += $"&q={Uri.EscapeDataString(Query)}";

		var filters = new List<string>();
		AddFilter(filters, "symbol", Symbol);
		AddFilter(filters, "commodity", Commodity);
		AddFilter(filters, "contract_type", ContractType);
		AddFilter(filters, "mdc", MarketDataCategory);
		AddFilter(filters, "assessment_frequency", AssessmentFrequency);
		if (filters.Count > 0)
			query += $"&filter={Uri.EscapeDataString(filters.Join(" AND "))}";
		return query;
	}

	private static void AddFilter(ICollection<string> filters, string field, string value)
	{
		if (!value.IsEmpty())
			filters.Add($"{field}: \"{EscapeFilterValue(value)}\"");
	}

	private static string EscapeFilterValue(string value)
		=> value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
