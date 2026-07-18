namespace StockSharp.NasdaqDataLink.Native.Model;

enum NasdaqDataLinkFrequencies
{
	Unknown,
	Daily,
	Weekly,
	Monthly,
	Quarterly,
	Annual,
}

enum NasdaqDataLinkOrders
{
	[EnumMember(Value = "asc")]
	Ascending,

	[EnumMember(Value = "desc")]
	Descending,
}

enum NasdaqDataLinkValueTypes
{
	Null,
	Number,
	Text,
	Boolean,
	Date,
}

sealed class NasdaqDataLinkDatasetResponse
{
	[JsonProperty("dataset")]
	public NasdaqDataLinkDataset Dataset { get; set; }
}

sealed class NasdaqDataLinkSearchResponse
{
	[JsonProperty("datasets")]
	public NasdaqDataLinkDataset[] Datasets { get; set; }

	[JsonProperty("meta")]
	public NasdaqDataLinkSearchMeta Meta { get; set; }
}

sealed class NasdaqDataLinkSearchMeta
{
	[JsonProperty("query")]
	public string Query { get; set; }

	[JsonProperty("current_page")]
	public int CurrentPage { get; set; }

	[JsonProperty("per_page")]
	public int PerPage { get; set; }

	[JsonProperty("total_pages")]
	public int TotalPages { get; set; }

	[JsonProperty("total_count")]
	public long TotalCount { get; set; }
}

sealed class NasdaqDataLinkDataset
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("dataset_code")]
	public string DatasetCode { get; set; }

	[JsonProperty("database_code")]
	public string DatabaseCode { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("refreshed_at")]
	public DateTime? RefreshedAt { get; set; }

	[JsonProperty("newest_available_date")]
	public DateTime? NewestAvailableDate { get; set; }

	[JsonProperty("oldest_available_date")]
	public DateTime? OldestAvailableDate { get; set; }

	[JsonProperty("column_names")]
	public string[] ColumnNames { get; set; }

	[JsonProperty("frequency")]
	[JsonConverter(typeof(NasdaqDataLinkFrequencyConverter))]
	public NasdaqDataLinkFrequencies Frequency { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("premium")]
	public bool IsPremium { get; set; }

	[JsonProperty("database_id")]
	public long? DatabaseId { get; set; }

	[JsonIgnore]
	public string Code => $"{DatabaseCode}/{DatasetCode}";
}

sealed class NasdaqDataLinkDataResponse
{
	[JsonProperty("dataset_data")]
	public NasdaqDataLinkDatasetData DatasetData { get; set; }
}

sealed class NasdaqDataLinkDatasetData
{
	[JsonProperty("limit")]
	public int? Limit { get; set; }

	[JsonProperty("transform")]
	public string Transform { get; set; }

	[JsonProperty("column_index")]
	public int? ColumnIndex { get; set; }

	[JsonProperty("column_names")]
	public string[] ColumnNames { get; set; }

	[JsonProperty("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonProperty("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonProperty("frequency")]
	[JsonConverter(typeof(NasdaqDataLinkFrequencyConverter))]
	public NasdaqDataLinkFrequencies Frequency { get; set; }

	[JsonProperty("data")]
	public NasdaqDataLinkRow[] Data { get; set; }

	[JsonProperty("order")]
	[JsonConverter(typeof(StringEnumConverter))]
	public NasdaqDataLinkOrders Order { get; set; }
}

sealed class NasdaqDataLinkErrorEnvelope
{
	[JsonProperty("quandl_error")]
	public NasdaqDataLinkError Error { get; set; }
}

sealed class NasdaqDataLinkError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
