namespace StockSharp.CoinCatch.Native.Model;

sealed class CoinCatchResponse<TData>
{
	public const string SuccessCode = "00000";

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("message")]
	private string AlternativeMessage
	{
		set
		{
			if (!value.IsEmpty())
				Message = value;
		}
	}

	[JsonProperty("requestTime")]
	public long RequestTime { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonIgnore]
	public bool IsSuccess => Code.EqualsIgnoreCase(SuccessCode);
}

sealed class CoinCatchWsArgument
{
	[JsonProperty("instType")]
	public string InstrumentType { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("instId")]
	public string InstrumentId { get; set; }
}

sealed class CoinCatchWsPush<TData>
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("arg")]
	public CoinCatchWsArgument Argument { get; set; }

	[JsonProperty("data")]
	public TData[] Data { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class CoinCatchWsEvent
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("arg")]
	public CoinCatchWsArgument Argument { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}
