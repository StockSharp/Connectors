namespace StockSharp.GMTrade.Native;

[JsonConverter(typeof(StringEnumConverter))]
enum GMTradeGraphQlSocketMessageTypes
{
	[EnumMember(Value = "connection_init")]
	ConnectionInit,

	[EnumMember(Value = "connection_ack")]
	ConnectionAck,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "next")]
	Next,

	[EnumMember(Value = "error")]
	Error,

	[EnumMember(Value = "complete")]
	Complete,

	[EnumMember(Value = "ping")]
	Ping,

	[EnumMember(Value = "pong")]
	Pong,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GMTradeOrderKinds
{
	[EnumMember(Value = "liquidation")]
	Liquidation,

	[EnumMember(Value = "auto_deleveraging")]
	AutoDeleveraging,

	[EnumMember(Value = "market_swap")]
	MarketSwap,

	[EnumMember(Value = "market_increase")]
	MarketIncrease,

	[EnumMember(Value = "market_decrease")]
	MarketDecrease,

	[EnumMember(Value = "limit_swap")]
	LimitSwap,

	[EnumMember(Value = "limit_increase")]
	LimitIncrease,

	[EnumMember(Value = "limit_decrease")]
	LimitDecrease,

	[EnumMember(Value = "stop_loss_decrease")]
	StopLossDecrease,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GMTradePositionSides
{
	[EnumMember(Value = "long")]
	Long,

	[EnumMember(Value = "short")]
	Short,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GMTradeActionStates
{
	[EnumMember(Value = "pending")]
	Pending,

	[EnumMember(Value = "completed")]
	Completed,

	[EnumMember(Value = "cancelled")]
	Cancelled,
}

sealed class GMTradeGraphQlRequest<TVariables>
{
	[JsonProperty("query")]
	public string Query { get; init; }

	[JsonProperty("variables")]
	public TVariables Variables { get; init; }
}

sealed class GMTradeGraphQlResponse<TData>
{
	[JsonProperty("data")]
	public TData Data { get; init; }

	[JsonProperty("errors")]
	public GMTradeGraphQlError[] Errors { get; init; }
}

sealed class GMTradeGraphQlError
{
	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class GMTradeNoVariables
{
}

sealed class GMTradeMarketsData
{
	[JsonProperty("markets")]
	public GMTradeMarket[] Markets { get; init; }
}

sealed class GMTradeMarketUpdateData
{
	[JsonProperty("markets")]
	public GMTradeMarket Market { get; init; }
}

sealed class GMTradeMarket
{
	[JsonProperty("marketToken")]
	public string MarketToken { get; init; }

	[JsonProperty("pubkey")]
	public string PublicKey { get; init; }

	[JsonProperty("slot")]
	public long? Slot { get; init; }

	[JsonProperty("meta")]
	public GMTradeMarketMeta Meta { get; init; }

	[JsonProperty("isSnapshot")]
	public bool IsSnapshot { get; init; }

	[JsonProperty("isLastSnapshot")]
	public bool IsLastSnapshot { get; init; }
}

sealed class GMTradeMarketMeta
{
	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("store")]
	public string Store { get; init; }

	[JsonProperty("indexToken")]
	public GMTradeToken IndexToken { get; init; }

	[JsonProperty("longToken")]
	public GMTradeToken LongToken { get; init; }

	[JsonProperty("shortToken")]
	public GMTradeToken ShortToken { get; init; }

	[JsonProperty("isPure")]
	public bool IsPure { get; init; }

	[JsonProperty("isEnabled")]
	public bool IsEnabled { get; init; }
}

sealed class GMTradeToken
{
	[JsonProperty("pubkey")]
	public string PublicKey { get; init; }

	[JsonProperty("price")]
	public GMTradePrice Price { get; init; }

	[JsonProperty("meta")]
	public GMTradeTokenMeta Meta { get; init; }
}

sealed class GMTradeTokenMeta
{
	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("uiSymbol")]
	public string DisplaySymbol { get; init; }

	[JsonProperty("uiName")]
	public string DisplayName { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }

	[JsonProperty("precision")]
	public int Precision { get; init; }

	[JsonProperty("category")]
	public string Category { get; init; }

	[JsonProperty("isEnabled")]
	public bool IsEnabled { get; init; }

	[JsonProperty("isSynthetic")]
	public bool IsSynthetic { get; init; }
}

sealed class GMTradePrice
{
	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("min")]
	public string Minimum { get; init; }

	[JsonProperty("max")]
	public string Maximum { get; init; }

	[JsonProperty("isOpen")]
	public bool IsOpen { get; init; }
}

sealed class GMTradeCandlesVariables
{
	[JsonProperty("indexToken")]
	public string IndexToken { get; init; }

	[JsonProperty("resolution")]
	public int Resolution { get; init; }

	[JsonProperty("from")]
	public long From { get; init; }

	[JsonProperty("to")]
	public long To { get; init; }
}

sealed class GMTradeCandleSubscriptionVariables
{
	[JsonProperty("indexToken")]
	public string IndexToken { get; init; }

	[JsonProperty("resolution")]
	public int Resolution { get; init; }
}

sealed class GMTradeCandlesData
{
	[JsonProperty("candles")]
	public GMTradeCandle[] Candles { get; init; }
}

sealed class GMTradeCandleUpdateData
{
	[JsonProperty("candleUpdate")]
	public GMTradeCandle Candle { get; init; }
}

sealed class GMTradeCandle
{
	[JsonProperty("indexToken")]
	public string IndexToken { get; init; }

	[JsonProperty("resolution")]
	public int Resolution { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("open")]
	public string Open { get; init; }

	[JsonProperty("high")]
	public string High { get; init; }

	[JsonProperty("low")]
	public string Low { get; init; }

	[JsonProperty("close")]
	public string Close { get; init; }
}

sealed class GMTradeTradesVariables
{
	[JsonProperty("where")]
	public GMTradeTradeFilter Filter { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }
}

sealed class GMTradeTradeFilter
{
	[JsonProperty("marketToken_eq")]
	public string MarketToken { get; init; }

	[JsonProperty("marketToken_in")]
	public string[] MarketTokens { get; init; }

	[JsonProperty("user_eq")]
	public string User { get; init; }

	[JsonProperty("timestamp_gte")]
	public DateTime? From { get; init; }

	[JsonProperty("timestamp_lte")]
	public DateTime? To { get; init; }
}

sealed class GMTradeTradesData
{
	[JsonProperty("tradeEvents")]
	public GMTradeTrade[] Trades { get; init; }
}

sealed class GMTradeTrade
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; init; }

	[JsonProperty("flags")]
	public string Flags { get; init; }

	[JsonProperty("tradeId")]
	public string TradeId { get; init; }

	[JsonProperty("user")]
	public string User { get; init; }

	[JsonProperty("marketToken")]
	public string MarketToken { get; init; }

	[JsonProperty("position")]
	public string Position { get; init; }

	[JsonProperty("order")]
	public string Order { get; init; }

	[JsonProperty("executionPrice")]
	public string ExecutionPrice { get; init; }

	[JsonProperty("beforeSizeInTokens")]
	public string BeforeSizeInTokens { get; init; }

	[JsonProperty("afterSizeInTokens")]
	public string AfterSizeInTokens { get; init; }

	[JsonProperty("beforeSizeInUsd")]
	public string BeforeSizeInUsd { get; init; }

	[JsonProperty("afterSizeInUsd")]
	public string AfterSizeInUsd { get; init; }

	[JsonProperty("beforeCollateralAmount")]
	public string BeforeCollateralAmount { get; init; }

	[JsonProperty("afterCollateralAmount")]
	public string AfterCollateralAmount { get; init; }

	[JsonProperty("pnlPnl")]
	public string ProfitLoss { get; init; }
}

sealed class GMTradeUserVariables
{
	[JsonProperty("owner")]
	public string Owner { get; init; }
}

sealed class GMTradeUserData
{
	[JsonProperty("user")]
	public GMTradeUser User { get; init; }
}

sealed class GMTradeUser
{
	[JsonProperty("pubkey")]
	public string PublicKey { get; init; }

	[JsonProperty("positions")]
	public GMTradePosition[] Positions { get; init; }

	[JsonProperty("orders")]
	public GMTradeOrder[] Orders { get; init; }
}

sealed class GMTradePositionUpdateData
{
	[JsonProperty("positions")]
	public GMTradePosition Position { get; init; }
}

sealed class GMTradePosition
{
	[JsonProperty("pubkey")]
	public string PublicKey { get; init; }

	[JsonProperty("isInsert")]
	public bool IsInsert { get; init; }

	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("store")]
	public string Store { get; init; }

	[JsonProperty("kind")]
	public GMTradePositionSides Kind { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("marketToken")]
	public string MarketToken { get; init; }

	[JsonProperty("collateralToken")]
	public string CollateralToken { get; init; }

	[JsonProperty("tradeId")]
	public long TradeId { get; init; }

	[JsonProperty("increasedAt")]
	public long IncreasedAt { get; init; }

	[JsonProperty("updatedAtSlot")]
	public long UpdatedAtSlot { get; init; }

	[JsonProperty("decreasedAt")]
	public long DecreasedAt { get; init; }

	[JsonProperty("sizeInTokens")]
	public string SizeInTokens { get; init; }

	[JsonProperty("collateralAmount")]
	public string CollateralAmount { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }
}

sealed class GMTradeOrderUpdateData
{
	[JsonProperty("orders")]
	public GMTradeOrder Order { get; init; }
}

sealed class GMTradeOrder
{
	[JsonProperty("pubkey")]
	public string PublicKey { get; init; }

	[JsonProperty("isInsert")]
	public bool IsInsert { get; init; }

	[JsonProperty("slot")]
	public long Slot { get; init; }

	[JsonProperty("marketToken")]
	public string MarketToken { get; init; }

	[JsonProperty("initialCollateralToken")]
	public string InitialCollateralToken { get; init; }

	[JsonProperty("finalOutputToken")]
	public string FinalOutputToken { get; init; }

	[JsonProperty("header")]
	public GMTradeActionHeader Header { get; init; }

	[JsonProperty("params")]
	public GMTradeOrderParameters Parameters { get; init; }
}

sealed class GMTradeActionHeader
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("store")]
	public string Store { get; init; }

	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("status")]
	public GMTradeActionStates Status { get; init; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; init; }

	[JsonProperty("updatedAtSlot")]
	public long UpdatedAtSlot { get; init; }
}

sealed class GMTradeOrderParameters
{
	[JsonProperty("kind")]
	public GMTradeOrderKinds Kind { get; init; }

	[JsonProperty("side")]
	public GMTradePositionSides Side { get; init; }

	[JsonProperty("amount")]
	public long Amount { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("acceptablePrice")]
	public string AcceptablePrice { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }

	[JsonProperty("minOutput")]
	public string MinimumOutput { get; init; }

	[JsonProperty("validFromTs")]
	public long ValidFromTimestamp { get; init; }
}

sealed class GMTradeGraphQlSocketHeader
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("type")]
	public GMTradeGraphQlSocketMessageTypes Type { get; init; }
}

sealed class GMTradeGraphQlSocketControlMessage
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("type")]
	public GMTradeGraphQlSocketMessageTypes Type { get; init; }
}

sealed class GMTradeGraphQlSocketRequest<TVariables>
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("type")]
	public GMTradeGraphQlSocketMessageTypes Type { get; init; }

	[JsonProperty("payload")]
	public GMTradeGraphQlRequest<TVariables> Payload { get; init; }
}

sealed class GMTradeGraphQlSocketResponse<TData>
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("type")]
	public GMTradeGraphQlSocketMessageTypes Type { get; init; }

	[JsonProperty("payload")]
	public GMTradeGraphQlResponse<TData> Payload { get; init; }
}

sealed class GMTradeGraphQlSocketErrorResponse
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("type")]
	public GMTradeGraphQlSocketMessageTypes Type { get; init; }

	[JsonProperty("payload")]
	public GMTradeGraphQlError[] Errors { get; init; }
}

sealed class GMTradeRpcRequest<TParameters>
	where TParameters : GMTradeRpcParameters
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public TParameters Parameters { get; init; }
}

sealed class GMTradeRpcResponse<TResult>
{
	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public GMTradeRpcError Error { get; init; }
}

sealed class GMTradeRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(GMTradeRpcParametersConverter))]
abstract class GMTradeRpcParameters
{
}

sealed class GMTradeRpcBalanceParameters : GMTradeRpcParameters
{
	public string Owner { get; init; }
	public GMTradeRpcCommitmentConfig Config { get; init; }
}

sealed class GMTradeRpcTokenAccountsParameters : GMTradeRpcParameters
{
	public string Owner { get; init; }
	public GMTradeRpcProgramFilter Filter { get; init; }
	public GMTradeRpcTokenAccountConfig Config { get; init; }
}

sealed class GMTradeRpcCommitmentConfig
{
	[JsonProperty("commitment")]
	public string Commitment { get; init; } = "confirmed";
}

sealed class GMTradeRpcProgramFilter
{
	[JsonProperty("programId")]
	public string ProgramId { get; init; }
}

sealed class GMTradeRpcTokenAccountConfig
{
	[JsonProperty("encoding")]
	public string Encoding { get; init; } = "jsonParsed";

	[JsonProperty("commitment")]
	public string Commitment { get; init; } = "confirmed";
}

sealed class GMTradeRpcContextValue<TResult>
{
	[JsonProperty("value")]
	public TResult Value { get; init; }
}

sealed class GMTradeRpcTokenAccount
{
	[JsonProperty("pubkey")]
	public string PublicKey { get; init; }

	[JsonProperty("account")]
	public GMTradeRpcAccount Account { get; init; }
}

sealed class GMTradeRpcAccount
{
	[JsonProperty("data")]
	public GMTradeRpcParsedData Data { get; init; }
}

sealed class GMTradeRpcParsedData
{
	[JsonProperty("parsed")]
	public GMTradeRpcParsedToken Parsed { get; init; }
}

sealed class GMTradeRpcParsedToken
{
	[JsonProperty("info")]
	public GMTradeRpcTokenInfo Information { get; init; }
}

sealed class GMTradeRpcTokenInfo
{
	[JsonProperty("mint")]
	public string Mint { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("tokenAmount")]
	public GMTradeRpcTokenAmount TokenAmount { get; init; }
}

sealed class GMTradeRpcTokenAmount
{
	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }
}

sealed class GMTradeWalletBalance
{
	public string Mint { get; init; }
	public string Amount { get; init; }
	public int Decimals { get; init; }
}

sealed class GMTradeRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(GMTradeRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case GMTradeRpcBalanceParameters balance:
				writer.WriteValue(balance.Owner);
				serializer.Serialize(writer, balance.Config);
				break;
			case GMTradeRpcTokenAccountsParameters tokens:
				writer.WriteValue(tokens.Owner);
				serializer.Serialize(writer, tokens.Filter);
				serializer.Serialize(writer, tokens.Config);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported GMTrade RPC parameter DTO '{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
