namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Position
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("quantity")]
	public double Quantity { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("cost_basis")]
	public double? CostBasis { get; set; }

	[JsonProperty("date_acquired")]
	public DateTime DateAcquired { get; set; }
}