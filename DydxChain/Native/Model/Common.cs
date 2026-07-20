namespace StockSharp.DydxChain.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainMarketStatuses
{
	[EnumMember(Value = "ACTIVE")]
	Active,

	[EnumMember(Value = "PAUSED")]
	Paused,

	[EnumMember(Value = "CANCEL_ONLY")]
	CancelOnly,

	[EnumMember(Value = "POST_ONLY")]
	PostOnly,

	[EnumMember(Value = "INITIALIZING")]
	Initializing,

	[EnumMember(Value = "FINAL_SETTLEMENT")]
	FinalSettlement,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainMarketTypes
{
	[EnumMember(Value = "CROSS")]
	Cross,

	[EnumMember(Value = "ISOLATED")]
	Isolated,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainOrderSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainTradeTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "LIQUIDATED")]
	Liquidated,

	[EnumMember(Value = "DELEVERAGED")]
	Deleveraged,

	[EnumMember(Value = "TWAP_SUBORDER")]
	TwapSuborder,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainCandleResolutions
{
	[EnumMember(Value = "1MIN")]
	OneMinute,

	[EnumMember(Value = "5MINS")]
	FiveMinutes,

	[EnumMember(Value = "15MINS")]
	FifteenMinutes,

	[EnumMember(Value = "30MINS")]
	ThirtyMinutes,

	[EnumMember(Value = "1HOUR")]
	OneHour,

	[EnumMember(Value = "4HOURS")]
	FourHours,

	[EnumMember(Value = "1DAY")]
	OneDay,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainPositionSides
{
	[EnumMember(Value = "LONG")]
	Long,

	[EnumMember(Value = "SHORT")]
	Short,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainPositionStatuses
{
	[EnumMember(Value = "OPEN")]
	Open,

	[EnumMember(Value = "CLOSED")]
	Closed,

	[EnumMember(Value = "LIQUIDATED")]
	Liquidated,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,

	[EnumMember(Value = "STOP_MARKET")]
	StopMarket,

	[EnumMember(Value = "TRAILING_STOP")]
	TrailingStop,

	[EnumMember(Value = "TAKE_PROFIT")]
	TakeProfit,

	[EnumMember(Value = "TAKE_PROFIT_MARKET")]
	TakeProfitMarket,

	[EnumMember(Value = "TWAP")]
	Twap,

	[EnumMember(Value = "TWAP_SUBORDER")]
	TwapSuborder,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainTimeInForces
{
	[EnumMember(Value = "GTT")]
	GoodTillTime,

	[EnumMember(Value = "FOK")]
	FillOrKill,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainOrderStatuses
{
	[EnumMember(Value = "BEST_EFFORT_OPENED")]
	BestEffortOpened,

	[EnumMember(Value = "OPEN")]
	Open,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "BEST_EFFORT_CANCELED")]
	BestEffortCanceled,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "UNTRIGGERED")]
	Untriggered,

	[EnumMember(Value = "ERROR")]
	Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainLiquidities
{
	[EnumMember(Value = "TAKER")]
	Taker,

	[EnumMember(Value = "MAKER")]
	Maker,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainFillTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "LIQUIDATED")]
	Liquidated,

	[EnumMember(Value = "LIQUIDATION")]
	Liquidation,

	[EnumMember(Value = "DELEVERAGED")]
	Deleveraged,

	[EnumMember(Value = "OFFSETTING")]
	Offsetting,

	[EnumMember(Value = "TWAP_SUBORDER")]
	TwapSuborder,
}

enum DydxChainOrderFlags : uint
{
	ShortTerm = 0,
	Conditional = 32,
	LongTerm = 64,
	Twap = 128,
	TwapSuborder = 256,
}

enum DydxChainProtoSides
{
	Unspecified,
	Buy,
	Sell,
}

enum DydxChainProtoTimeInForces
{
	Unspecified,
	ImmediateOrCancel,
	PostOnly,
	FillOrKill,
}

enum DydxChainProtoConditionTypes
{
	Unspecified,
	StopLoss,
	TakeProfit,
}

sealed class DydxChainMarket
{
	[JsonProperty("clobPairId")]
	public string ClobPairId { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("status")]
	public DydxChainMarketStatuses Status { get; set; }

	[JsonProperty("oraclePrice")]
	public string OraclePrice { get; set; }

	[JsonProperty("priceChange24H")]
	public string PriceChange24Hours { get; set; }

	[JsonProperty("volume24H")]
	public string Volume24Hours { get; set; }

	[JsonProperty("trades24H")]
	public int Trades24Hours { get; set; }

	[JsonProperty("nextFundingRate")]
	public string NextFundingRate { get; set; }

	[JsonProperty("initialMarginFraction")]
	public string InitialMarginFraction { get; set; }

	[JsonProperty("maintenanceMarginFraction")]
	public string MaintenanceMarginFraction { get; set; }

	[JsonProperty("openInterest")]
	public string OpenInterest { get; set; }

	[JsonProperty("atomicResolution")]
	public int AtomicResolution { get; set; }

	[JsonProperty("quantumConversionExponent")]
	public int QuantumConversionExponent { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }

	[JsonProperty("stepBaseQuantums")]
	public int StepBaseQuantums { get; set; }

	[JsonProperty("subticksPerTick")]
	public int SubticksPerTick { get; set; }

	[JsonProperty("marketType")]
	public DydxChainMarketTypes MarketType { get; set; }

	[JsonProperty("openInterestLowerCap")]
	public string OpenInterestLowerCap { get; set; }

	[JsonProperty("openInterestUpperCap")]
	public string OpenInterestUpperCap { get; set; }

	[JsonProperty("baseOpenInterest")]
	public string BaseOpenInterest { get; set; }

	[JsonProperty("defaultFundingRate1H")]
	public string DefaultFundingRateOneHour { get; set; }
}

sealed class DydxChainTradingMarketUpdate
{
	public string Key { get; set; }

	[JsonProperty("clobPairId")]
	public string ClobPairId { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("status")]
	public DydxChainMarketStatuses? Status { get; set; }

	[JsonProperty("initialMarginFraction")]
	public string InitialMarginFraction { get; set; }

	[JsonProperty("maintenanceMarginFraction")]
	public string MaintenanceMarginFraction { get; set; }

	[JsonProperty("openInterest")]
	public string OpenInterest { get; set; }

	[JsonProperty("quantumConversionExponent")]
	public int? QuantumConversionExponent { get; set; }

	[JsonProperty("atomicResolution")]
	public int? AtomicResolution { get; set; }

	[JsonProperty("subticksPerTick")]
	public int? SubticksPerTick { get; set; }

	[JsonProperty("stepBaseQuantums")]
	public int? StepBaseQuantums { get; set; }

	[JsonProperty("marketType")]
	public DydxChainMarketTypes? MarketType { get; set; }

	[JsonProperty("openInterestLowerCap")]
	public string OpenInterestLowerCap { get; set; }

	[JsonProperty("openInterestUpperCap")]
	public string OpenInterestUpperCap { get; set; }

	[JsonProperty("baseOpenInterest")]
	public string BaseOpenInterest { get; set; }

	[JsonProperty("defaultFundingRate1H")]
	public string DefaultFundingRateOneHour { get; set; }

	[JsonProperty("priceChange24H")]
	public string PriceChange24Hours { get; set; }

	[JsonProperty("volume24H")]
	public string Volume24Hours { get; set; }

	[JsonProperty("trades24H")]
	public int? Trades24Hours { get; set; }

	[JsonProperty("nextFundingRate")]
	public string NextFundingRate { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }
}

sealed class DydxChainOraclePriceUpdate
{
	public string Ticker { get; set; }

	[JsonProperty("oraclePrice")]
	public string OraclePrice { get; set; }

	[JsonProperty("effectiveAt")]
	public string EffectiveAt { get; set; }

	[JsonProperty("effectiveAtHeight")]
	public string EffectiveAtHeight { get; set; }

	[JsonProperty("marketId")]
	public int MarketId { get; set; }
}

[JsonConverter(typeof(DydxChainPriceLevelConverter))]
sealed class DydxChainPriceLevel
{
	public string Price { get; set; }
	public string Size { get; set; }
}

sealed class DydxChainObjectPriceLevel
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }
}

sealed class DydxChainTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("side")]
	public DydxChainOrderSides Side { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("type")]
	public DydxChainTradeTypes Type { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("createdAtHeight")]
	public string CreatedAtHeight { get; set; }
}

sealed class DydxChainCandle
{
	[JsonProperty("startedAt")]
	public string StartedAt { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("resolution")]
	public DydxChainCandleResolutions Resolution { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }

	[JsonProperty("baseTokenVolume")]
	public string BaseTokenVolume { get; set; }

	[JsonProperty("usdVolume")]
	public string UsdVolume { get; set; }

	[JsonProperty("trades")]
	public int Trades { get; set; }

	[JsonProperty("startingOpenInterest")]
	public string StartingOpenInterest { get; set; }

	[JsonProperty("orderbookMidPriceOpen")]
	public string OrderbookMidPriceOpen { get; set; }

	[JsonProperty("orderbookMidPriceClose")]
	public string OrderbookMidPriceClose { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }
}

sealed class DydxChainPerpetualPosition
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("subaccountNumber")]
	public int SubaccountNumber { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("status")]
	public DydxChainPositionStatuses Status { get; set; }

	[JsonProperty("side")]
	public DydxChainPositionSides Side { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("maxSize")]
	public string MaximumSize { get; set; }

	[JsonProperty("entryPrice")]
	public string EntryPrice { get; set; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnl { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("createdAtHeight")]
	public string CreatedAtHeight { get; set; }

	[JsonProperty("sumOpen")]
	public string SumOpen { get; set; }

	[JsonProperty("sumClose")]
	public string SumClose { get; set; }

	[JsonProperty("netFunding")]
	public string NetFunding { get; set; }

	[JsonProperty("unrealizedPnl")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("closedAt")]
	public string ClosedAt { get; set; }

	[JsonProperty("exitPrice")]
	public string ExitPrice { get; set; }
}

sealed class DydxChainAssetPosition
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("subaccountNumber")]
	public int SubaccountNumber { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public DydxChainPositionSides Side { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("assetId")]
	public string AssetId { get; set; }
}

sealed class DydxChainOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("subaccountId")]
	public string SubaccountId { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("clobPairId")]
	public string ClobPairId { get; set; }

	[JsonProperty("side")]
	public DydxChainOrderSides? Side { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("totalFilled")]
	public string TotalFilled { get; set; }

	[JsonProperty("totalOptimisticFilled")]
	public string TotalOptimisticFilled { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("type")]
	public DydxChainOrderTypes? Type { get; set; }

	[JsonProperty("reduceOnly")]
	public bool? IsReduceOnly { get; set; }

	[JsonProperty("orderFlags")]
	public string OrderFlags { get; set; }

	[JsonProperty("goodTilBlock")]
	public string GoodTilBlock { get; set; }

	[JsonProperty("goodTilBlockTime")]
	public string GoodTilBlockTime { get; set; }

	[JsonProperty("createdAtHeight")]
	public string CreatedAtHeight { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("clientMetadata")]
	public string ClientMetadata { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("duration")]
	public string Duration { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("priceTolerance")]
	public string PriceTolerance { get; set; }

	[JsonProperty("timeInForce")]
	public DydxChainTimeInForces? TimeInForce { get; set; }

	[JsonProperty("status")]
	public DydxChainOrderStatuses Status { get; set; }

	[JsonProperty("postOnly")]
	public bool? IsPostOnly { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; set; }

	[JsonProperty("updatedAtHeight")]
	public string UpdatedAtHeight { get; set; }

	[JsonProperty("subaccountNumber")]
	public int SubaccountNumber { get; set; }

	[JsonProperty("removalReason")]
	public string RemovalReason { get; set; }
}

sealed class DydxChainFill
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("subaccountId")]
	public string SubaccountId { get; set; }

	[JsonProperty("side")]
	public DydxChainOrderSides Side { get; set; }

	[JsonProperty("liquidity")]
	public DydxChainLiquidities Liquidity { get; set; }

	[JsonProperty("type")]
	public DydxChainFillTypes Type { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("clobPairId")]
	public string ClobPairId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("quoteAmount")]
	public string QuoteAmount { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("createdAtHeight")]
	public string CreatedAtHeight { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientMetadata")]
	public string ClientMetadata { get; set; }

	[JsonProperty("subaccountNumber")]
	public int SubaccountNumber { get; set; }

	[JsonProperty("transactionHash")]
	public string TransactionHash { get; set; }
}

sealed class DydxChainTwapParameters
{
	public uint Duration { get; init; }
	public uint Interval { get; init; }
	public uint PriceTolerance { get; init; }
}

sealed class DydxChainPlaceOrder
{
	public string Address { get; init; }
	public uint SubaccountNumber { get; init; }
	public uint ClientId { get; init; }
	public uint ClobPairId { get; init; }
	public DydxChainOrderFlags OrderFlags { get; init; }
	public uint GoodTilBlock { get; init; }
	public uint GoodTilBlockTime { get; init; }
	public DydxChainProtoSides Side { get; init; }
	public ulong Quantums { get; init; }
	public ulong Subticks { get; init; }
	public DydxChainProtoTimeInForces TimeInForce { get; init; }
	public bool IsReduceOnly { get; init; }
	public uint ClientMetadata { get; init; }
	public DydxChainProtoConditionTypes ConditionType { get; init; }
	public ulong ConditionalTriggerSubticks { get; init; }
	public DydxChainTwapParameters TwapParameters { get; init; }
}

sealed class DydxChainCancelOrder
{
	public string Address { get; init; }
	public uint SubaccountNumber { get; init; }
	public uint ClientId { get; init; }
	public uint ClobPairId { get; init; }
	public DydxChainOrderFlags OrderFlags { get; init; }
	public uint GoodTilBlock { get; init; }
	public uint GoodTilBlockTime { get; init; }
}

sealed class DydxChainAccountInfo
{
	public ulong AccountNumber { get; init; }
	public ulong Sequence { get; init; }
}

sealed class DydxChainOrderIdentity
{
	public string OrderId { get; set; }
	public uint ClientId { get; init; }
	public uint ClobPairId { get; init; }
	public DydxChainOrderFlags OrderFlags { get; init; }
	public string Ticker { get; init; }
	public long TransactionId { get; init; }
}

sealed class DydxChainPriceLevelConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(DydxChainPriceLevel);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType == JsonToken.StartObject)
		{
			var value = serializer.Deserialize<DydxChainObjectPriceLevel>(reader);
			return value is null ? null : new DydxChainPriceLevel
			{
				Price = value.Price,
				Size = value.Size,
			};
		}
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				"dYdX price level must be an object or two-value array.");
		if (!reader.Read() || reader.TokenType is JsonToken.EndArray)
			throw new JsonSerializationException(
				"dYdX price level has no price.");
		var price = Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType is JsonToken.EndArray)
			throw new JsonSerializationException(
				"dYdX price level has no size.");
		var size = Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"dYdX price level contains extra values.");
		return new DydxChainPriceLevel { Price = price, Size = size };
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

abstract class DydxChainObjectCollectionConverter<T> : JsonConverter
	where T : class
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(T[]);

	protected abstract void SetKey(T item, string key);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return Array.Empty<T>();
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"dYdX keyed collection must be a JSON object.");

		var items = new List<T>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"dYdX keyed collection contains an invalid key.");
			var key = reader.Value?.ToString();
			if (!reader.Read())
				throw new JsonSerializationException(
					"dYdX keyed collection is truncated.");
			var item = serializer.Deserialize<T>(reader);
			if (item is null)
				continue;
			SetKey(item, key);
			items.Add(item);
		}
		return items.ToArray();
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class DydxChainMarketCollectionConverter :
	DydxChainObjectCollectionConverter<DydxChainMarket>
{
	protected override void SetKey(DydxChainMarket item, string key)
	{
		if (item.Ticker.IsEmpty())
			item.Ticker = key;
	}
}

sealed class DydxChainTradingMarketCollectionConverter :
	DydxChainObjectCollectionConverter<DydxChainTradingMarketUpdate>
{
	protected override void SetKey(DydxChainTradingMarketUpdate item, string key)
		=> item.Key = key;
}

sealed class DydxChainOraclePriceCollectionConverter :
	DydxChainObjectCollectionConverter<DydxChainOraclePriceUpdate>
{
	protected override void SetKey(DydxChainOraclePriceUpdate item, string key)
		=> item.Ticker = key;
}

sealed class DydxChainPerpetualPositionCollectionConverter :
	DydxChainObjectCollectionConverter<DydxChainPerpetualPosition>
{
	protected override void SetKey(DydxChainPerpetualPosition item, string key)
	{
		if (item.Market.IsEmpty())
			item.Market = key;
	}
}

sealed class DydxChainAssetPositionCollectionConverter :
	DydxChainObjectCollectionConverter<DydxChainAssetPosition>
{
	protected override void SetKey(DydxChainAssetPosition item, string key)
	{
		if (item.Symbol.IsEmpty())
			item.Symbol = key;
	}
}
