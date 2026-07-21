namespace StockSharp.Copper.Native.Model;

sealed class CopperEnumConverter<TEnum> : JsonConverter<TEnum>
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

enum CopperPortfolioTypes
{
	[EnumMember(Value = "unknown")]
	Unknown,
	[EnumMember(Value = "custody")]
	Custody,
	[EnumMember(Value = "trading-vault")]
	TradingVault,
	[EnumMember(Value = "trading")]
	Trading,
	[EnumMember(Value = "external")]
	External,
	[EnumMember(Value = "clearloop")]
	ClearLoop,
}

enum CopperOrderTypes
{
	[EnumMember(Value = "unknown")]
	Unknown,
	[EnumMember(Value = "sell")]
	Sell,
	[EnumMember(Value = "buy")]
	Buy,
	[EnumMember(Value = "deposit")]
	Deposit,
	[EnumMember(Value = "withdraw")]
	Withdraw,
	[EnumMember(Value = "multi-withdraw")]
	MultiWithdraw,
	[EnumMember(Value = "wallet-message")]
	WalletMessage,
	[EnumMember(Value = "retrieved-deposit")]
	RetrievedDeposit,
	[EnumMember(Value = "earn-reward")]
	EarnReward,
	[EnumMember(Value = "earn-shared-reward")]
	EarnSharedReward,
	[EnumMember(Value = "claim-shared-reward")]
	ClaimSharedReward,
	[EnumMember(Value = "cross-chain-deposit")]
	CrossChainDeposit,
	[EnumMember(Value = "cross-chain-withdraw")]
	CrossChainWithdraw,
}

enum CopperOrderStatuses
{
	[EnumMember(Value = "unknown")]
	Unknown,
	[EnumMember(Value = "new")]
	New,
	[EnumMember(Value = "waiting-funds")]
	WaitingFunds,
	[EnumMember(Value = "reserving")]
	Reserving,
	[EnumMember(Value = "reserved")]
	Reserved,
	[EnumMember(Value = "queued")]
	Queued,
	[EnumMember(Value = "validating-funds")]
	ValidatingFunds,
	[EnumMember(Value = "working")]
	Working,
	[EnumMember(Value = "waiting-approve")]
	WaitingApprove,
	[EnumMember(Value = "co-sign-require")]
	CoSignRequired,
	[EnumMember(Value = "approved")]
	Approved,
	[EnumMember(Value = "processing")]
	Processing,
	[EnumMember(Value = "executed")]
	Executed,
	[EnumMember(Value = "canceled")]
	Canceled,
	[EnumMember(Value = "rejecting")]
	Rejecting,
	[EnumMember(Value = "rejected")]
	Rejected,
	[EnumMember(Value = "declining")]
	Declining,
	[EnumMember(Value = "declined")]
	Declined,
	[EnumMember(Value = "suspending")]
	Suspending,
	[EnumMember(Value = "suspended")]
	Suspended,
	[EnumMember(Value = "blocked")]
	Blocked,
	[EnumMember(Value = "action-required")]
	ActionRequired,
	[EnumMember(Value = "accepting")]
	Accepting,
	[EnumMember(Value = "accepted")]
	Accepted,
	[EnumMember(Value = "require-initializer-approve")]
	InitializerApprovalRequired,
	[EnumMember(Value = "waiting-counterparty-approve")]
	WaitingCounterpartyApproval,
	[EnumMember(Value = "require-counterparty-approve")]
	CounterpartyApprovalRequired,
	[EnumMember(Value = "ready-for-settlement")]
	ReadyForSettlement,
	[EnumMember(Value = "settled")]
	Settled,
	[EnumMember(Value = "liquidated")]
	Liquidated,
	[EnumMember(Value = "part-signed-tx-added")]
	PartSignedTransactionAdded,
	[EnumMember(Value = "full-signed-tx-added")]
	FullSignedTransactionAdded,
	[EnumMember(Value = "rejected-part-signed-tx-added")]
	RejectedPartSignedTransactionAdded,
	[EnumMember(Value = "rejected-full-signed-tx-added")]
	RejectedFullSignedTransactionAdded,
	[EnumMember(Value = "accepted-part-signed-tx-added")]
	AcceptedPartSignedTransactionAdded,
	[EnumMember(Value = "accepted-full-signed-tx-added")]
	AcceptedFullSignedTransactionAdded,
	[EnumMember(Value = "awaiting-settlement")]
	AwaitingSettlement,
	[EnumMember(Value = "master-password-required")]
	MasterPasswordRequired,
	[EnumMember(Value = "manual-resolving")]
	ManualResolving,
	[EnumMember(Value = "error")]
	Error,
	[EnumMember(Value = "pending-atomic-settlement-confirmation")]
	PendingAtomicSettlementConfirmation,
	[EnumMember(Value = "atomic-settlement-reservation-completed")]
	AtomicSettlementReservationCompleted,
	[EnumMember(Value = "require-finalize")]
	FinalizeRequired,
	[EnumMember(Value = "waiting-accept-deposit")]
	WaitingAcceptDeposit,
	[EnumMember(Value = "waiting-reject-deposit")]
	WaitingRejectDeposit,
}

sealed class CopperErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
