namespace StockSharp.Okex.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = false)]
[JsonConverter(typeof(JArrayToObjectConverter))]
class Ohlc
{
	public long Time { get; set; }

	public decimal Open { get; set; }

	public decimal High { get; set; }

	public decimal Low { get; set; }

	public decimal Close { get; set; }

	public decimal Volume { get; set; }

	public decimal VolCcy { get; set; }

	public decimal VolCcyQuote { get; set; }

	public int Comfirm { get; set; }
}