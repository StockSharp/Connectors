namespace StockSharp.ThetaData.Native.Model;

sealed class ThetaSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class ThetaOptionContract
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("expiration")]
	public string Expiration { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("right")]
	public string Right { get; set; }
}
