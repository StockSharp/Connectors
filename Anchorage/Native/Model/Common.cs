namespace StockSharp.Anchorage.Native.Model;

sealed class AnchorageEnumConverter<TEnum> : JsonConverter<TEnum>
	where TEnum : struct, Enum
{
	private static readonly IReadOnlyDictionary<string, TEnum> _fromWire =
		Enum.GetValues<TEnum>().ToDictionary(GetWireValue,
			static value => value, StringComparer.OrdinalIgnoreCase);

	private static string GetWireValue(TEnum value)
	{
		var member = typeof(TEnum).GetMember(value.ToString())[0];
		return member.GetCustomAttribute<EnumMemberAttribute>()?.Value ??
			value.ToString();
	}

	public override TEnum ReadJson(JsonReader reader, Type objectType,
		TEnum existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = hasExistingValue;
		_ = serializer;
		var value = reader.Value?.ToString();
		return !value.IsEmpty() && _fromWire.TryGetValue(value, out var result)
			? result
			: existingValue;
	}

	public override void WriteJson(JsonWriter writer, TEnum value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteValue(GetWireValue(value));
	}
}

enum AnchorageSides
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "BUY")]
	Buy,
	[EnumMember(Value = "SELL")]
	Sell,
}

enum AnchorageTimeInForces
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "FOK")]
	FillOrKill,
	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,
	[EnumMember(Value = "GTC")]
	GoodTillCancel,
}

enum AnchorageOrderStatuses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "PENDING")]
	Pending,
	[EnumMember(Value = "PENDING_NEW")]
	PendingNew,
	[EnumMember(Value = "NEW")]
	New,
	[EnumMember(Value = "PARTIALLY_FILLED")]
	PartiallyFilled,
	[EnumMember(Value = "FILLED")]
	Filled,
	[EnumMember(Value = "REJECTED")]
	Rejected,
	[EnumMember(Value = "PENDING_CANCEL")]
	PendingCancel,
	[EnumMember(Value = "CANCELED")]
	Canceled,
}

enum AnchorageExecutionTypes
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "PENDING_NEW")]
	PendingNew,
	[EnumMember(Value = "NEW")]
	New,
	[EnumMember(Value = "FILL")]
	Fill,
	[EnumMember(Value = "CANCEL")]
	Cancel,
	[EnumMember(Value = "REJECT")]
	Reject,
	[EnumMember(Value = "REJECTED")]
	Rejected,
	[EnumMember(Value = "CANCEL_REJECT")]
	CancelReject,
}

enum AnchorageRejectReasons
{
	[EnumMember(Value = "Unknown")]
	Unknown,
	[EnumMember(Value = "InvalidTimeInForce")]
	InvalidTimeInForce,
	[EnumMember(Value = "InvalidSymbol")]
	InvalidSymbol,
	[EnumMember(Value = "InvalidCurrency")]
	InvalidCurrency,
	[EnumMember(Value = "InvalidSide")]
	InvalidSide,
	[EnumMember(Value = "InvalidQuantity")]
	InvalidQuantity,
	[EnumMember(Value = "InvalidOrderType")]
	InvalidOrderType,
	[EnumMember(Value = "InvalidAccount")]
	InvalidAccount,
	[EnumMember(Value = "AccountDisabledForTrading")]
	AccountDisabledForTrading,
	[EnumMember(Value = "UnauthorizedForTrading")]
	UnauthorizedForTrading,
	[EnumMember(Value = "InsufficientLiquidity")]
	InsufficientLiquidity,
	[EnumMember(Value = "DuplicateOrder")]
	DuplicateOrder,
	[EnumMember(Value = "ExecutionLimitExceeded")]
	ExecutionLimitExceeded,
	[EnumMember(Value = "InternalError")]
	InternalError,
	[EnumMember(Value = "InvalidPrice")]
	InvalidPrice,
	[EnumMember(Value = "InvalidLimitPrice")]
	InvalidLimitPrice,
	[EnumMember(Value = "InvalidTriggerPrice")]
	InvalidTriggerPrice,
	[EnumMember(Value = "OrderNotCancelable")]
	OrderNotCancelable,
	[EnumMember(Value = "InvalidAccountsInAllocation")]
	InvalidAccountsInAllocation,
	[EnumMember(Value = "InvalidCurrencyForFOK")]
	InvalidCurrencyForFillOrKill,
	[EnumMember(Value = "InvalidSideForSpecLotID")]
	InvalidSideForSpecificLot,
	[EnumMember(Value = "InvalidTimeInForceForSpecLotID")]
	InvalidTimeInForceForSpecificLot,
	[EnumMember(Value = "InvalidAllocation")]
	InvalidAllocation,
}

enum AnchorageTransferStatuses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "QUEUED")]
	Queued,
	[EnumMember(Value = "IN_PROGRESS")]
	InProgress,
	[EnumMember(Value = "COMPLETED")]
	Completed,
	[EnumMember(Value = "FAILED")]
	Failed,
}

enum AnchorageTransactionStatuses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "INITIATING")]
	Initiating,
	[EnumMember(Value = "NEEDS_APPROVAL")]
	NeedsApproval,
	[EnumMember(Value = "INPROGRESS")]
	InProgress,
	[EnumMember(Value = "SUCCESS")]
	Success,
	[EnumMember(Value = "FAILURE")]
	Failure,
	[EnumMember(Value = "REJECTED")]
	Rejected,
	[EnumMember(Value = "EXPIRED")]
	Expired,
}

enum AnchorageTransactionTypes
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "DEPOSIT")]
	Deposit,
	[EnumMember(Value = "WITHDRAW")]
	Withdraw,
	[EnumMember(Value = "TRANSFER")]
	Transfer,
	[EnumMember(Value = "STAKING_REWARD")]
	StakingReward,
	[EnumMember(Value = "RESTAKING_REWARD")]
	RestakingReward,
	[EnumMember(Value = "ALLUVIAL_STAKING_REWARD")]
	AlluvialStakingReward,
	[EnumMember(Value = "DELEGATION_REWARD")]
	DelegationReward,
	[EnumMember(Value = "MEV_REWARD")]
	MevReward,
	[EnumMember(Value = "PRIORITY_FEE_REWARD")]
	PriorityFeeReward,
	[EnumMember(Value = "FIAT_INTEREST")]
	FiatInterest,
	[EnumMember(Value = "MINT")]
	Mint,
	[EnumMember(Value = "BURN")]
	Burn,
	[EnumMember(Value = "DIEM_PREBURN")]
	DiemPreburn,
	[EnumMember(Value = "GAS_STATION")]
	GasStation,
	[EnumMember(Value = "OTHER")]
	Other,
}

enum AnchorageTradeStatuses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "PENDING")]
	Pending,
	[EnumMember(Value = "REJECTED")]
	Rejected,
	[EnumMember(Value = "CANCELED")]
	Canceled,
	[EnumMember(Value = "EXECUTED")]
	Executed,
	[EnumMember(Value = "SETTLING")]
	Settling,
	[EnumMember(Value = "SETTLED")]
	Settled,
	[EnumMember(Value = "PENDINGALLOCATION")]
	PendingAllocation,
	[EnumMember(Value = "ALLOCATED")]
	Allocated,
}

enum AnchorageErrorTypes
{
	[EnumMember(Value = "Unknown")]
	Unknown,
	[EnumMember(Value = "InternalError")]
	InternalError,
	[EnumMember(Value = "InvalidRequest")]
	InvalidRequest,
	[EnumMember(Value = "Unauthenticated")]
	Unauthenticated,
	[EnumMember(Value = "Forbidden")]
	Forbidden,
	[EnumMember(Value = "NotFound")]
	NotFound,
	[EnumMember(Value = "Conflict")]
	Conflict,
	[EnumMember(Value = "UnprocessableEntity")]
	UnprocessableEntity,
	[EnumMember(Value = "TooManyRequests")]
	TooManyRequests,
	[EnumMember(Value = "ServiceUnavailable")]
	ServiceUnavailable,
	[EnumMember(Value = "QuoteExpired")]
	QuoteExpired,
	[EnumMember(Value = "InsufficientFunds")]
	InsufficientFunds,
	[EnumMember(Value = "NotImplemented")]
	NotImplemented,
}

enum AnchorageSupportedFeatures
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "TRANSFERS")]
	Transfers,
	[EnumMember(Value = "HOLDS")]
	Holds,
}

enum AnchorageWebSocketMessageTypes
{
	[EnumMember(Value = "Unknown")]
	Unknown,
	[EnumMember(Value = "MarketDataSnapshotRequest")]
	MarketDataSnapshotRequest,
	[EnumMember(Value = "MarketDataSnapshot")]
	MarketDataSnapshot,
	[EnumMember(Value = "ExecutionReportResendRequest")]
	ExecutionReportResendRequest,
	[EnumMember(Value = "ExecutionReport")]
	ExecutionReport,
}

enum AnchorageSubscriptionActions
{
	[EnumMember(Value = "unknown")]
	Unknown,
	[EnumMember(Value = "subscribe")]
	Subscribe,
	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
}

sealed class AnchoragePage
{
	[JsonProperty("next")]
	public string Next { get; set; }
}

sealed class AnchorageAmount
{
	[JsonProperty("assetType")]
	public string AssetType { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("currentPrice")]
	public string CurrentPrice { get; set; }

	[JsonProperty("currentUSDValue")]
	public string CurrentUsdValue { get; set; }
}

sealed class AnchorageErrorDetails
{
	[JsonProperty("errorType")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageErrorTypes>))]
	public AnchorageErrorTypes Type { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class AnchorageResource
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("type")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageResourceTypes>))]
	public AnchorageResourceTypes Type { get; init; }
}

sealed class AnchorageEmptyResponse
{
}
