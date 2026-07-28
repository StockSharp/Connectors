namespace StockSharp.AscendEx.Native.Model;

sealed class AscendExResponse<TData>
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class AscendExError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }
}

sealed class AscendExAccountInfo
{
	[JsonProperty("accountGroup")]
	public int AccountGroup { get; set; }

	[JsonProperty("userUID")]
	public string UserId { get; set; }
}
