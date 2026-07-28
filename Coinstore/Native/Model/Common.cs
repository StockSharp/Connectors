namespace StockSharp.Coinstore.Native.Model;

sealed class CoinstoreResponse<TData>
{
	[JsonProperty("code")]
	public JToken Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("msg")]
	private string LegacyMessage
	{
		set
		{
			if (!value.IsEmpty())
				Message = value;
		}
	}

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class CoinstoreError
{
	[JsonProperty("code")]
	public JToken Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("msg")]
	public string LegacyMessage { get; set; }
}
