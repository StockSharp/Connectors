namespace StockSharp.Fireblocks.Native.Model;

sealed class FireblocksEnumConverter<TEnum> : JsonConverter<TEnum>
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
		var value = reader.TokenType == JsonToken.String
			? reader.Value as string
			: null;
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

enum FireblocksAssetClasses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "NATIVE")]
	Native,
	[EnumMember(Value = "FT")]
	FungibleToken,
	[EnumMember(Value = "FIAT")]
	Fiat,
	[EnumMember(Value = "NFT")]
	NonFungibleToken,
	[EnumMember(Value = "SFT")]
	SemiFungibleToken,
	[EnumMember(Value = "VIRTUAL")]
	Virtual,
}

enum FireblocksTransactionOperations
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "TRANSFER")]
	Transfer,
	[EnumMember(Value = "BURN")]
	Burn,
	[EnumMember(Value = "CONTRACT_CALL")]
	ContractCall,
	[EnumMember(Value = "PROGRAM_CALL")]
	ProgramCall,
	[EnumMember(Value = "MINT")]
	Mint,
	[EnumMember(Value = "RAW")]
	Raw,
	[EnumMember(Value = "TYPED_MESSAGE")]
	TypedMessage,
	[EnumMember(Value = "ENABLE_ASSET")]
	EnableAsset,
	[EnumMember(Value = "STAKE")]
	Stake,
	[EnumMember(Value = "REDEEM_FROM_COMPOUND")]
	RedeemFromCompound,
	[EnumMember(Value = "SUPPLY_TO_COMPOUND")]
	SupplyToCompound,
	[EnumMember(Value = "APPROVE")]
	Approve,
}

enum FireblocksTransactionStatuses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "SUBMITTED")]
	Submitted,
	[EnumMember(Value = "PENDING_AML_SCREENING")]
	PendingAmlScreening,
	[EnumMember(Value = "PENDING_ENRICHMENT")]
	PendingEnrichment,
	[EnumMember(Value = "PENDING_AUTHORIZATION")]
	PendingAuthorization,
	[EnumMember(Value = "QUEUED")]
	Queued,
	[EnumMember(Value = "PENDING_SIGNATURE")]
	PendingSignature,
	[EnumMember(Value = "PENDING_3RD_PARTY_MANUAL_APPROVAL")]
	PendingThirdPartyManualApproval,
	[EnumMember(Value = "PENDING_3RD_PARTY")]
	PendingThirdParty,
	[EnumMember(Value = "PENDING")]
	Pending,
	[EnumMember(Value = "BROADCASTING")]
	Broadcasting,
	[EnumMember(Value = "CONFIRMING")]
	Confirming,
	[EnumMember(Value = "CONFIRMED")]
	Confirmed,
	[EnumMember(Value = "COMPLETED")]
	Completed,
	[EnumMember(Value = "PARTIALLY_COMPLETED")]
	PartiallyCompleted,
	[EnumMember(Value = "CANCELLING")]
	Cancelling,
	[EnumMember(Value = "CANCELLED")]
	Cancelled,
	[EnumMember(Value = "BLOCKED")]
	Blocked,
	[EnumMember(Value = "REJECTED")]
	Rejected,
	[EnumMember(Value = "FAILED")]
	Failed,
	[EnumMember(Value = "TIMEOUT")]
	Timeout,
}

enum FireblocksSystemMessageTypes
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "WARN")]
	Warning,
	[EnumMember(Value = "BLOCK")]
	Blocking,
}

sealed class FireblocksJwtHeader
{
	[JsonProperty("alg")]
	public string Algorithm { get; init; } = "RS256";

	[JsonProperty("typ")]
	public string Type { get; init; } = "JWT";
}

sealed class FireblocksJwtPayload
{
	[JsonProperty("uri")]
	public string Uri { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }

	[JsonProperty("iat")]
	public long IssuedAt { get; init; }

	[JsonProperty("exp")]
	public long ExpiresAt { get; init; }

	[JsonProperty("sub")]
	public string Subject { get; init; }

	[JsonProperty("bodyHash")]
	public string BodyHash { get; init; }
}

sealed class FireblocksErrorResponse
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class FireblocksBooleanResponse
{
	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }
}
