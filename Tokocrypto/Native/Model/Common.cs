namespace StockSharp.Tokocrypto.Native.Model;

sealed class TokocryptoResponse<TData>
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("message")]
	private string AlternateMessage
	{
		set
		{
			if (!value.IsEmpty())
				Message = value;
		}
	}

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class TokocryptoList<TItem>
{
	[JsonProperty("list")]
	public TItem[] List { get; set; }
}
