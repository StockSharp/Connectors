namespace StockSharp.Kraken.Native.Spot.Model;

class OpenOrders
{
	[JsonProperty("open")]
	public Dictionary<string, OrderInfo> Open { get; set; }
}

class ClosedOrders
{
	[JsonProperty("closed")]
	public Dictionary<string, OrderInfo> Closed { get; set; }

	[JsonProperty("count")]
	public int Count { get; set; }
}

class OrderInfo
{
	[JsonProperty("refid")]
	public string RefId { get; set; }

	[JsonProperty("userref")]
	public long? UserRef { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("opentm")]
	public double OpenTime { get; set; }

	[JsonProperty("starttm")]
	public double StartTime { get; set; }

	[JsonProperty("expiretm")]
	public double ExpireTime { get; set; }

	[JsonProperty("closetm")]
	public double CloseTime { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; } // Nullable

	[JsonProperty("descr")]
	public OrderDescription Description { get; set; }

	[JsonProperty("vol")]
	public decimal Volume { get; set; }

	[JsonProperty("vol_exec")]
	public decimal VolumeExecuted { get; set; }

	[JsonProperty("cost")]
	public decimal? Cost { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("stopprice")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("limitprice")]
	public decimal? LimitPrice { get; set; }

	[JsonProperty("misc")]
	public string Misc { get; set; }

	[JsonProperty("oflags")]
	public string OrderFlags { get; set; }

	[JsonProperty("trades")]
	public string[] Trades { get; set; }
}

class OrderDescription
{
	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("ordertype")]
	public string OrderType { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("price2")]
	public decimal? Price2 { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("order")]
	public string Order { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }
}