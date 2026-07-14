namespace StockSharp.Alpaca.Native.Model;

class Tick : BaseEntity
{
	[JsonProperty("i")]
	public long Id { get; set; }

	[JsonProperty("s")]
	public double Size { get; set; }

	[JsonProperty("p")]
	public double Price { get; set; }

	[JsonProperty("x")]
	public string Exchange { get; set; }

	[JsonProperty("vw")]
	public double? AvgPrice { get; set; }

	[JsonProperty("tks")]
	public string Side { get; set; }
}