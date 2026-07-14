namespace StockSharp.Kucoin.Native.Spot.Model;

class PublicInfoServer
{
	[JsonProperty("pingInterval")]
	public int PingInterval { get; set; }

	[JsonProperty("endpoint")]
	public string Endpoint { get; set; }

	[JsonProperty("protocol")]
	public string Protocol { get; set; }

	[JsonProperty("encrypt")]
	public bool Encrypt { get; set; }

	[JsonProperty("pingTimeout")]
	public int? PingTimeout { get; set; }
}

class PublicInfo
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("instanceServers")]
	public PublicInfoServer[] InstanceServers { get; set; }

	[JsonProperty("historyServers")]
	public PublicInfoServer[] HistoryServers { get; set; }
}

class PrivateInfo
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("instanceServers")]
	public PublicInfoServer[] InstanceServers { get; set; }

	[JsonProperty("historyServers")]
	public PublicInfoServer[] HistoryServers { get; set; }
}