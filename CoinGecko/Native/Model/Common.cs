namespace StockSharp.CoinGecko.Native.Model;

sealed class CoinGeckoPing
{
	[JsonProperty("gecko_says")]
	public string Message { get; set; }
}

sealed class CoinGeckoErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("status")]
	public CoinGeckoErrorStatus Status { get; set; }
}

sealed class CoinGeckoErrorStatus
{
	[JsonProperty("error_code")]
	public int? Code { get; set; }

	[JsonProperty("error_message")]
	public string Message { get; set; }
}

sealed class CoinGeckoCoin
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}
