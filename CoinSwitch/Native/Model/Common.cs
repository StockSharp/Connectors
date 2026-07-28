namespace StockSharp.CoinSwitch.Native.Model;

sealed class CoinSwitchEnvelope
{
	[JsonProperty("data")]
	public JToken Data { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public JToken Error { get; set; }
}

sealed class CoinSwitchHftEnvelope
{
	[JsonProperty("retCode")]
	public int ReturnCode { get; set; }

	[JsonProperty("retMsg")]
	public string ReturnMessage { get; set; }

	[JsonProperty("result")]
	public JToken Result { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }
}

sealed class CoinSwitchOrderList<T>
{
	[JsonProperty("orders")]
	public T[] Orders { get; set; }

	[JsonProperty("cursor")]
	public long? Cursor { get; set; }
}

sealed class CoinSwitchHftList<T>
{
	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("list")]
	public T[] Values { get; set; }

	[JsonProperty("nextPageCursor")]
	public string NextPageCursor { get; set; }
}

sealed class CoinSwitchMarket
{
	public string NativeSymbol { get; init; }

	public string SecurityCode { get; init; }

	public string BaseCurrency { get; init; }

	public string QuoteCurrency { get; init; }

	public SecurityTypes SecurityType { get; init; }

	public SecurityStates State { get; init; }

	public decimal? PriceStep { get; init; }

	public decimal? VolumeStep { get; init; }

	public decimal? MinimumVolume { get; init; }

	public decimal? MaximumVolume { get; init; }

	public DateTime? ExpiryDate { get; init; }

	public OptionTypes? OptionType { get; init; }

	public decimal? Strike { get; init; }

	public SecurityId ToSecurityId()
		=> new()
		{
			SecurityCode = SecurityCode,
			BoardCode = BoardCodes.CoinSwitch,
		};
}
