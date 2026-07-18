namespace StockSharp.BloFin.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BloFinInstrumentTypes
{
	[EnumMember(Value = "SWAP")]
	Swap,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BloFinInstrumentStates
{
	[EnumMember(Value = "live")]
	Live,

	[EnumMember(Value = "suspend")]
	Suspend,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BloFinCandleIntervals
{
	[EnumMember(Value = "1m")]
	Minute1,
	[EnumMember(Value = "3m")]
	Minute3,
	[EnumMember(Value = "5m")]
	Minute5,
	[EnumMember(Value = "15m")]
	Minute15,
	[EnumMember(Value = "30m")]
	Minute30,
	[EnumMember(Value = "1H")]
	Hour1,
	[EnumMember(Value = "2H")]
	Hour2,
	[EnumMember(Value = "4H")]
	Hour4,
	[EnumMember(Value = "6H")]
	Hour6,
	[EnumMember(Value = "8H")]
	Hour8,
	[EnumMember(Value = "12H")]
	Hour12,
	[EnumMember(Value = "1D")]
	Day1,
	[EnumMember(Value = "3D")]
	Day3,
	[EnumMember(Value = "1W")]
	Week1,
	[EnumMember(Value = "1M")]
	Month1,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BloFinSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BloFinApiOrderTypes
{
	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "post_only")]
	PostOnly,

	[EnumMember(Value = "fok")]
	FillOrKill,

	[EnumMember(Value = "ioc")]
	ImmediateOrCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BloFinApiOrderStates
{
	[EnumMember(Value = "live")]
	Live,

	[EnumMember(Value = "partially_filled")]
	PartiallyFilled,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "canceled")]
	Canceled,

	[EnumMember(Value = "partially_canceled")]
	PartiallyCanceled,

	[EnumMember(Value = "failed")]
	Failed,

	[EnumMember(Value = "order_failed")]
	OrderFailed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BloFinWsOperations
{
	[EnumMember(Value = "login")]
	Login,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BloFinWsEvents
{
	[EnumMember(Value = "login")]
	Login,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "error")]
	Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BloFinWsActions
{
	[EnumMember(Value = "snapshot")]
	Snapshot,

	[EnumMember(Value = "update")]
	Update,
}

interface IBloFinQuery
{
	string ToQueryString();
}

sealed class BloFinQueryBuilder
{
	private readonly StringBuilder _value = new();

	public BloFinQueryBuilder Add(string name, string value)
	{
		if (value.IsEmpty())
			return this;
		if (_value.Length > 0)
			_value.Append('&');
		_value.Append(Uri.EscapeDataString(name));
		_value.Append('=');
		_value.Append(Uri.EscapeDataString(value));
		return this;
	}

	public BloFinQueryBuilder Add(string name, long? value)
		=> value is null ? this : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

	public BloFinQueryBuilder Add(string name, int? value)
		=> value is null ? this : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

	public override string ToString() => _value.ToString();
}

sealed class BloFinEmptyQuery : IBloFinQuery
{
	public string ToQueryString() => string.Empty;
}

sealed class BloFinInstrumentQuery : IBloFinQuery
{
	public string InstrumentId { get; init; }

	public string ToQueryString() => new BloFinQueryBuilder()
		.Add("instId", InstrumentId)
		.ToString();
}

sealed class BloFinBookQuery : IBloFinQuery
{
	public string InstrumentId { get; init; }
	public int Size { get; init; }

	public string ToQueryString() => new BloFinQueryBuilder()
		.Add("instId", InstrumentId)
		.Add("size", Size)
		.ToString();
}

sealed class BloFinTradesQuery : IBloFinQuery
{
	public string InstrumentId { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new BloFinQueryBuilder()
		.Add("instId", InstrumentId)
		.Add("limit", Limit)
		.ToString();
}

sealed class BloFinCandlesQuery : IBloFinQuery
{
	public string InstrumentId { get; init; }
	public BloFinCandleIntervals Bar { get; init; }
	public long? Before { get; init; }
	public long? After { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new BloFinQueryBuilder()
		.Add("instId", InstrumentId)
		.Add("bar", Bar.ToBloFin())
		.Add("before", Before)
		.Add("after", After)
		.Add("limit", Limit)
		.ToString();
}

sealed class BloFinPositionsQuery : IBloFinQuery
{
	public string InstrumentId { get; init; }

	public string ToQueryString() => new BloFinQueryBuilder()
		.Add("instId", InstrumentId)
		.ToString();
}

sealed class BloFinOrdersQuery : IBloFinQuery
{
	public string InstrumentId { get; init; }
	public BloFinApiOrderTypes? OrderType { get; init; }
	public BloFinApiOrderStates? State { get; init; }
	public string Before { get; init; }
	public string After { get; init; }
	public long? Begin { get; init; }
	public long? End { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new BloFinQueryBuilder()
		.Add("instId", InstrumentId)
		.Add("orderType", OrderType?.ToBloFin())
		.Add("state", State?.ToBloFin())
		.Add("before", Before)
		.Add("after", After)
		.Add("begin", Begin)
		.Add("end", End)
		.Add("limit", Limit)
		.ToString();
}

sealed class BloFinFillsQuery : IBloFinQuery
{
	public string InstrumentId { get; init; }
	public string OrderId { get; init; }
	public string Before { get; init; }
	public string After { get; init; }
	public long? Begin { get; init; }
	public long? End { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new BloFinQueryBuilder()
		.Add("instId", InstrumentId)
		.Add("orderId", OrderId)
		.Add("before", Before)
		.Add("after", After)
		.Add("begin", Begin)
		.Add("end", End)
		.Add("limit", Limit)
		.ToString();
}

sealed class BloFinResponse<TData>
{
	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }

	[JsonProperty("data")]
	public TData Data { get; init; }
}

sealed class BloFinResponseHeader
{
	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }
}

sealed class BloFinServerTime
{
	[JsonProperty("ts")]
	public long Timestamp { get; init; }
}

sealed class BloFinInstrument
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; init; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; init; }

	[JsonProperty("settleCurrency")]
	public string SettleCurrency { get; init; }

	[JsonProperty("contractValue")]
	public string ContractValue { get; init; }

	[JsonProperty("listTime")]
	public long ListTime { get; init; }

	[JsonProperty("expireTime")]
	public long ExpireTime { get; init; }

	[JsonProperty("maxLeverage")]
	public string MaxLeverage { get; init; }

	[JsonProperty("minSize")]
	public string MinSize { get; init; }

	[JsonProperty("lotSize")]
	public string LotSize { get; init; }

	[JsonProperty("tickSize")]
	public string TickSize { get; init; }

	[JsonProperty("instType")]
	public BloFinInstrumentTypes InstrumentType { get; init; }

	[JsonProperty("maxLimitSize")]
	public string MaxLimitSize { get; init; }

	[JsonProperty("maxMarketSize")]
	public string MaxMarketSize { get; init; }

	[JsonProperty("state")]
	public BloFinInstrumentStates State { get; init; }
}

sealed class BloFinTicker
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("last")]
	public string LastPrice { get; init; }

	[JsonProperty("lastSize")]
	public string LastSize { get; init; }

	[JsonProperty("askPrice")]
	public string AskPrice { get; init; }

	[JsonProperty("askSize")]
	public string AskSize { get; init; }

	[JsonProperty("bidPrice")]
	public string BidPrice { get; init; }

	[JsonProperty("bidSize")]
	public string BidSize { get; init; }

	[JsonProperty("open24h")]
	public string OpenPrice { get; init; }

	[JsonProperty("high24h")]
	public string HighPrice { get; init; }

	[JsonProperty("low24h")]
	public string LowPrice { get; init; }

	[JsonProperty("volCurrency24h")]
	public string BaseVolume { get; init; }

	[JsonProperty("vol24h")]
	public string ContractVolume { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }
}

sealed class BloFinBook
{
	[JsonProperty("asks")]
	public BloFinBookLevel[] Asks { get; init; }

	[JsonProperty("bids")]
	public BloFinBookLevel[] Bids { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("prevSeqId")]
	public long PreviousSequenceId { get; init; }

	[JsonProperty("seqId")]
	public long SequenceId { get; init; }
}

[JsonConverter(typeof(BloFinBookLevelConverter))]
sealed class BloFinBookLevel
{
	public string Price { get; init; }
	public string Size { get; init; }
}

sealed class BloFinTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("side")]
	public BloFinSides Side { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }
}

sealed class BloFinFundingRate
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("fundingRate")]
	public string Rate { get; init; }

	[JsonProperty("fundingTime")]
	public long Time { get; init; }
}

[JsonConverter(typeof(BloFinCandleConverter))]
sealed class BloFinCandle
{
	public long Timestamp { get; init; }
	public string Open { get; init; }
	public string High { get; init; }
	public string Low { get; init; }
	public string Close { get; init; }
	public string ContractVolume { get; init; }
	public string BaseVolume { get; init; }
	public string QuoteVolume { get; init; }
	public bool IsFinished { get; init; }
}

sealed class BloFinAccount
{
	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("totalEquity")]
	public string TotalEquity { get; init; }

	[JsonProperty("isolatedEquity")]
	public string IsolatedEquity { get; init; }

	[JsonProperty("details")]
	public BloFinBalance[] Details { get; init; }
}

sealed class BloFinBalance
{
	[JsonProperty("currency")]
	public string Currency { get; init; }

	[JsonProperty("equity")]
	public string Equity { get; init; }

	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("isolatedEquity")]
	public string IsolatedEquity { get; init; }

	[JsonProperty("available")]
	public string Available { get; init; }

	[JsonProperty("availableEquity")]
	public string AvailableEquity { get; init; }

	[JsonProperty("frozen")]
	public string Frozen { get; init; }

	[JsonProperty("orderFrozen")]
	public string OrderFrozen { get; init; }

	[JsonProperty("equityUsd")]
	public string EquityUsd { get; init; }

	[JsonProperty("isolatedUnrealizedPnl")]
	public string IsolatedUnrealizedPnl { get; init; }

	[JsonProperty("unrealizedPnl")]
	public string UnrealizedPnl { get; init; }

	[JsonProperty("bonus")]
	public string Bonus { get; init; }
}

sealed class BloFinPosition
{
	[JsonProperty("positionId")]
	public string PositionId { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("marginMode")]
	public BloFinMarginModes MarginMode { get; init; }

	[JsonProperty("positionSide")]
	public BloFinPositionSides PositionSide { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }

	[JsonProperty("positions")]
	public string Positions { get; init; }

	[JsonProperty("availablePositions")]
	public string AvailablePositions { get; init; }

	[JsonProperty("averagePrice")]
	public string AveragePrice { get; init; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; init; }

	[JsonProperty("margin")]
	public string Margin { get; init; }

	[JsonProperty("marginRatio")]
	public string MarginRatio { get; init; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; init; }

	[JsonProperty("unrealizedPnl")]
	public string UnrealizedPnl { get; init; }

	[JsonProperty("unrealizedPnlRatio")]
	public string UnrealizedPnlRatio { get; init; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnl { get; init; }

	[JsonProperty("initialMargin")]
	public string InitialMargin { get; init; }

	[JsonProperty("maintenanceMargin")]
	public string MaintenanceMargin { get; init; }

	[JsonProperty("adl")]
	public string Adl { get; init; }

	[JsonProperty("createTime")]
	public long CreateTime { get; init; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; init; }
}

sealed class BloFinOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("marginMode")]
	public BloFinMarginModes MarginMode { get; init; }

	[JsonProperty("positionSide")]
	public BloFinPositionSides PositionSide { get; init; }

	[JsonProperty("side")]
	public BloFinSides Side { get; init; }

	[JsonProperty("orderType")]
	public BloFinApiOrderTypes OrderType { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("reduceOnly")]
	[JsonConverter(typeof(BloFinStringBooleanConverter))]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }

	[JsonProperty("state")]
	public BloFinApiOrderStates State { get; init; }

	[JsonProperty("filledSize")]
	public string FilledSize { get; init; }

	[JsonProperty("filledAmount")]
	public string FilledAmount { get; init; }

	[JsonProperty("averagePrice")]
	public string AveragePrice { get; init; }

	[JsonProperty("fee")]
	public string Fee { get; init; }

	[JsonProperty("pnl")]
	public string Pnl { get; init; }

	[JsonProperty("createTime")]
	public long CreateTime { get; init; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; init; }

	[JsonProperty("tpTriggerPrice")]
	public string TakeProfitTriggerPrice { get; init; }

	[JsonProperty("tpOrderPrice")]
	public string TakeProfitOrderPrice { get; init; }

	[JsonProperty("tpTriggerPriceType")]
	public BloFinTriggerPriceTypes? TakeProfitTriggerPriceType { get; init; }

	[JsonProperty("slTriggerPrice")]
	public string StopLossTriggerPrice { get; init; }

	[JsonProperty("slOrderPrice")]
	public string StopLossOrderPrice { get; init; }

	[JsonProperty("slTriggerPriceType")]
	public BloFinTriggerPriceTypes? StopLossTriggerPriceType { get; init; }

	[JsonProperty("brokerId")]
	public string BrokerId { get; init; }
}

sealed class BloFinFill
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("tradeId")]
	public string TradeId { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("fillPrice")]
	public string Price { get; init; }

	[JsonProperty("fillSize")]
	public string Size { get; init; }

	[JsonProperty("fillPnl")]
	public string Pnl { get; init; }

	[JsonProperty("side")]
	public BloFinSides Side { get; init; }

	[JsonProperty("fee")]
	public string Fee { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("brokerId")]
	public string BrokerId { get; init; }
}

sealed class BloFinOperationResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }
}

sealed class BloFinLeverageResult
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }

	[JsonProperty("marginMode")]
	public BloFinMarginModes MarginMode { get; init; }

	[JsonProperty("positionSide")]
	public BloFinPositionSides PositionSide { get; init; }
}

sealed class BloFinPlaceOrderRequest
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("marginMode")]
	public BloFinMarginModes MarginMode { get; init; }

	[JsonProperty("positionSide")]
	public BloFinPositionSides PositionSide { get; init; }

	[JsonProperty("side")]
	public BloFinSides Side { get; init; }

	[JsonProperty("orderType")]
	public BloFinApiOrderTypes OrderType { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("reduceOnly")]
	[JsonConverter(typeof(BloFinNullableStringBooleanConverter))]
	public bool? IsReduceOnly { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("tpTriggerPrice")]
	public string TakeProfitTriggerPrice { get; init; }

	[JsonProperty("tpOrderPrice")]
	public string TakeProfitOrderPrice { get; init; }

	[JsonProperty("tpTriggerPriceType")]
	public BloFinTriggerPriceTypes? TakeProfitTriggerPriceType { get; init; }

	[JsonProperty("slTriggerPrice")]
	public string StopLossTriggerPrice { get; init; }

	[JsonProperty("slOrderPrice")]
	public string StopLossOrderPrice { get; init; }

	[JsonProperty("slTriggerPriceType")]
	public BloFinTriggerPriceTypes? StopLossTriggerPriceType { get; init; }
}

sealed class BloFinCancelOrderRequest
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }
}

sealed class BloFinSetLeverageRequest
{
	[JsonProperty("instId")]
	public string InstrumentId { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }

	[JsonProperty("marginMode")]
	public BloFinMarginModes MarginMode { get; init; }

	[JsonProperty("positionSide")]
	public BloFinPositionSides PositionSide { get; init; }
}

sealed class BloFinWsHeader
{
	[JsonProperty("event")]
	public BloFinWsEvents? Event { get; init; }

	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }

	[JsonProperty("action")]
	public BloFinWsActions? Action { get; init; }

	[JsonProperty("arg")]
	public BloFinWsArgument Argument { get; init; }
}

sealed class BloFinWsArgument
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }
}

sealed class BloFinWsEnvelope<TData>
{
	[JsonProperty("action")]
	public BloFinWsActions? Action { get; init; }

	[JsonProperty("arg")]
	public BloFinWsArgument Argument { get; init; }

	[JsonProperty("data")]
	public TData[] Data { get; init; }
}

sealed class BloFinWsObjectEnvelope<TData>
{
	[JsonProperty("arg")]
	public BloFinWsArgument Argument { get; init; }

	[JsonProperty("data")]
	public TData Data { get; init; }
}

sealed class BloFinWsCommand<TArgument>
{
	[JsonProperty("op")]
	public BloFinWsOperations Operation { get; init; }

	[JsonProperty("args")]
	public TArgument[] Arguments { get; init; }
}

sealed class BloFinWsSubscription
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("instId")]
	public string InstrumentId { get; init; }
}

sealed class BloFinWsLogin
{
	[JsonProperty("apiKey")]
	public string ApiKey { get; init; }

	[JsonProperty("passphrase")]
	public string Passphrase { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("sign")]
	public string Signature { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }
}

sealed class BloFinStringBooleanConverter : JsonConverter<bool>
{
	public override bool ReadJson(JsonReader reader, Type objectType, bool existingValue,
		bool hasExistingValue, JsonSerializer serializer)
		=> reader.TokenType switch
		{
			JsonToken.Boolean => (bool)reader.Value,
			JsonToken.String when bool.TryParse((string)reader.Value, out var value) => value,
			JsonToken.Integer => Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture) != 0,
			_ => throw new JsonSerializationException("BloFin boolean value has an invalid type."),
		};

	public override void WriteJson(JsonWriter writer, bool value, JsonSerializer serializer)
		=> writer.WriteValue(value ? "true" : "false");
}

sealed class BloFinNullableStringBooleanConverter : JsonConverter<bool?>
{
	private static readonly BloFinStringBooleanConverter _inner = new();

	public override bool? ReadJson(JsonReader reader, Type objectType, bool? existingValue,
		bool hasExistingValue, JsonSerializer serializer)
		=> reader.TokenType == JsonToken.Null
			? null
			: _inner.ReadJson(reader, typeof(bool), existingValue ?? false,
				hasExistingValue, serializer);

	public override void WriteJson(JsonWriter writer, bool? value, JsonSerializer serializer)
	{
		if (value is null)
			writer.WriteNull();
		else
			_inner.WriteJson(writer, value.Value, serializer);
	}
}

sealed class BloFinBookLevelConverter : JsonConverter<BloFinBookLevel>
{
	public override BloFinBookLevel ReadJson(JsonReader reader, Type objectType,
		BloFinBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("BloFin order-book level must be an array.");

		var level = new BloFinBookLevel
		{
			Price = BloFinJson.ReadString(reader, "order-book price"),
			Size = BloFinJson.ReadString(reader, "order-book size"),
		};
		BloFinJson.SkipToEndArray(reader, "order-book level");
		return level;
	}

	public override void WriteJson(JsonWriter writer, BloFinBookLevel value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class BloFinCandleConverter : JsonConverter<BloFinCandle>
{
	public override BloFinCandle ReadJson(JsonReader reader, Type objectType,
		BloFinCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("BloFin candle must be an array.");

		var candle = new BloFinCandle
		{
			Timestamp = BloFinJson.ReadInt64(reader, "candle timestamp"),
			Open = BloFinJson.ReadString(reader, "candle open"),
			High = BloFinJson.ReadString(reader, "candle high"),
			Low = BloFinJson.ReadString(reader, "candle low"),
			Close = BloFinJson.ReadString(reader, "candle close"),
			ContractVolume = BloFinJson.ReadString(reader, "candle contract volume"),
			BaseVolume = BloFinJson.ReadString(reader, "candle base volume"),
			QuoteVolume = BloFinJson.ReadString(reader, "candle quote volume"),
			IsFinished = BloFinJson.ReadString(reader, "candle confirmation") == "1",
		};
		BloFinJson.SkipToEndArray(reader, "candle");
		return candle;
	}

	public override void WriteJson(JsonWriter writer, BloFinCandle value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

static class BloFinJson
{
	public static string ReadString(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException($"BloFin {field} is missing.");
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or JsonToken.Float or JsonToken.Boolean))
			throw new JsonSerializationException($"BloFin {field} has an invalid type.");
		return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
	}

	public static long ReadInt64(JsonReader reader, string field)
	{
		var value = ReadString(reader, field);
		if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			throw new JsonSerializationException($"BloFin {field} is not an integer.");
		return result;
	}

	public static void SkipToEndArray(JsonReader reader, string valueName)
	{
		while (reader.Read())
		{
			if (reader.TokenType == JsonToken.EndArray)
				return;
			if (reader.TokenType is JsonToken.StartArray or JsonToken.StartObject)
				reader.Skip();
		}
		throw new JsonSerializationException($"BloFin {valueName} is not terminated.");
	}
}
