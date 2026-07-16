namespace StockSharp.Etoro.Native.Model;

sealed class EtoroSocketRequest<T>
{
	[JsonProperty("id")]
	public Guid Id { get; set; }

	[JsonProperty("operation")]
	public EtoroSocketOperations Operation { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }
}

sealed class EtoroSocketAuthentication
{
	[JsonProperty("userKey")]
	public string UserKey { get; set; }

	[JsonProperty("apiKey")]
	public string ApiKey { get; set; }
}

sealed class EtoroSocketSubscription
{
	[JsonProperty("topics")]
	public string[] Topics { get; set; }

	[JsonProperty("snapshot")]
	public bool IsSnapshot { get; set; }
}

sealed class EtoroSocketUnsubscription
{
	[JsonProperty("topics")]
	public string[] Topics { get; set; }
}

sealed class EtoroSocketEnvelope
{
	[JsonProperty("id")]
	public Guid? Id { get; set; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("operation")]
	public EtoroSocketOperations? Operation { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("messages")]
	public EtoroSocketMessage[] Messages { get; set; }
}

sealed class EtoroSocketMessage
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("content")]
	public string Content { get; set; }

	[JsonProperty("id")]
	public Guid Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

sealed class EtoroPrivateUpdate
{
	[JsonProperty("OrderID")]
	public long OrderId { get; set; }

	[JsonProperty("OrderType")]
	public int OrderType { get; set; }

	[JsonProperty("CID")]
	public long ClientId { get; set; }

	[JsonProperty("StatusID")]
	public EtoroOrderStatusIds StatusId { get; set; }

	[JsonProperty("InstrumentID")]
	public int InstrumentId { get; set; }

	[JsonProperty("PositionID")]
	public long PositionId { get; set; }

	[JsonProperty("UnitsToDeduct")]
	public decimal UnitsToDeduct { get; set; }

	[JsonProperty("RequestGuid")]
	public Guid? RequestGuid { get; set; }

	[JsonProperty("RequestOccurred")]
	public DateTime RequestOccurred { get; set; }

	[JsonProperty("RequestToken")]
	public Guid? RequestToken { get; set; }

	[JsonProperty("ErrorCode")]
	public int ErrorCode { get; set; }

	[JsonProperty("ErrorMessage")]
	public string ErrorMessage { get; set; }

	[JsonProperty("RequestedUnits")]
	public decimal RequestedUnits { get; set; }

	[JsonProperty("ExecutedUnits")]
	public decimal ExecutedUnits { get; set; }

	[JsonProperty("EndRate")]
	public decimal EndRate { get; set; }

	[JsonProperty("NetProfit")]
	public decimal NetProfit { get; set; }

	[JsonProperty("PendingClosePositionIDs")]
	public long[] PendingClosePositionIds { get; set; }

	[JsonProperty("OpenDateTime")]
	public DateTime OpenDateTime { get; set; }

	[JsonProperty("IsInMirror")]
	public bool IsInMirror { get; set; }

	[JsonProperty("TotalExternalFees")]
	public decimal TotalExternalFees { get; set; }

	[JsonProperty("TotalExternalTaxes")]
	public decimal TotalExternalTaxes { get; set; }

	[JsonProperty("RequestedLots")]
	public decimal RequestedLots { get; set; }

	[JsonProperty("ExecutedLots")]
	public decimal ExecutedLots { get; set; }
}

[DataContract]
enum EtoroSocketOperations
{
	[EnumMember(Value = "Authenticate")]
	Authenticate,

	[EnumMember(Value = "Subscribe")]
	Subscribe,

	[EnumMember(Value = "Unsubscribe")]
	Unsubscribe,
}
