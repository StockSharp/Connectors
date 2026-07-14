namespace StockSharp.DXtrade.Native.Model;

class Balance
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("version")]
	public int Version { get; set; }

	[JsonProperty("value")]
	public double? Value { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}