namespace StockSharp.EdgeX.Native.Derivatives.Model;

sealed class Contract
{
	[JsonProperty("contractId")]
	public string Id { get; set; }

	[JsonProperty("contractName")]
	public string Name { get; set; }

	[JsonProperty("baseCoinId")]
	public string BaseCoinId { get; set; }

	[JsonProperty("quoteCoinId")]
	public string QuoteCoinId { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }

	[JsonProperty("minOrderSize")]
	public string MinOrderSize { get; set; }

	[JsonProperty("maxOrderSize")]
	public string MaxOrderSize { get; set; }

	[JsonProperty("enableTrade")]
	public bool? EnableTrade { get; set; }

	[JsonProperty("enableDisplay")]
	public bool? EnableDisplay { get; set; }
}
