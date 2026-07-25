namespace StockSharp.BingX.Native.Spot.Model;

class SymbolsResponse
{
	[JsonProperty("symbols")]
	public Symbol[] Symbols { get; set; }
}

class Symbol
{
	[JsonProperty("symbol")]
	public string Id { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("minQty")]
	public double? MinQty { get; set; }

	[JsonProperty("maxQty")]
	public double? MaxQty { get; set; }

	[JsonProperty("minNotional")]
	public double? MinNotional { get; set; }

	[JsonProperty("maxNotional")]
	public double? MaxNotional { get; set; }

	[JsonProperty("tickSize")]
	public double? TickSize { get; set; }

	[JsonProperty("stepSize")]
	public double? StepSize { get; set; }

	[JsonProperty("apiStateBuy")]
	public bool ApiStateBuy { get; set; }

	[JsonProperty("apiStateSell")]
	public bool ApiStateSell { get; set; }

	/// <summary>
	/// The venue does not send the base currency separately, it is the left part of the pair.
	/// </summary>
	[JsonIgnore]
	public string BaseAsset => Id?.Split('-').FirstOrDefault();
}
