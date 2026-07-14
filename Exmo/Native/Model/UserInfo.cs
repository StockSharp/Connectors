namespace StockSharp.Exmo.Native.Model;

class UserInfo
{
	[JsonProperty("uid")]
	public int Uid { get; set; }

	[JsonProperty("server_date")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime ServerDate { get; set; }

	[JsonProperty("balances")]
	public JObject Balances { get; set; }

	[JsonProperty("reserved")]
	public JObject Reserved { get; set; }
}