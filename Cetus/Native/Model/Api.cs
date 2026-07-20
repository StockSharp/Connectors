namespace StockSharp.Cetus.Native.Model;

sealed class CetusApiResponse<TData>
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class CetusQuoteData
{
	[JsonProperty("request_id")]
	public string RequestId { get; set; }

	[JsonProperty("amount_in")]
	public ulong InputAmount { get; set; }

	[JsonProperty("amount_out")]
	public ulong OutputAmount { get; set; }

	[JsonProperty("deviation_ratio")]
	public string DeviationRatio { get; set; }

	[JsonProperty("paths")]
	public CetusQuotePath[] Paths { get; set; }

	[JsonProperty("gas")]
	public ulong Gas { get; set; }
}

sealed class CetusQuotePath
{
	[JsonProperty("id")]
	public string PoolId { get; set; }

	[JsonProperty("provider")]
	public CetusProviders Provider { get; set; }

	[JsonProperty("from")]
	public string InputCoinType { get; set; }

	[JsonProperty("target")]
	public string OutputCoinType { get; set; }

	[JsonProperty("direction")]
	public bool IsAToB { get; set; }

	[JsonProperty("fee_rate")]
	public string FeeRate { get; set; }

	[JsonProperty("lot_size")]
	public ulong LotSize { get; set; }

	[JsonProperty("amount_in")]
	public ulong InputAmount { get; set; }

	[JsonProperty("amount_out")]
	public ulong OutputAmount { get; set; }

	[JsonProperty("published_at")]
	public string PublishedAt { get; set; }
}
