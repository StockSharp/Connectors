namespace StockSharp.CoinSpot.Native.Model;

sealed class CoinSpotResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

