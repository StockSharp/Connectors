namespace StockSharp.Phemex.Native.Model;

sealed class PhemexApiResponse<TData>
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class PhemexMarketResponse<TData>
{
	[JsonProperty("error")]
	public PhemexWsError Error { get; set; }

	[JsonProperty("result")]
	public TData Result { get; set; }
}

sealed class PhemexProductsData
{
	[JsonProperty("currencies")]
	public PhemexWireCurrency[] Currencies { get; set; }

	[JsonProperty("products")]
	public PhemexWireProduct[] Products { get; set; }

	[JsonProperty("perpProductsV2")]
	public PhemexWirePerpetualProduct[] PerpetualProducts { get; set; }
}

sealed class PhemexWireCurrency
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("valueScale")]
	public int ValueScale { get; set; }
}

sealed class PhemexWireProduct
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("displaySymbol")]
	public string DisplaySymbol { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("settleCurrency")]
	public string SettleCurrency { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("priceScale")]
	public int PriceScale { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("baseQtyPrecision")]
	public int BaseQuantityPrecision { get; set; }

	[JsonProperty("baseTickSizeEv")]
	public string BaseTickSizeEv { get; set; }

	[JsonProperty("minOrderValueEv")]
	public string MinOrderValueEv { get; set; }
}

sealed class PhemexWirePerpetualProduct
{
	[JsonProperty("perpProductSubType")]
	public string SubType { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("displaySymbol")]
	public string DisplaySymbol { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("settleCurrency")]
	public string SettleCurrency { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("qtyPrecision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("qtyStepSize")]
	public string QuantityStepSize { get; set; }

	[JsonProperty("minOrderValueRv")]
	public string MinimumOrderValue { get; set; }
}

sealed class PhemexSymbol
{
	public string Symbol { get; set; }
	public string Name { get; set; }
	public string BaseCurrency { get; set; }
	public string QuoteCurrency { get; set; }
	public int BasePrecision { get; set; }
	public int QuotePrecision { get; set; }
	public string MinTradeSize { get; set; }
	public bool IsEnabled { get; set; }
}

sealed class PhemexFuturesSymbol
{
	public string Symbol { get; set; }
	public string Name { get; set; }
	public string BaseCurrency { get; set; }
	public string QuoteCurrency { get; set; }
	public string SettleCurrency { get; set; }
	public int BasePrecision { get; set; }
	public int QuotePrecision { get; set; }
	public string BaseStep { get; set; }
	public string QuoteStep { get; set; }
	public string MinSizeLimit { get; set; }
	public string Status { get; set; }
}

sealed class PhemexTicker
{
	public string Symbol { get; set; }
	public long Time { get; set; }
	public string Open { get; set; }
	public string Close { get; set; }
	public string High { get; set; }
	public string Low { get; set; }
	public string Volume { get; set; }
	public string Amount { get; set; }
	public string IndexPrice { get; set; }
	public string MarkPrice { get; set; }
	public string FundingRate { get; set; }
	public string BidPrice { get; set; }
	public string AskPrice { get; set; }
}

sealed class PhemexBookTicker
{
	public string Symbol { get; set; }
	public string BidPrice { get; set; }
	public string BidSize { get; set; }
	public string AskPrice { get; set; }
	public string AskSize { get; set; }
	public long Timestamp { get; set; }
}

[JsonConverter(typeof(PhemexBookLevelConverter))]
sealed class PhemexBookLevel
{
	public string Price { get; set; }
	public string Size { get; set; }
}

sealed class PhemexBookLevelConverter : JsonConverter<PhemexBookLevel>
{
	public override PhemexBookLevel ReadJson(JsonReader reader, Type objectType,
		PhemexBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Phemex order-book level must be an array.");

		var level = new PhemexBookLevel
		{
			Price = PhemexJson.ReadString(reader, "order-book price"),
			Size = PhemexJson.ReadString(reader, "order-book size"),
		};
		PhemexJson.ReadEndArray(reader, "order-book level");
		return level;
	}

	public override void WriteJson(JsonWriter writer, PhemexBookLevel value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class PhemexDepthData
{
	public PhemexBookLevel[] Bids { get; set; }
	public PhemexBookLevel[] Asks { get; set; }
	public long UpdateTime { get; set; }
}

[JsonConverter(typeof(PhemexWireTradeConverter))]
sealed class PhemexWireTrade
{
	public long Timestamp { get; set; }
	public string Side { get; set; }
	public string Price { get; set; }
	public string Size { get; set; }
}

sealed class PhemexWireTradeConverter : JsonConverter<PhemexWireTrade>
{
	public override PhemexWireTrade ReadJson(JsonReader reader, Type objectType,
		PhemexWireTrade existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Phemex trade must be an array.");

		var trade = new PhemexWireTrade
		{
			Timestamp = PhemexJson.ReadInt64(reader, "trade timestamp"),
			Side = PhemexJson.ReadString(reader, "trade side"),
			Price = PhemexJson.ReadString(reader, "trade price"),
			Size = PhemexJson.ReadString(reader, "trade size"),
		};
		PhemexJson.ReadEndArray(reader, "trade");
		return trade;
	}

	public override void WriteJson(JsonWriter writer, PhemexWireTrade value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class PhemexWireTradeResult
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("trades")]
	public PhemexWireTrade[] SpotTrades { get; set; }

	[JsonProperty("trades_p")]
	public PhemexWireTrade[] FuturesTrades { get; set; }
}

sealed class PhemexMarketTrade
{
	public string Symbol { get; set; }
	public string TradeId { get; set; }
	public string Price { get; set; }
	public string Size { get; set; }
	public string Side { get; set; }
	public long Timestamp { get; set; }
}

sealed class PhemexWireBook
{
	[JsonProperty("bids")]
	public PhemexBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public PhemexBookLevel[] Asks { get; set; }
}

sealed class PhemexWireDepthResult
{
	[JsonProperty("book")]
	public PhemexWireBook SpotBook { get; set; }

	[JsonProperty("orderbook_p")]
	public PhemexWireBook FuturesBook { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("dts")]
	public long DispatchTimestamp { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

sealed class PhemexWireSpotTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("openEp")]
	public string Open { get; set; }

	[JsonProperty("lastEp")]
	public string Close { get; set; }

	[JsonProperty("highEp")]
	public string High { get; set; }

	[JsonProperty("lowEp")]
	public string Low { get; set; }

	[JsonProperty("volumeEv")]
	public string Volume { get; set; }

	[JsonProperty("turnoverEv")]
	public string Turnover { get; set; }

	[JsonProperty("indexEp")]
	public string IndexPrice { get; set; }

	[JsonProperty("bidEp")]
	public string BidPrice { get; set; }

	[JsonProperty("askEp")]
	public string AskPrice { get; set; }
}

sealed class PhemexWireFuturesTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("openRp")]
	public string Open { get; set; }

	[JsonProperty("lastRp")]
	public string Close { get; set; }

	[JsonProperty("closeRp")]
	private string LegacyClose { set => Close = value; }

	[JsonProperty("highRp")]
	public string High { get; set; }

	[JsonProperty("lowRp")]
	public string Low { get; set; }

	[JsonProperty("volumeRq")]
	public string Volume { get; set; }

	[JsonProperty("turnoverRv")]
	public string Turnover { get; set; }

	[JsonProperty("indexRp")]
	public string IndexPrice { get; set; }

	[JsonProperty("indexPriceRp")]
	private string LegacyIndexPrice { set => IndexPrice = value; }

	[JsonProperty("markRp")]
	public string MarkPrice { get; set; }

	[JsonProperty("markPriceRp")]
	private string LegacyMarkPrice { set => MarkPrice = value; }

	[JsonProperty("fundingRateRr")]
	public string FundingRate { get; set; }

	[JsonProperty("bidRp")]
	public string BidPrice { get; set; }

	[JsonProperty("askRp")]
	public string AskPrice { get; set; }
}

[JsonConverter(typeof(PhemexWireKlineConverter))]
sealed class PhemexWireKline
{
	public long Time { get; set; }
	public int Resolution { get; set; }
	public string LastClose { get; set; }
	public string Open { get; set; }
	public string High { get; set; }
	public string Low { get; set; }
	public string Close { get; set; }
	public string Volume { get; set; }
	public string Turnover { get; set; }
	public string Symbol { get; set; }
}

sealed class PhemexWireKlineConverter : JsonConverter<PhemexWireKline>
{
	public override PhemexWireKline ReadJson(JsonReader reader, Type objectType,
		PhemexWireKline existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Phemex kline must be an array.");

		var candle = new PhemexWireKline
		{
			Time = PhemexJson.ReadInt64(reader, "kline timestamp"),
			Resolution = PhemexJson.ReadInt32(reader, "kline resolution"),
			LastClose = PhemexJson.ReadString(reader, "kline previous close"),
			Open = PhemexJson.ReadString(reader, "kline open"),
			High = PhemexJson.ReadString(reader, "kline high"),
			Low = PhemexJson.ReadString(reader, "kline low"),
			Close = PhemexJson.ReadString(reader, "kline close"),
			Volume = PhemexJson.ReadString(reader, "kline volume"),
			Turnover = PhemexJson.ReadString(reader, "kline turnover"),
		};
		if (!reader.Read())
			throw new JsonSerializationException("Phemex kline is incomplete.");
		if (reader.TokenType != JsonToken.EndArray)
		{
			candle.Symbol = PhemexJson.CurrentString(reader, "kline symbol");
			PhemexJson.ReadEndArray(reader, "kline");
		}
		return candle;
	}

	public override void WriteJson(JsonWriter writer, PhemexWireKline value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class PhemexRowsData<TRow>
{
	[JsonProperty("rows")]
	public TRow[] Rows { get; set; }
}

sealed class PhemexKline
{
	public long Time { get; set; }
	public string Open { get; set; }
	public string Close { get; set; }
	public string High { get; set; }
	public string Low { get; set; }
	public string Volume { get; set; }
}

sealed class PhemexWireSpotWallet
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balanceEv")]
	public string Balance { get; set; }

	[JsonProperty("lockedTradingBalanceEv")]
	public string LockedTradingBalance { get; set; }

	[JsonProperty("lockedWithdrawEv")]
	public string LockedWithdrawBalance { get; set; }

	[JsonProperty("lastUpdateTimeNs")]
	public long UpdateTime { get; set; }
}

sealed class PhemexWireFuturesAccount
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("accountBalanceRv")]
	public string Balance { get; set; }

	[JsonProperty("totalUsedBalanceRv")]
	public string UsedBalance { get; set; }
}

sealed class PhemexWireFuturesAccountResult
{
	[JsonProperty("account")]
	public PhemexWireFuturesAccount Account { get; set; }

	[JsonProperty("positions")]
	public PhemexWirePosition[] Positions { get; set; }
}

sealed class PhemexWirePosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("posSide")]
	public string PositionSide { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("sizeRq")]
	public string SizeRq { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("avgEntryPriceRp")]
	public string AveragePrice { get; set; }

	[JsonProperty("markPriceRp")]
	public string MarkPrice { get; set; }

	[JsonProperty("unRealisedPnlRv")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("unrealisedPnlRv")]
	private string WsUnrealizedPnl { set => UnrealizedPnl = value; }

	[JsonProperty("liquidationPriceRp")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("leverageRr")]
	public string Leverage { get; set; }

	[JsonProperty("transactTimeNs")]
	public long UpdateTime { get; set; }

	[JsonProperty("posMode")]
	public string PositionMode { get; set; }
}

sealed class PhemexBalance
{
	public string Coin { get; set; }
	public string Free { get; set; }
	public string Frozen { get; set; }
	public string Debts { get; set; }
}

class PhemexBalancesData
{
	public PhemexBalance[] Balances { get; set; }
}

sealed class PhemexFuturesBalancesData : PhemexBalancesData
{
	public PhemexIsolatedBalance[] Isolates { get; set; }
}

sealed class PhemexIsolatedBalance
{
	public string Symbol { get; set; }
	public string IsolatedMode { get; set; }
	public PhemexBalance[] Balances { get; set; }
}

sealed class PhemexPosition
{
	public string PositionId { get; set; }
	public string Symbol { get; set; }
	public string IsolatedMode { get; set; }
	public string PositionSide { get; set; }
	public string NetSize { get; set; }
	public string AveragePrice { get; set; }
	public string UnrealizedPnl { get; set; }
	public string LongSize { get; set; }
	public string ShortSize { get; set; }
	public string MarkPrice { get; set; }
	public string LiquidationPrice { get; set; }
	public string Leverage { get; set; }
	public long CreateTime { get; set; }
	public long UpdateTime { get; set; }
}

sealed class PhemexWireOrder
{
	[JsonProperty("orderID")]
	public string OrderId { get; set; }

	[JsonProperty("orderId")]
	private string AlternateOrderId { set => OrderId = value; }

	[JsonProperty("clOrdID")]
	public string ClientOrderId { get; set; }

	[JsonProperty("clOrdId")]
	private string AlternateClientOrderId { set => ClientOrderId = value; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("posSide")]
	public string PositionSide { get; set; }

	[JsonProperty("ordType")]
	public string OrderType { get; set; }

	[JsonProperty("orderType")]
	private string AlternateOrderType { set => OrderType = value; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("ordStatus")]
	public string Status { get; set; }

	[JsonProperty("priceEp")]
	public string SpotPrice { get; set; }

	[JsonProperty("priceRp")]
	public string FuturesPrice { get; set; }

	[JsonProperty("execPriceRp")]
	public string ExecutionPrice { get; set; }

	[JsonProperty("execID")]
	public string ExecutionId { get; set; }

	[JsonProperty("execQty")]
	public string ExecutionQuantity { get; set; }

	[JsonProperty("baseQtyEv")]
	public string SpotQuantity { get; set; }

	[JsonProperty("quoteQtyEv")]
	public string SpotQuoteQuantity { get; set; }

	[JsonProperty("cumBaseQtyEv")]
	public string SpotFilledQuantity { get; set; }

	[JsonProperty("cumBaseValueEv")]
	private string AlternateSpotFilledQuantity { set => SpotFilledQuantity = value; }

	[JsonProperty("cumQuoteQtyEv")]
	public string SpotFilledAmount { get; set; }

	[JsonProperty("cumQuoteValueEv")]
	private string AlternateSpotFilledAmount { set => SpotFilledAmount = value; }

	[JsonProperty("orderQtyRq")]
	public string FuturesQuantity { get; set; }

	[JsonProperty("orderQty")]
	private string AlternateFuturesQuantity { set => FuturesQuantity = value; }

	[JsonProperty("cumQtyRq")]
	public string FuturesFilledQuantity { get; set; }

	[JsonProperty("cumQty")]
	private string AlternateFuturesFilledQuantity { set => FuturesFilledQuantity = value; }

	[JsonProperty("execQtyRq")]
	private string HistoricalFuturesFilledQuantity { set => FuturesFilledQuantity = value; }

	[JsonProperty("cumValueRv")]
	public string FuturesFilledAmount { get; set; }

	[JsonProperty("orderValueRv")]
	private string HistoricalFuturesAmount { set => FuturesFilledAmount = value; }

	[JsonProperty("cumFeeEv")]
	public string SpotFee { get; set; }

	[JsonProperty("execFeeRv")]
	public string FuturesFee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("qtyType")]
	public string QuantityType { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("execInst")]
	public string ExecutionInstruction { get; set; }

	[JsonProperty("createTimeNs")]
	public long CreateTime { get; set; }

	[JsonProperty("actionTimeNs")]
	public long ActionTime { get; set; }

	[JsonProperty("transactTimeNs")]
	public long UpdateTime { get; set; }

	[JsonProperty("createdAt")]
	public long CreatedAtMilliseconds { get; set; }
}

sealed class PhemexWireFill
{
	[JsonProperty("execID")]
	public string ExecutionId { get; set; }

	[JsonProperty("execId")]
	private string AlternateExecutionId { set => ExecutionId = value; }

	[JsonProperty("orderID")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("execPriceEp")]
	public string SpotPrice { get; set; }

	[JsonProperty("execPriceRp")]
	public string FuturesPrice { get; set; }

	[JsonProperty("execBaseQtyEv")]
	public string SpotQuantity { get; set; }

	[JsonProperty("execQtyRq")]
	public string FuturesQuantity { get; set; }

	[JsonProperty("execQuoteQtyEv")]
	public string SpotAmount { get; set; }

	[JsonProperty("execValueRv")]
	public string FuturesAmount { get; set; }

	[JsonProperty("execFeeEv")]
	public string SpotFee { get; set; }

	[JsonProperty("execFeeRv")]
	public string FuturesFee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("transactTimeNs")]
	public long Timestamp { get; set; }

	[JsonProperty("createdAt")]
	public long CreatedAtMilliseconds { get; set; }
}

[JsonConverter(typeof(PhemexOrderCollectionConverter))]
sealed class PhemexOrderCollection : PhemexCollection<PhemexWireOrder>
{
}

[JsonConverter(typeof(PhemexFillCollectionConverter))]
sealed class PhemexFillCollection : PhemexCollection<PhemexWireFill>
{
}

class PhemexCollection<TItem>
{
	public TItem[] Items { get; set; }
}

sealed class PhemexOrderCollectionConverter : JsonConverter<PhemexOrderCollection>
{
	public override PhemexOrderCollection ReadJson(JsonReader reader, Type objectType,
		PhemexOrderCollection existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		return new() { Items = PhemexCollectionReader.Read<PhemexWireOrder>(reader, serializer) };
	}

	public override void WriteJson(JsonWriter writer, PhemexOrderCollection value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class PhemexFillCollectionConverter : JsonConverter<PhemexFillCollection>
{
	public override PhemexFillCollection ReadJson(JsonReader reader, Type objectType,
		PhemexFillCollection existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		return new() { Items = PhemexCollectionReader.Read<PhemexWireFill>(reader, serializer) };
	}

	public override void WriteJson(JsonWriter writer, PhemexFillCollection value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

static class PhemexCollectionReader
{
	public static TItem[] Read<TItem>(JsonReader reader, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return [];
		if (reader.TokenType == JsonToken.StartArray)
			return serializer.Deserialize<TItem[]>(reader) ?? [];
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("Phemex collection must be an array or a container.");

		TItem[] items = [];
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("Phemex collection contains an invalid property.");
			var name = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException("Phemex collection is incomplete.");
			if (name.EqualsIgnoreCase("rows") || name.EqualsIgnoreCase("orders") ||
				name.EqualsIgnoreCase("fills"))
				items = serializer.Deserialize<TItem[]>(reader) ?? [];
			else
				serializer.Deserialize<PhemexIgnoredValue>(reader);
		}
		return items;
	}
}

[JsonConverter(typeof(PhemexIgnoredValueConverter))]
sealed class PhemexIgnoredValue
{
}

sealed class PhemexIgnoredValueConverter : JsonConverter<PhemexIgnoredValue>
{
	public override PhemexIgnoredValue ReadJson(JsonReader reader, Type objectType,
		PhemexIgnoredValue existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		reader.Skip();
		return new();
	}

	public override void WriteJson(JsonWriter writer, PhemexIgnoredValue value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class PhemexOrderResult
{
	public string OrderId { get; set; }
	public string ClientOrderId { get; set; }
}

sealed class PhemexOrder
{
	public string OrderId { get; set; }
	public string Symbol { get; set; }
	public string Type { get; set; }
	public string PositionMode { get; set; }
	public string IsolatedMode { get; set; }
	public string Side { get; set; }
	public string PositionSide { get; set; }
	public string Price { get; set; }
	public string OriginalSize { get; set; }
	public string Size { get; set; }
	public string Amount { get; set; }
	public string FilledSize { get; set; }
	public string FilledAmount { get; set; }
	public string Fee { get; set; }
	public string FeeCoin { get; set; }
	public string Status { get; set; }
	public string TimeInForce { get; set; }
	public bool IsImmediateOrCancel { get; set; }
	public bool IsReduceOnly { get; set; }
	public string ClientOrderId { get; set; }
	public long CreateTime { get; set; }
	public long UpdateTime { get; set; }
}

sealed class PhemexFill
{
	public string Id { get; set; }
	public string OrderId { get; set; }
	public string Symbol { get; set; }
	public string Side { get; set; }
	public string Price { get; set; }
	public string Size { get; set; }
	public string Fee { get; set; }
	public string FeeCoin { get; set; }
	public long Timestamp { get; set; }
}

sealed class PhemexSpotOrderRequest
{
	public string Symbol { get; init; }
	public string Side { get; init; }
	public string Type { get; init; }
	public string ClientOrderId { get; init; }
	public string Size { get; init; }
	public string Price { get; init; }
	public string Amount { get; init; }
	public PhemexOrderPolicies Policy { get; init; }
}

sealed class PhemexFuturesOrderRequest
{
	public string ClientOrderId { get; init; }
	public string Symbol { get; init; }
	public string PositionSide { get; init; }
	public string Side { get; init; }
	public string Type { get; init; }
	public string Size { get; init; }
	public string Price { get; init; }
	public PhemexOrderPolicies Policy { get; init; }
	public bool IsReduceOnly { get; init; }
}

sealed class PhemexCancelOrderRequest
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string PositionSide { get; init; }
}

sealed class PhemexAmendOrderRequest
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string PositionSide { get; init; }
	public string Price { get; init; }
	public string Size { get; init; }
	public string QuoteAmount { get; init; }
}

sealed class PhemexCancelAllOrdersRequest
{
	public string Symbol { get; init; }
}

sealed class PhemexWireSpotOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("clOrdID")]
	public string ClientOrderId { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("qtyType")]
	public string QuantityType { get; init; }

	[JsonProperty("quoteQtyEv")]
	public long? QuoteQuantity { get; init; }

	[JsonProperty("baseQtyEv")]
	public long? BaseQuantity { get; init; }

	[JsonProperty("priceEp")]
	public long? Price { get; init; }

	[JsonProperty("ordType")]
	public string OrderType { get; init; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; init; }
}

sealed class PhemexWireFuturesOrderRequest
{
	[JsonProperty("clOrdID")]
	public string ClientOrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("orderQtyRq")]
	public string Quantity { get; init; }

	[JsonProperty("ordType")]
	public string OrderType { get; init; }

	[JsonProperty("priceRp")]
	public string Price { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("posSide")]
	public string PositionSide { get; init; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; init; }
}

sealed class PhemexWsRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public string[] Parameters { get; init; }
}

[JsonConverter(typeof(PhemexWsAuthParametersConverter))]
sealed class PhemexWsAuthParameters
{
	public string ApiKey { get; init; }
	public string Signature { get; init; }
	public long Expiry { get; init; }
}

sealed class PhemexWsAuthParametersConverter : JsonConverter<PhemexWsAuthParameters>
{
	public override PhemexWsAuthParameters ReadJson(JsonReader reader, Type objectType,
		PhemexWsAuthParameters existingValue, bool hasExistingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, PhemexWsAuthParameters value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		writer.WriteValue("API");
		writer.WriteValue(value.ApiKey);
		writer.WriteValue(value.Signature);
		writer.WriteValue(value.Expiry);
		writer.WriteEndArray();
	}
}

sealed class PhemexWsAuthRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; } = "user.auth";

	[JsonProperty("params")]
	public PhemexWsAuthParameters Parameters { get; init; }
}

sealed class PhemexWsError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class PhemexWsEnvelope
{
	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("error")]
	public PhemexWsError Error { get; set; }

	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("book")]
	public PhemexWireBook SpotBook { get; set; }

	[JsonProperty("orderbook_p")]
	public PhemexWireBook FuturesBook { get; set; }

	[JsonProperty("trades")]
	public PhemexWireTrade[] SpotTrades { get; set; }

	[JsonProperty("trades_p")]
	public PhemexWireTrade[] FuturesTrades { get; set; }

	[JsonProperty("spot_market24h")]
	public PhemexWireSpotTicker SpotTicker { get; set; }

	[JsonProperty("data")]
	public PhemexWsTickerPack[] FuturesTickers { get; set; }

	[JsonProperty("fields")]
	public string[] Fields { get; set; }

	[JsonProperty("wallets")]
	public PhemexWireSpotWallet[] SpotWallets { get; set; }

	[JsonProperty("orders")]
	public PhemexWsSpotOrders SpotOrders { get; set; }

	[JsonProperty("accounts_p")]
	public PhemexWireFuturesAccount[] FuturesAccounts { get; set; }

	[JsonProperty("orders_p")]
	public PhemexWireOrder[] FuturesOrders { get; set; }

	[JsonProperty("positions_p")]
	public PhemexWirePosition[] FuturesPositions { get; set; }
}

sealed class PhemexWsSpotOrders
{
	[JsonProperty("open")]
	public PhemexWireOrder[] Open { get; set; }

	[JsonProperty("closed")]
	public PhemexWireOrder[] Closed { get; set; }

	[JsonProperty("fills")]
	public PhemexWireFill[] Fills { get; set; }
}

[JsonConverter(typeof(PhemexWsTickerPackConverter))]
sealed class PhemexWsTickerPack
{
	public string Symbol { get; set; }
	public string Open { get; set; }
	public string High { get; set; }
	public string Low { get; set; }
	public string Close { get; set; }
	public string Volume { get; set; }
	public string Turnover { get; set; }
	public string OpenInterest { get; set; }
	public string IndexPrice { get; set; }
	public string MarkPrice { get; set; }
	public string FundingRate { get; set; }
	public string PredictedFundingRate { get; set; }
	public string BidPrice { get; set; }
	public string AskPrice { get; set; }
}

sealed class PhemexWsTickerPackConverter : JsonConverter<PhemexWsTickerPack>
{
	public override PhemexWsTickerPack ReadJson(JsonReader reader, Type objectType,
		PhemexWsTickerPack existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Phemex ticker row must be an array.");

		var ticker = new PhemexWsTickerPack
		{
			Symbol = PhemexJson.ReadString(reader, "ticker symbol"),
			Open = PhemexJson.ReadString(reader, "ticker open"),
			High = PhemexJson.ReadString(reader, "ticker high"),
			Low = PhemexJson.ReadString(reader, "ticker low"),
			Close = PhemexJson.ReadString(reader, "ticker close"),
			Volume = PhemexJson.ReadString(reader, "ticker volume"),
			Turnover = PhemexJson.ReadString(reader, "ticker turnover"),
			OpenInterest = PhemexJson.ReadString(reader, "ticker open interest"),
			IndexPrice = PhemexJson.ReadString(reader, "ticker index price"),
			MarkPrice = PhemexJson.ReadString(reader, "ticker mark price"),
			FundingRate = PhemexJson.ReadString(reader, "ticker funding rate"),
			PredictedFundingRate = PhemexJson.ReadString(reader, "ticker predicted funding rate"),
			BidPrice = PhemexJson.ReadString(reader, "ticker bid price"),
			AskPrice = PhemexJson.ReadString(reader, "ticker ask price"),
		};
		PhemexJson.ReadEndArray(reader, "ticker row");
		return ticker;
	}

	public override void WriteJson(JsonWriter writer, PhemexWsTickerPack value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class PhemexWsTradeMessage
{
	public string Symbol { get; set; }
	public long Timestamp { get; set; }
	public PhemexWsTrade[] Data { get; set; }
}

sealed class PhemexWsTrade
{
	public string Symbol { get; set; }
	public string TradeId { get; set; }
	public string Price { get; set; }
	public string Size { get; set; }
	public string Side { get; set; }
	public long Timestamp { get; set; }
}

sealed class PhemexWsDepthMessage
{
	public string Symbol { get; set; }
	public long Timestamp { get; set; }
	public PhemexDepthData Data { get; set; }
}

sealed class PhemexWsIndexMessage
{
	public string Symbol { get; set; }
	public long Timestamp { get; set; }
	public PhemexWsIndex[] Data { get; set; }
}

sealed class PhemexWsIndex
{
	public string Symbol { get; set; }
	public string Open { get; set; }
	public string High { get; set; }
	public string Low { get; set; }
	public string LastPrice { get; set; }
	public string Volume { get; set; }
	public string Turnover { get; set; }
	public string IndexPrice { get; set; }
	public string MarkPrice { get; set; }
	public string NextFundingRate { get; set; }
	public string BidPrice { get; set; }
	public string AskPrice { get; set; }
	public long NextFundingTime { get; set; }
	public long UpdateTime { get; set; }
}

sealed class PhemexWsOrderMessage
{
	public string Symbol { get; set; }
	public long Timestamp { get; set; }
	public PhemexOrder Data { get; set; }
}

sealed class PhemexWsFillMessage
{
	public string Symbol { get; set; }
	public long Timestamp { get; set; }
	public PhemexFill Data { get; set; }
}

sealed class PhemexWsBalanceMessage
{
	public long Timestamp { get; set; }
	public PhemexWsBalanceData Data { get; set; }
}

sealed class PhemexWsBalanceData
{
	public string Type { get; set; }
	public string Symbol { get; set; }
	public PhemexBalance[] Balances { get; set; }
	public long Timestamp { get; set; }
}

sealed class PhemexWsPositionMessage
{
	public string Symbol { get; set; }
	public long Timestamp { get; set; }
	public PhemexPosition Data { get; set; }
}

static class PhemexJson
{
	public static string ReadString(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException($"Phemex {field} is missing.");
		return CurrentString(reader, field);
	}

	public static string CurrentString(JsonReader reader, string field)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or JsonToken.Float))
			throw new JsonSerializationException($"Phemex {field} has an invalid type.");
		return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
	}

	public static long ReadInt64(JsonReader reader, string field)
	{
		var value = ReadString(reader, field);
		if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			throw new JsonSerializationException($"Phemex {field} is not an integer.");
		return result;
	}

	public static int ReadInt32(JsonReader reader, string field)
	{
		var value = ReadString(reader, field);
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			throw new JsonSerializationException($"Phemex {field} is not an integer.");
		return result;
	}

	public static void ReadEndArray(JsonReader reader, string valueName)
	{
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException($"Phemex {valueName} has an invalid field count.");
	}
}
