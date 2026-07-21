namespace StockSharp.Paxos.Native.Model;

[JsonConverter(typeof(PaxosEnumConverter<PaxosOAuthGrantTypes>))]
enum PaxosOAuthGrantTypes
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "client_credentials")]
	ClientCredentials,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosProfileTypes>))]
enum PaxosProfileTypes
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "NORMAL")]
	Normal,
	[EnumMember(Value = "DEFAULT")]
	Default,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosMarketTradingStatuses>))]
enum PaxosMarketTradingStatuses
{
	[EnumMember(Value = "MARKET_TRADING_STATUS_UNSPECIFIED")]
	Unknown,
	[EnumMember(Value = "AVAILABLE")]
	Available,
	[EnumMember(Value = "UNAVAILABLE")]
	Unavailable,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosSides>))]
enum PaxosSides
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "BUY")]
	Buy,
	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosOrderTypes>))]
enum PaxosOrderTypes
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "LIMIT")]
	Limit,
	[EnumMember(Value = "MARKET")]
	Market,
	[EnumMember(Value = "POST_ONLY_LIMIT")]
	PostOnlyLimit,
	[EnumMember(Value = "STOP_MARKET")]
	StopMarket,
	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosOrderStatuses>))]
enum PaxosOrderStatuses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "PENDING_SUBMISSION")]
	PendingSubmission,
	[EnumMember(Value = "SUBMITTED")]
	Submitted,
	[EnumMember(Value = "OPEN")]
	Open,
	[EnumMember(Value = "FILLED")]
	Filled,
	[EnumMember(Value = "CANCELLED")]
	Cancelled,
	[EnumMember(Value = "REJECTED")]
	Rejected,
	[EnumMember(Value = "EXPIRED")]
	Expired,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosTimeInForces>))]
enum PaxosTimeInForces
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "GTC")]
	GoodTillCancel,
	[EnumMember(Value = "FOK")]
	FillOrKill,
	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,
	[EnumMember(Value = "GTT")]
	GoodTillTime,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosTransferStatuses>))]
enum PaxosTransferStatuses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "PENDING")]
	Pending,
	[EnumMember(Value = "COMPLETED")]
	Completed,
	[EnumMember(Value = "FAILED")]
	Failed,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosTransferDirections>))]
enum PaxosTransferDirections
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "CREDIT")]
	Credit,
	[EnumMember(Value = "DEBIT")]
	Debit,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosTransferTypes>))]
enum PaxosTransferTypes
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "INTERNAL_TRANSFER_DEBIT")]
	InternalTransferDebit,
	[EnumMember(Value = "INTERNAL_TRANSFER_CREDIT")]
	InternalTransferCredit,
	[EnumMember(Value = "CRYPTO_DEPOSIT")]
	CryptoDeposit,
	[EnumMember(Value = "CRYPTO_WITHDRAWAL")]
	CryptoWithdrawal,
	[EnumMember(Value = "WIRE_DEPOSIT")]
	WireDeposit,
	[EnumMember(Value = "WIRE_WITHDRAWAL")]
	WireWithdrawal,
	[EnumMember(Value = "SEN_DEPOSIT")]
	SenDeposit,
	[EnumMember(Value = "SEN_WITHDRAWAL")]
	SenWithdrawal,
	[EnumMember(Value = "BANK_DEPOSIT")]
	BankDeposit,
	[EnumMember(Value = "BANK_WITHDRAWAL")]
	BankWithdrawal,
	[EnumMember(Value = "PAXOS_TRANSFER_DEBIT")]
	PaxosTransferDebit,
	[EnumMember(Value = "PAXOS_TRANSFER_CREDIT")]
	PaxosTransferCredit,
	[EnumMember(Value = "SIGNET_DEPOSIT")]
	SignetDeposit,
	[EnumMember(Value = "SIGNET_WITHDRAWAL")]
	SignetWithdrawal,
	[EnumMember(Value = "CBIT_WITHDRAWAL")]
	CbitWithdrawal,
	[EnumMember(Value = "CBIT_DEPOSIT")]
	CbitDeposit,
	[EnumMember(Value = "CUBIX_DEPOSIT")]
	CubixDeposit,
	[EnumMember(Value = "CUBIX_WITHDRAWAL")]
	CubixWithdrawal,
	[EnumMember(Value = "RTP_DEPOSIT")]
	RtpDeposit,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosConversionStatuses>))]
enum PaxosConversionStatuses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "CREATED")]
	Created,
	[EnumMember(Value = "SETTLED")]
	Settled,
	[EnumMember(Value = "CANCELLED")]
	Cancelled,
	[EnumMember(Value = "FAILED")]
	Failed,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosCandleIncrements>))]
enum PaxosCandleIncrements
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "ONE_MINUTE")]
	OneMinute,
	[EnumMember(Value = "FIVE_MINUTES")]
	FiveMinutes,
	[EnumMember(Value = "FIFTEEN_MINUTES")]
	FifteenMinutes,
	[EnumMember(Value = "THIRTY_MINUTES")]
	ThirtyMinutes,
	[EnumMember(Value = "ONE_HOUR")]
	OneHour,
	[EnumMember(Value = "TWO_HOURS")]
	TwoHours,
	[EnumMember(Value = "TWELVE_HOURS")]
	TwelveHours,
	[EnumMember(Value = "ONE_DAY")]
	OneDay,
	[EnumMember(Value = "ONE_WEEK")]
	OneWeek,
	[EnumMember(Value = "TWO_WEEKS")]
	TwoWeeks,
	[EnumMember(Value = "FOUR_WEEKS")]
	FourWeeks,
}

[JsonConverter(typeof(PaxosEnumConverter<PaxosSocketMessageTypes>))]
enum PaxosSocketMessageTypes
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "SNAPSHOT")]
	Snapshot,
	[EnumMember(Value = "UPDATE")]
	Update,
}

sealed class PaxosItemsResponse<TItem>
{
	[JsonProperty("items")]
	public TItem[] Items { get; set; }

	[JsonProperty("next_page_cursor")]
	public string NextPageCursor { get; set; }
}

sealed class PaxosTokenRequest
{
	public PaxosOAuthGrantTypes GrantType { get; init; }
	public string ClientId { get; init; }
	public string ClientSecret { get; init; }
	public string Scope { get; init; }
}

sealed class PaxosTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("scope")]
	public string Scope { get; set; }
}

sealed class PaxosErrorDetails
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }
}

sealed class PaxosEmptyResponse
{
}
