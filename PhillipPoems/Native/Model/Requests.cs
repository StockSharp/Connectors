namespace StockSharp.PhillipPoems.Native.Model;

[DataContract]
enum PoemsOrderActions
{
	[EnumMember(Value = "1")]
	Buy = 1,

	[EnumMember(Value = "2")]
	Sell = 2,

	[EnumMember(Value = "5")]
	ShortSell = 5,
}

[DataContract]
enum PoemsOrderTypes
{
	[EnumMember(Value = "LO")]
	Limit,

	[EnumMember(Value = "SLO")]
	StopLimit,

	[EnumMember(Value = "LIT")]
	LimitIfTouched,
}

[DataContract]
enum PoemsTriggerPriceTypes
{
	[EnumMember(Value = "2")]
	LastDone = 2,
}

[DataContract]
enum PoemsValidityTypes
{
	[EnumMember(Value = "0")]
	Day = 0,
}

[DataContract]
abstract class PoemsRequest
{
	[JsonProperty("language")]
	public int Language { get; set; } = 1;

	[JsonProperty("osVersion")]
	public string OsVersion { get; set; } = "StockSharp";
}

[DataContract]
sealed class PoemsCommonRequest : PoemsRequest
{
}

[DataContract]
sealed class PoemsTokenRequest
{
	[JsonProperty("grant_type")]
	public string GrantType { get; set; }

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }

	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("client_secret")]
	public string ClientSecret { get; set; }
}

[DataContract]
sealed class PoemsSecuritySearchRequest : PoemsRequest
{
	[JsonProperty("productFlag")]
	public int ProductFlag { get; set; } = 2;

	[JsonProperty("module")]
	public int Module { get; set; }

	[JsonProperty("keyword")]
	public string Keyword { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("count")]
	public int Count { get; set; } = 100;
}

[DataContract]
sealed class PoemsCounterInfoRequest : PoemsRequest
{
	[JsonProperty("counterID")]
	public string CounterId { get; set; }

	[JsonProperty("priceMode")]
	public int PriceMode { get; set; } = 2;
}

[DataContract]
sealed class PoemsPriceListRequest : PoemsRequest
{
	[JsonProperty("counterIDs")]
	public string CounterIds { get; set; }

	[JsonProperty("size")]
	public int Size { get; set; }
}

[DataContract]
sealed class PoemsTimeSalesRequest : PoemsRequest
{
	[JsonProperty("counterID")]
	public string CounterId { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }

	[JsonProperty("to")]
	public string To { get; set; }

	[JsonProperty("size")]
	public int Size { get; set; } = 100;

	[JsonProperty("page")]
	public int Page { get; set; } = 1;
}

[DataContract]
sealed class PoemsMarketDepthRequest : PoemsRequest
{
	[JsonProperty("counterID")]
	public string CounterId { get; set; }
}

[DataContract]
sealed class PoemsAccountRequest : PoemsRequest
{
	[JsonProperty("accountType")]
	public string AccountType { get; set; }
}

[DataContract]
sealed class PoemsOrderRequest : PoemsRequest
{
	[JsonProperty("counterID")]
	public string CounterId { get; set; }

	[JsonProperty("action")]
	public PoemsOrderActions Action { get; set; }

	[JsonProperty("orderType")]
	public PoemsOrderTypes OrderType { get; set; }

	[JsonProperty("limitPrice")]
	public decimal LimitPrice { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("settlementCurrency")]
	public string SettlementCurrency { get; set; }

	[JsonProperty("payment")]
	public PhillipPoemsPaymentModes Payment { get; set; }

	[JsonProperty("triggerPriceType")]
	public PoemsTriggerPriceTypes? TriggerPriceType { get; set; }

	[JsonProperty("validity")]
	public PoemsValidityTypes Validity { get; set; } = PoemsValidityTypes.Day;

	[JsonProperty("gtd")]
	public string GoodTillDate { get; set; }
}

[DataContract]
sealed class PoemsAmendOrderRequest : PoemsRequest
{
	[JsonProperty("counterID")]
	public string CounterId { get; set; }

	[JsonProperty("amendQty")]
	public long Quantity { get; set; }
}

[DataContract]
sealed class PoemsCancelOrderRequest : PoemsRequest
{
	[JsonProperty("counterID")]
	public string CounterId { get; set; }
}
