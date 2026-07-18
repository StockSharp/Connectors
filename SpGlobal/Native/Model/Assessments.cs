namespace StockSharp.SpGlobal.Native.Model;

sealed class SpGlobalAssessmentResponse
{
	[JsonProperty("results")]
	public SpGlobalAssessmentResult[] Results { get; set; }

	[JsonProperty("metadata")]
	public SpGlobalAssessmentMetadata Metadata { get; set; }
}

sealed class SpGlobalAssessmentResult
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("data")]
	public SpGlobalAssessment[] Data { get; set; }
}

sealed class SpGlobalAssessment
{
	[JsonProperty("bate")]
	public string Bate { get; set; }

	[JsonProperty("value")]
	public decimal? Value { get; set; }

	[JsonProperty("assessDate")]
	public string AssessmentDate { get; set; }

	[JsonProperty("modDate")]
	public string ModifiedDate { get; set; }

	[JsonProperty("isCorrected")]
	public bool? IsCorrected { get; set; }

	[JsonProperty("change")]
	public SpGlobalAssessmentChange Change { get; set; }

	public DateTime? GetTime()
	{
		var value = AssessmentDate.IsEmpty(ModifiedDate);
		if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
			return null;
		return time.UtcDateTime;
	}
}

sealed class SpGlobalAssessmentChange
{
	[JsonProperty("deltaPrice")]
	public decimal? DeltaPrice { get; set; }

	[JsonProperty("deltaPercent")]
	public decimal? DeltaPercent { get; set; }

	[JsonProperty("pValue")]
	public decimal? PreviousValue { get; set; }

	[JsonProperty("pDate")]
	public string PreviousDate { get; set; }
}

sealed class SpGlobalAssessmentMetadata
{
	[JsonProperty("totalPages")]
	public int TotalPages { get; set; }

	[JsonProperty("total_pages")]
	public int TotalPagesSnake { get; set; }

	[JsonProperty("pageNumber")]
	public int PageNumber { get; set; }

	[JsonProperty("totalRecords")]
	public long TotalRecords { get; set; }

	public int GetTotalPages() => TotalPages > 0 ? TotalPages : TotalPagesSnake;
}

sealed class SpGlobalAssessmentQuery
{
	public string Symbol { get; init; }
	public string Bate { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }
	public int Page { get; set; } = 1;
	public int PageSize { get; init; } = 10000;

	public string ToQueryString(bool current)
	{
		var filters = new List<string>
		{
			$"symbol: \"{EscapeFilterValue(Symbol.ThrowIfEmpty(nameof(Symbol)))}\"",
		};
		if (!Bate.IsEmpty())
			filters.Add($"bate: \"{EscapeFilterValue(Bate)}\"");
		if (!current && From != null)
			filters.Add($"assessDate >= \"{From:yyyy-MM-dd}\"");
		if (!current && To != null)
			filters.Add($"assessDate <= \"{To:yyyy-MM-dd}\"");

		var query = $"filter={Uri.EscapeDataString(filters.Join(" AND "))}" +
			$"&page={Page.Max(1)}&pageSize={PageSize.Min(10000).Max(1)}";
		if (current)
			query += "&field=deltaPrice%2CdeltaPercent%2CpValue%2CpDate";
		return query;
	}

	private static string EscapeFilterValue(string value)
		=> value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
