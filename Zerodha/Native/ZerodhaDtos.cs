namespace StockSharp.Zerodha.Native;

[AttributeUsage(AttributeTargets.Property)]
internal sealed class KiteFormFieldAttribute(string name) : Attribute
{
	public string Name { get; } = name;
}

internal sealed class KiteEnvelope<T>
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error_type")]
	public string ErrorType { get; set; }
}

internal sealed class KiteEmptyData
{
}

internal sealed class KiteSession
{
	[JsonProperty("user_id")]
	public string UserId { get; set; }

	[JsonProperty("user_name")]
	public string UserName { get; set; }

	[JsonProperty("api_key")]
	public string ApiKey { get; set; }

	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("login_time")]
	public string LoginTime { get; set; }
}

internal sealed class KiteProfile
{
	[JsonProperty("user_id")]
	public string UserId { get; set; }

	[JsonProperty("user_name")]
	public string UserName { get; set; }

	[JsonProperty("user_shortname")]
	public string UserShortName { get; set; }

	[JsonProperty("email")]
	public string Email { get; set; }

	[JsonProperty("broker")]
	public string Broker { get; set; }

	[JsonProperty("exchanges")]
	public string[] Exchanges { get; set; }

	[JsonProperty("products")]
	public string[] Products { get; set; }
}

internal sealed class KiteInstrument
{
	public long InstrumentToken { get; set; }
	public long? ExchangeToken { get; set; }
	public string TradingSymbol { get; set; }
	public string Name { get; set; }
	public decimal LastPrice { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public decimal? Strike { get; set; }
	public decimal TickSize { get; set; }
	public decimal LotSize { get; set; }
	public string InstrumentType { get; set; }
	public string Segment { get; set; }
	public string Exchange { get; set; }
}

internal sealed class KiteOrderResult
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

internal sealed class KiteOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("exchange_order_id")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("parent_order_id")]
	public string ParentOrderId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("status_message")]
	public string StatusMessage { get; set; }

	[JsonProperty("status_message_raw")]
	public string RawStatusMessage { get; set; }

	[JsonProperty("order_timestamp")]
	public string OrderTimestamp { get; set; }

	[JsonProperty("exchange_update_timestamp")]
	public string ExchangeUpdateTimestamp { get; set; }

	[JsonProperty("exchange_timestamp")]
	public string ExchangeTimestamp { get; set; }

	[JsonProperty("variety")]
	public string Variety { get; set; }

	[JsonProperty("modified")]
	public bool IsModified { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("instrument_token")]
	public long InstrumentToken { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("validity_ttl")]
	public int? ValidityTtl { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("disclosed_quantity")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("trigger_price")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("filled_quantity")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("pending_quantity")]
	public decimal PendingQuantity { get; set; }

	[JsonProperty("cancelled_quantity")]
	public decimal CancelledQuantity { get; set; }

	[JsonProperty("market_protection")]
	public decimal? MarketProtection { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }
}

internal sealed class KiteTrade
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("exchange_order_id")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("instrument_token")]
	public long InstrumentToken { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("fill_timestamp")]
	public string FillTimestamp { get; set; }

	[JsonProperty("exchange_timestamp")]
	public string ExchangeTimestamp { get; set; }
}

internal sealed class KiteMarginsData
{
	[JsonProperty("equity")]
	public KiteMarginSegment Equity { get; set; }

	[JsonProperty("commodity")]
	public KiteMarginSegment Commodity { get; set; }
}

internal sealed class KiteMarginSegment
{
	[JsonProperty("enabled")]
	public bool IsEnabled { get; set; }

	[JsonProperty("net")]
	public decimal Net { get; set; }

	[JsonProperty("available")]
	public KiteAvailableMargin Available { get; set; }

	[JsonProperty("utilised")]
	public KiteUtilisedMargin Utilised { get; set; }
}

internal sealed class KiteAvailableMargin
{
	[JsonProperty("cash")]
	public decimal Cash { get; set; }

	[JsonProperty("opening_balance")]
	public decimal OpeningBalance { get; set; }

	[JsonProperty("live_balance")]
	public decimal LiveBalance { get; set; }

	[JsonProperty("collateral")]
	public decimal Collateral { get; set; }

	[JsonProperty("intraday_payin")]
	public decimal IntradayPayin { get; set; }
}

internal sealed class KiteUtilisedMargin
{
	[JsonProperty("debits")]
	public decimal Debits { get; set; }

	[JsonProperty("exposure")]
	public decimal Exposure { get; set; }

	[JsonProperty("m2m_realised")]
	public decimal RealizedMtm { get; set; }

	[JsonProperty("m2m_unrealised")]
	public decimal UnrealizedMtm { get; set; }

	[JsonProperty("span")]
	public decimal Span { get; set; }

	[JsonProperty("option_premium")]
	public decimal OptionPremium { get; set; }
}

internal sealed class KitePositionsData
{
	[JsonProperty("net")]
	public KitePosition[] Net { get; set; }

	[JsonProperty("day")]
	public KitePosition[] Day { get; set; }
}

internal sealed class KitePosition
{
	[JsonProperty("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("instrument_token")]
	public long InstrumentToken { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("overnight_quantity")]
	public decimal OvernightQuantity { get; set; }

	[JsonProperty("multiplier")]
	public decimal Multiplier { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("close_price")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("last_price")]
	public decimal LastPrice { get; set; }

	[JsonProperty("pnl")]
	public decimal PnL { get; set; }

	[JsonProperty("m2m")]
	public decimal Mtm { get; set; }

	[JsonProperty("unrealised")]
	public decimal Unrealized { get; set; }

	[JsonProperty("realised")]
	public decimal Realized { get; set; }

	[JsonProperty("buy_quantity")]
	public decimal BuyQuantity { get; set; }

	[JsonProperty("sell_quantity")]
	public decimal SellQuantity { get; set; }
}

internal sealed class KiteHolding
{
	[JsonProperty("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("instrument_token")]
	public long InstrumentToken { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("t1_quantity")]
	public decimal T1Quantity { get; set; }

	[JsonProperty("used_quantity")]
	public decimal UsedQuantity { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("last_price")]
	public decimal LastPrice { get; set; }

	[JsonProperty("close_price")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("pnl")]
	public decimal PnL { get; set; }
}

internal sealed class KiteCandlesData
{
	[JsonProperty("candles")]
	public KiteCandle[] Candles { get; set; }
}

[JsonConverter(typeof(KiteCandleConverter))]
internal sealed class KiteCandle
{
	public DateTime OpenTime { get; set; }
	public decimal OpenPrice { get; set; }
	public decimal HighPrice { get; set; }
	public decimal LowPrice { get; set; }
	public decimal ClosePrice { get; set; }
	public decimal Volume { get; set; }
	public decimal? OpenInterest { get; set; }
}

internal sealed class KiteCandleConverter : JsonConverter<KiteCandle>
{
	public override KiteCandle ReadJson(JsonReader reader, Type objectType, KiteCandle existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Zerodha candle must be a JSON array.");

		var time = Read<string>(reader, serializer).ParseKiteTime()
			?? throw new JsonSerializationException("Zerodha candle timestamp is invalid.");
		var candle = new KiteCandle
		{
			OpenTime = time,
			OpenPrice = Read<decimal>(reader, serializer),
			HighPrice = Read<decimal>(reader, serializer),
			LowPrice = Read<decimal>(reader, serializer),
			ClosePrice = Read<decimal>(reader, serializer),
			Volume = Read<decimal>(reader, serializer),
		};

		if (!reader.Read())
			throw new JsonSerializationException("Zerodha candle is incomplete.");
		if (reader.TokenType != JsonToken.EndArray)
		{
			candle.OpenInterest = serializer.Deserialize<decimal>(reader);
			if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
				throw new JsonSerializationException("Zerodha candle has too many fields.");
		}
		return candle;
	}

	private static T Read<T>(JsonReader reader, JsonSerializer serializer)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException("Zerodha candle is incomplete.");
		return serializer.Deserialize<T>(reader);
	}

	public override void WriteJson(JsonWriter writer, KiteCandle value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

internal sealed class KiteTokenRequest
{
	[KiteFormField("api_key")]
	public string ApiKey { get; set; }

	[KiteFormField("request_token")]
	public string RequestToken { get; set; }

	[KiteFormField("checksum")]
	public string Checksum { get; set; }
}

internal sealed class KiteHistoricalRequest
{
	[KiteFormField("from")]
	public string From { get; set; }

	[KiteFormField("to")]
	public string To { get; set; }

	[KiteFormField("continuous")]
	public bool IsContinuous { get; set; }

	[KiteFormField("oi")]
	public bool IsOpenInterest { get; set; }
}

internal sealed class KitePlaceOrderRequest
{
	[KiteFormField("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[KiteFormField("exchange")]
	public string Exchange { get; set; }

	[KiteFormField("transaction_type")]
	public string TransactionType { get; set; }

	[KiteFormField("order_type")]
	public string OrderType { get; set; }

	[KiteFormField("quantity")]
	public decimal Quantity { get; set; }

	[KiteFormField("product")]
	public string Product { get; set; }

	[KiteFormField("price")]
	public decimal? Price { get; set; }

	[KiteFormField("trigger_price")]
	public decimal? TriggerPrice { get; set; }

	[KiteFormField("disclosed_quantity")]
	public decimal? DisclosedQuantity { get; set; }

	[KiteFormField("validity")]
	public string Validity { get; set; }

	[KiteFormField("validity_ttl")]
	public int? ValidityTtl { get; set; }

	[KiteFormField("market_protection")]
	public decimal? MarketProtection { get; set; }

	[KiteFormField("autoslice")]
	public bool? IsAutoSlice { get; set; }

	[KiteFormField("tag")]
	public string Tag { get; set; }
}

internal sealed class KiteModifyOrderRequest
{
	[KiteFormField("order_type")]
	public string OrderType { get; set; }

	[KiteFormField("quantity")]
	public decimal Quantity { get; set; }

	[KiteFormField("price")]
	public decimal? Price { get; set; }

	[KiteFormField("trigger_price")]
	public decimal? TriggerPrice { get; set; }

	[KiteFormField("disclosed_quantity")]
	public decimal? DisclosedQuantity { get; set; }

	[KiteFormField("validity")]
	public string Validity { get; set; }

	[KiteFormField("validity_ttl")]
	public int? ValidityTtl { get; set; }
}

internal static class KiteSocketActions
{
	public const string Subscribe = "subscribe";
	public const string Unsubscribe = "unsubscribe";
	public const string Mode = "mode";
}

internal static class KiteSocketModes
{
	public const string Ltp = "ltp";
	public const string Quote = "quote";
	public const string Full = "full";
}

internal sealed class KiteSocketTokenRequest
{
	[JsonProperty("a")]
	public string Action { get; set; }

	[JsonProperty("v")]
	public long[] Tokens { get; set; }
}

internal sealed class KiteSocketModeRequest
{
	[JsonProperty("a")]
	public string Action { get; set; }

	[JsonProperty("v")]
	public KiteSocketModeValue Value { get; set; }
}

[JsonConverter(typeof(KiteSocketModeValueConverter))]
internal sealed class KiteSocketModeValue
{
	public string Mode { get; set; }
	public long[] Tokens { get; set; }
}

internal sealed class KiteSocketModeValueConverter : JsonConverter<KiteSocketModeValue>
{
	public override KiteSocketModeValue ReadJson(JsonReader reader, Type objectType,
		KiteSocketModeValue existingValue, bool hasExistingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, KiteSocketModeValue value, JsonSerializer serializer)
	{
		writer.WriteStartArray();
		writer.WriteValue(value.Mode);
		serializer.Serialize(writer, value.Tokens);
		writer.WriteEndArray();
	}

	public override bool CanRead => false;
}

internal sealed class KiteSocketDiscriminator
{
	[JsonProperty("type")]
	public string Type { get; set; }
}

internal sealed class KiteSocketOrderEnvelope
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("data")]
	public KiteOrder Data { get; set; }
}

internal sealed class KiteSocketTextEnvelope
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }
}

internal sealed class KiteTick
{
	public long InstrumentToken { get; set; }
	public bool IsTradable { get; set; }
	public decimal LastPrice { get; set; }
	public decimal? LastQuantity { get; set; }
	public decimal? AveragePrice { get; set; }
	public decimal? Volume { get; set; }
	public decimal? TotalBuyQuantity { get; set; }
	public decimal? TotalSellQuantity { get; set; }
	public decimal? OpenPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal? ClosePrice { get; set; }
	public DateTime? LastTradeTime { get; set; }
	public DateTime? ExchangeTime { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? OpenInterestHigh { get; set; }
	public decimal? OpenInterestLow { get; set; }
	public KiteDepthEntry[] Bids { get; set; }
	public KiteDepthEntry[] Asks { get; set; }
}

internal sealed class KiteDepthEntry
{
	public decimal Price { get; set; }
	public decimal Quantity { get; set; }
	public int Orders { get; set; }
}
