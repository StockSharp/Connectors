namespace StockSharp.SynFutures.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesSocketHeader
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("result")]
	public string Result { get; set; }

	[JsonProperty("stream")]
	public string Stream { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesSocketRequest<T>
{
	[JsonProperty("id", Order = 1)]
	public long Id { get; init; }

	[JsonProperty("method", Order = 2)]
	public string Method { get; init; }

	[JsonProperty("params", Order = 3)]
	public T Parameters { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
class SynFuturesSocketPairParameters
{
	[JsonProperty("chainId", Order = 1)]
	public int ChainId { get; init; }

	[JsonProperty("instrument", Order = 2)]
	public string Instrument { get; init; }

	[JsonProperty("expiry", Order = 3)]
	public uint Expiry { get; init; }

	[JsonProperty("type", Order = 4)]
	public string Type { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesSocketTradesParameters
{
	[JsonProperty("chainId", Order = 1)]
	public int ChainId { get; init; }

	[JsonProperty("pairs", Order = 2)]
	public string[] Pairs { get; init; }

	[JsonProperty("type", Order = 3)]
	public string Type { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesSocketKlineParameters : SynFuturesSocketPairParameters
{
	[JsonProperty("interval", Order = 5)]
	public string Interval { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesSocketPortfolioParameters
{
	[JsonProperty("chainId", Order = 1)]
	public int ChainId { get; init; }

	[JsonProperty("userAddress", Order = 2)]
	public string UserAddress { get; init; }

	[JsonProperty("type", Order = 3)]
	public string Type { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesSocketEnvelope<T>
{
	[JsonProperty("stream")]
	public string Stream { get; set; }

	[JsonProperty("chainId")]
	public int ChainId { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("expiry")]
	public uint Expiry { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesPortfolioNotification
{
	[JsonProperty("chainId")]
	public int ChainId { get; set; }

	[JsonProperty("userAddress")]
	public string UserAddress { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("expiry")]
	public uint Expiry { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("orderInfo")]
	public SynFuturesOrderNotification OrderInfo { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesOrderNotification
{
	[JsonProperty("oid")]
	public long OrderId { get; set; }

	[JsonProperty("orderId")]
	public string OrderStringId { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}
