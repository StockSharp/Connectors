namespace StockSharp.Alpaca.Native.Model;

class BaseEntity
{
	[JsonProperty("c")]
	public string[] Conditions { get; set; }

	[JsonProperty("z")]
	public string Tape { get; set; }

	[JsonProperty("t")]
	public DateTime Time { get; set; }
}
