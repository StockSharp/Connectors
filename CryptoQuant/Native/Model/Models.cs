namespace StockSharp.CryptoQuant.Native.Model;

sealed class CryptoQuantResponse<TItem>
{
	[JsonProperty("status")]
	public CryptoQuantStatus Status { get; set; }

	[JsonProperty("result")]
	public CryptoQuantResult<TItem> Result { get; set; }
}

sealed class CryptoQuantErrorResponse
{
	[JsonProperty("status")]
	public CryptoQuantStatus Status { get; set; }
}

sealed class CryptoQuantStatus
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public CryptoQuantStatuses Message { get; set; }
}

sealed class CryptoQuantResult<TItem>
{
	[JsonProperty("window")]
	public CryptoQuantWindows Window { get; set; }

	[JsonProperty("data")]
	public TItem[] Data { get; set; }
}

sealed class CryptoQuantEndpoint
{
	[JsonProperty("path")]
	public string Path { get; set; }

	[JsonProperty("parameters")]
	public CryptoQuantEndpointParameter[] Parameters { get; set; }
}

sealed class CryptoQuantEndpointParameter
{
	[JsonProperty("token")]
	public string[] Token { get; set; }

	[JsonProperty("window")]
	public CryptoQuantWindows[] Window { get; set; }
}

sealed class CryptoQuantOhlcv
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("datetime")]
	public string DateTime { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonIgnore]
	public bool IsComplete => Open is not null && High is not null &&
		Low is not null && Close is not null;
}
