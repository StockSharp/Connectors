namespace StockSharp.Usmart.Native.Model;

[DataContract]
enum UsmartExchangeTypes
{
	[EnumMember(Value = "0")]
	HongKong = 0,

	[EnumMember(Value = "5")]
	UnitedStates = 5,

	[EnumMember(Value = "6")]
	ShanghaiConnect = 6,

	[EnumMember(Value = "7")]
	ShenzhenConnect = 7,

	[EnumMember(Value = "67")]
	China = 67,

	[EnumMember(Value = "100")]
	All = 100,
}

[DataContract]
enum UsmartOrderSides
{
	[EnumMember(Value = "0")]
	Buy = 0,

	[EnumMember(Value = "1")]
	Sell = 1,
}

[DataContract]
enum UsmartModifyActions
{
	[EnumMember(Value = "0")]
	Cancel = 0,

	[EnumMember(Value = "1")]
	Modify = 1,
}

[DataContract]
sealed class UsmartMarketRequest
{
	[JsonProperty("market")]
	public string Market { get; set; }
}

[DataContract]
sealed class UsmartSecuritiesRequest
{
	[JsonProperty("secuIds")]
	public string[] SecurityIds { get; set; }
}

[DataContract]
sealed class UsmartSecurityRequest
{
	[JsonProperty("secuId")]
	public string SecurityId { get; set; }
}

[DataContract]
sealed class UsmartKlineRequest
{
	[JsonProperty("secuId")]
	public string SecurityId { get; set; }

	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("start")]
	public long Start { get; set; }

	[JsonProperty("right")]
	public int Adjustment { get; set; }

	[JsonProperty("count")]
	public int Count { get; set; }
}

[DataContract]
sealed class UsmartTickRequest
{
	[JsonProperty("secuId")]
	public string SecurityId { get; set; }

	[JsonProperty("tradeTime")]
	public long TradeTime { get; set; }

	[JsonProperty("seq")]
	public long Sequence { get; set; }

	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("sortDirection")]
	public int SortDirection { get; set; } = 1;
}

[DataContract]
sealed class UsmartPlaceOrderRequest
{
	[JsonProperty("serialNo")]
	public long SerialNo { get; set; }

	[JsonProperty("entrustAmount")]
	public decimal Quantity { get; set; }

	[JsonProperty("entrustPrice")]
	public decimal Price { get; set; }

	[JsonProperty("entrustProp")]
	public UsmartOrderInstructions Instruction { get; set; }

	[JsonProperty("entrustType")]
	public UsmartOrderSides Side { get; set; }

	[JsonProperty("exchangeType")]
	public UsmartExchangeTypes Exchange { get; set; }

	[JsonProperty("stockCode")]
	public string StockCode { get; set; }

	[JsonProperty("password")]
	public string EncryptedPassword { get; set; }

	[JsonProperty("forceEntrustFlag")]
	public bool ForceOrder { get; set; }

	[JsonProperty("sessionType")]
	public UsmartTradingSessions Session { get; set; }
}

[DataContract]
sealed class UsmartFractionalOrderRequest
{
	[JsonProperty("entrustAmount")]
	public decimal Quantity { get; set; }

	[JsonProperty("entrustPrice")]
	public decimal Price { get; set; }

	[JsonProperty("entrustType")]
	public UsmartOrderSides Side { get; set; }

	[JsonProperty("exchangeType")]
	public UsmartExchangeTypes Exchange { get; set; }

	[JsonProperty("stockCode")]
	public string StockCode { get; set; }
}

[DataContract]
sealed class UsmartModifyOrderRequest
{
	[JsonProperty("actionType")]
	public UsmartModifyActions Action { get; set; }

	[JsonProperty("entrustAmount")]
	public decimal Quantity { get; set; }

	[JsonProperty("entrustId")]
	public long OrderId { get; set; }

	[JsonProperty("entrustPrice")]
	public decimal Price { get; set; }

	[JsonProperty("password")]
	public string EncryptedPassword { get; set; }

	[JsonProperty("forceEntrustFlag")]
	public bool ForceOrder { get; set; }
}

[DataContract]
sealed class UsmartFractionalCancelRequest
{
	[JsonProperty("actionType")]
	public UsmartModifyActions Action { get; set; }

	[JsonProperty("oddId")]
	public long OrderId { get; set; }
}

[DataContract]
class UsmartPagedRequest
{
	[JsonProperty("exchangeType")]
	public UsmartExchangeTypes Exchange { get; set; } = UsmartExchangeTypes.All;

	[JsonProperty("pageNum")]
	public int Page { get; set; } = 1;

	[JsonProperty("pageSize")]
	public int PageSize { get; set; } = 100;

	[JsonProperty("stockCode")]
	public string StockCode { get; set; }
}

[DataContract]
sealed class UsmartRecordsRequest : UsmartPagedRequest
{
	[JsonProperty("entrustId")]
	public long? OrderId { get; set; }

	[JsonProperty("beginTime")]
	public string BeginTime { get; set; }

	[JsonProperty("endTime")]
	public string EndTime { get; set; }
}

[DataContract]
sealed class UsmartExchangeRequest
{
	[JsonProperty("exchangeType")]
	public UsmartExchangeTypes Exchange { get; set; } = UsmartExchangeTypes.All;
}
