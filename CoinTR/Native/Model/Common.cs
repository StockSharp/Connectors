namespace StockSharp.CoinTR.Native.Model;

sealed class CoinTRResponse<TData>
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
	public bool IsSuccess
		=> Code.EqualsIgnoreCase(SuccessCode);
}

sealed class CoinTRWsArgument
{
	[JsonProperty("instType")]
	public string InstrumentType { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("instId")]
	public string InstrumentId { get; set; }

	[JsonProperty("coin")]
	public string Coin { get; set; }
}

sealed class CoinTRWsPush<TData>
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("arg")]
	public CoinTRWsArgument Argument { get; set; }

	[JsonProperty("data")]
	public TData[] Data { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class CoinTRWsEvent
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("arg")]
	public CoinTRWsArgument Argument { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}
