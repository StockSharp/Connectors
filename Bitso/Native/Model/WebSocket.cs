namespace StockSharp.Bitso.Native.Model;

enum BitsoWsChannels
{
	[EnumMember(Value = "ka")]
	KeepAlive,

	[EnumMember(Value = "trades")]
	Trades,

	[EnumMember(Value = "orders")]
	Orders,

	[EnumMember(Value = "diff-orders")]
	DiffOrders,
}

enum BitsoWsActions
{
	[EnumMember(Value = "subscribe")]
	Subscribe,
}

sealed class BitsoWsSubscriptionRequest
{
	[JsonProperty("action")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoWsActions Action { get; init; }

	[JsonProperty("book")]
	public string Book { get; init; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoWsChannels Channel { get; init; }
}

sealed class BitsoWsHeader
{
	[JsonProperty("action")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoWsActions? Action { get; set; }

	[JsonProperty("response")]
	public string Response { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoWsChannels? Channel { get; set; }
}

sealed class BitsoWsEnvelope<TPayload>
{
	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoWsChannels Channel { get; set; }

	[JsonProperty("book")]
	public string Book { get; set; }

	[JsonProperty("payload")]
	public TPayload Payload { get; set; }

	[JsonProperty("sent")]
	public long Sent { get; set; }

	[JsonProperty("sequence")]
	public long? Sequence { get; set; }
}

sealed class BitsoWsTrade
{
	[JsonProperty("i")]
	public string TradeId { get; set; }

	[JsonProperty("a")]
	public decimal Amount { get; set; }

	[JsonProperty("r")]
	public decimal Price { get; set; }

	[JsonProperty("v")]
	public decimal Value { get; set; }

	[JsonProperty("mo")]
	public string MakerOrderId { get; set; }

	[JsonProperty("to")]
	public string TakerOrderId { get; set; }

	[JsonProperty("t")]
	public int TakerSide { get; set; }

	[JsonProperty("x")]
	public long CreatedAt { get; set; }
}

sealed class BitsoWsOrder
{
	[JsonProperty("o")]
	public string OrderId { get; set; }

	[JsonProperty("r")]
	public decimal Price { get; set; }

	[JsonProperty("a")]
	public decimal? Amount { get; set; }

	[JsonProperty("v")]
	public decimal? Value { get; set; }

	[JsonProperty("t")]
	public int Side { get; set; }

	[JsonProperty("d")]
	public long CreatedAt { get; set; }

	[JsonProperty("z")]
	public long? UpdatedAt { get; set; }

	[JsonProperty("s")]
	public string Status { get; set; }
}

sealed class BitsoWsOrders
{
	[JsonProperty("bids")]
	public BitsoWsOrder[] Bids { get; set; }

	[JsonProperty("asks")]
	public BitsoWsOrder[] Asks { get; set; }
}
