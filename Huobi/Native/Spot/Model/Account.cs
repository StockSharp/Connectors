namespace StockSharp.Huobi.Native.Spot.Model;

class Account
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("user-id")]
	public long? UserId { get; set; }

	[JsonProperty("list")]
	public Balance[] Balances { get; set; }
}