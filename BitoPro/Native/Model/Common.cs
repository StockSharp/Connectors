namespace StockSharp.BitoPro.Native.Model;

sealed class BitoProDataResponse<TData>
{
	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class BitoProError
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}
