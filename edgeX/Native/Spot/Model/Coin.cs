namespace StockSharp.EdgeX.Native.Spot.Model;

sealed class Coin
{
	[JsonProperty("coinId")]
	public string Id { get; set; }

	[JsonProperty("coinName")]
	public string Name { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }

	[JsonProperty("showStepSize")]
	public string ShowStepSize { get; set; }
}
