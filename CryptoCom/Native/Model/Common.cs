namespace StockSharp.CryptoCom.Native.Model;

class CryptoComResponseStatus
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("original")]
	public string Original { get; set; }
}

sealed class CryptoComResponse<TResult> : CryptoComResponseStatus
{
	[JsonProperty("result")]
	public TResult Result { get; set; }
}

sealed class CryptoComDataResult<TItem>
{
	[JsonProperty("data")]
	public TItem[] Data { get; set; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("depth")]
	public int? Depth { get; set; }

	[JsonProperty("next_cursor")]
	public string NextCursor { get; set; }
}

sealed class CryptoComEmptyResult
{
}
