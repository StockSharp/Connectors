namespace StockSharp.Bitbank.Native.Model;

class Account
{
	[JsonProperty("uuid")]
	public string Uuid { get; set; }

	[JsonProperty("label")]
	public string Label { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }
}