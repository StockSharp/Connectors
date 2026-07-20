namespace StockSharp.ApexOmni.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniAccount
{
	[JsonProperty("id", Required = Required.Always)]
	public string Id { get; set; }

	[JsonProperty("ethereumAddress")]
	public string EthereumAddress { get; set; }

	[JsonProperty("l2Key")]
	public string L2Key { get; set; }

	[JsonProperty("contractAccount", Required = Required.Always)]
	public ApexOmniContractAccount ContractAccount { get; set; }

	[JsonProperty("contractWallets")]
	public ApexOmniWallet[] ContractWallets { get; set; }

	[JsonProperty("positions")]
	public ApexOmniPosition[] Positions { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniContractAccount
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("makerFeeRate", Required = Required.Always)]
	public string MakerFeeRate { get; set; }

	[JsonProperty("takerFeeRate", Required = Required.Always)]
	public string TakerFeeRate { get; set; }

	[JsonProperty("createdAt")]
	public long CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; set; }

}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniWallet
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("balance", Required = Required.Always)]
	public string Balance { get; set; }

	[JsonProperty("pendingDepositAmount")]
	public string PendingDepositAmount { get; set; }

	[JsonProperty("pendingWithdrawAmount")]
	public string PendingWithdrawAmount { get; set; }

	[JsonProperty("pendingTransferInAmount")]
	public string PendingTransferInAmount { get; set; }

	[JsonProperty("pendingTransferOutAmount")]
	public string PendingTransferOutAmount { get; set; }

	public string Currency => Asset.IsEmpty() ? Token : Asset;
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniPosition
{
	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public ApexOmniPositionSides Side { get; set; }

	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }

	[JsonProperty("entryPrice")]
	public string EntryPrice { get; set; }

	[JsonProperty("exitPrice")]
	public string ExitPrice { get; set; }

	[JsonProperty("fundingFee")]
	public string FundingFee { get; set; }

	[JsonProperty("netFunding")]
	private string NetFunding { set => FundingFee = value; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnl { get; set; }

	[JsonProperty("customImr")]
	public string CustomInitialMarginRate { get; set; }

	[JsonProperty("customInitialMarginRate")]
	private string LongCustomInitialMarginRate
	{
		set => CustomInitialMarginRate = value;
	}

	[JsonProperty("createdAt")]
	public long CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; set; }

	[JsonProperty("updatedTime")]
	private long UpdatedTime { set => UpdatedAt = value; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniAccountBalance
{
	[JsonProperty("totalEquityValue")]
	public string TotalEquityValue { get; set; }

	[JsonProperty("availableBalance")]
	public string AvailableBalance { get; set; }

	[JsonProperty("initialMargin")]
	public string InitialMargin { get; set; }

	[JsonProperty("maintenanceMargin")]
	public string MaintenanceMargin { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public ApexOmniNativeSides Side { get; set; }

	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("limitFee")]
	public string LimitFee { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("triggerPriceType")]
	public ApexOmniTriggerPriceTypes TriggerPriceType { get; set; }

	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }

	[JsonProperty("remainingSize")]
	public string RemainingSize { get; set; }

	[JsonProperty("type", Required = Required.Always)]
	public ApexOmniNativeOrderTypes Type { get; set; }

	[JsonProperty("createdAt")]
	public long CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; set; }

	[JsonProperty("updatedTime")]
	private long UpdatedTime { set => UpdatedAt = value; }

	[JsonProperty("expiresAt")]
	public long ExpiresAt { get; set; }

	[JsonProperty("status", Required = Required.Always)]
	public ApexOmniOrderStatuses Status { get; set; }

	[JsonProperty("timeInForce")]
	public ApexOmniTimeInForces TimeInForce { get; set; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("isPositionTpsl")]
	public bool IsPositionTpsl { get; set; }

	[JsonProperty("cumSuccessFillSize")]
	public string FilledSize { get; set; }

	[JsonProperty("cumSuccessFillValue")]
	public string FilledValue { get; set; }

	[JsonProperty("cumSuccessFillFee")]
	public string FilledFee { get; set; }

	[JsonProperty("latestMatchFillPrice")]
	public string LatestFillPrice { get; set; }

	public string EffectiveId => Id.IsEmpty() ? OrderId : Id;

	public string EffectiveClientId => ClientId.IsEmpty()
		? ClientOrderId
		: ClientId;
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniFill
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("matchFillId")]
	public string MatchFillId { get; set; }

	[JsonProperty("orderId", Required = Required.Always)]
	public string OrderId { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public ApexOmniNativeSides Side { get; set; }

	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("createdAt")]
	public long CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; set; }

	public string EffectiveId => Id.IsEmpty() ? MatchFillId : Id;

	public string EffectiveClientId => ClientId.IsEmpty()
		? ClientOrderId
		: ClientId;
}

[JsonConverter(typeof(ApexOmniOrdersPayloadConverter))]
sealed class ApexOmniOrdersPayload
{
	public ApexOmniOrder[] Orders { get; init; }
	public long TotalSize { get; init; }
}

sealed class ApexOmniOrdersPayloadConverter : JsonConverter<ApexOmniOrdersPayload>
{
	public override ApexOmniOrdersPayload ReadJson(JsonReader reader,
		Type objectType, ApexOmniOrdersPayload existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType == JsonToken.StartArray)
			return new()
			{
				Orders = serializer.Deserialize<ApexOmniOrder[]>(reader) ?? [],
			};
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"ApeX Omni order payload must be an object or array.");
		ApexOmniOrder[] orders = [];
		long totalSize = 0;
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"ApeX Omni order payload is malformed.");
			var name = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException(
					"ApeX Omni order payload ended unexpectedly.");
			switch (name)
			{
				case "orders":
					orders = serializer.Deserialize<ApexOmniOrder[]>(reader) ?? [];
					break;
				case "totalSize":
					totalSize = Convert.ToInt64(reader.Value,
						CultureInfo.InvariantCulture);
					break;
				default:
					reader.Skip();
					break;
			}
		}
		return new() { Orders = orders, TotalSize = totalSize };
	}

	public override void WriteJson(JsonWriter writer, ApexOmniOrdersPayload value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

[JsonConverter(typeof(ApexOmniFillsPayloadConverter))]
sealed class ApexOmniFillsPayload
{
	public ApexOmniFill[] Fills { get; init; }
	public long TotalSize { get; init; }
}

sealed class ApexOmniFillsPayloadConverter : JsonConverter<ApexOmniFillsPayload>
{
	public override ApexOmniFillsPayload ReadJson(JsonReader reader,
		Type objectType, ApexOmniFillsPayload existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType == JsonToken.StartArray)
			return new()
			{
				Fills = serializer.Deserialize<ApexOmniFill[]>(reader) ?? [],
			};
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"ApeX Omni fill payload must be an object or array.");
		ApexOmniFill[] fills = [];
		long totalSize = 0;
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"ApeX Omni fill payload is malformed.");
			var name = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException(
					"ApeX Omni fill payload ended unexpectedly.");
			switch (name)
			{
				case "fills":
				case "orders":
					fills = serializer.Deserialize<ApexOmniFill[]>(reader) ?? [];
					break;
				case "totalSize":
					totalSize = Convert.ToInt64(reader.Value,
						CultureInfo.InvariantCulture);
					break;
				default:
					reader.Skip();
					break;
			}
		}
		return new() { Fills = fills, TotalSize = totalSize };
	}

	public override void WriteJson(JsonWriter writer, ApexOmniFillsPayload value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniOrderHistoryRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }

	[JsonProperty("page")]
	public int Page { get; set; }

	[JsonProperty("beginTimeInclusive")]
	public long? BeginTime { get; set; }

	[JsonProperty("endTimeExclusive")]
	public long? EndTime { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniWorstPriceRequest
{
	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public ApexOmniNativeSides Side { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniWorstPrice
{
	[JsonProperty("worstPrice", Required = Required.Always)]
	public string WorstPrice { get; set; }

	[JsonProperty("bidOnePrice")]
	public string BestBidPrice { get; set; }

	[JsonProperty("askOnePrice")]
	public string BestAskPrice { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniCreateOrderRequest
{
	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public ApexOmniNativeSides Side { get; set; }

	[JsonProperty("type", Required = Required.Always)]
	public ApexOmniNativeOrderTypes Type { get; set; }

	[JsonProperty("timeInForce", Required = Required.Always)]
	public ApexOmniTimeInForces TimeInForce { get; set; }

	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }

	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("limitFee", Required = Required.Always)]
	public string LimitFee { get; set; }

	[JsonProperty("expiration", Required = Required.Always)]
	public long Expiration { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("triggerPriceType")]
	public ApexOmniTriggerPriceTypes? TriggerPriceType { get; set; }

	[JsonProperty("clientId", Required = Required.Always)]
	public string ClientId { get; set; }

	[JsonProperty("signature", Required = Required.Always)]
	public string Signature { get; set; }

	[JsonProperty("reduceOnly", Required = Required.Always)]
	public bool IsReduceOnly { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniCancelOrderRequest
{
	[JsonProperty("id", Required = Required.Always)]
	public string Id { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniCancelAllOrdersRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}
