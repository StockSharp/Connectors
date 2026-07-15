namespace StockSharp.Upstox.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxResponse<T>
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxWebSocketData
{
	[JsonProperty("authorizedRedirectUri")]
	public string AuthorizedRedirectUri { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxInstrument
{
	[JsonProperty("weekly")]
	public bool? Weekly { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("expiry")]
	public long? Expiry { get; set; }

	[JsonProperty("instrument_type")]
	public string InstrumentType { get; set; }

	[JsonProperty("instrument_key")]
	public string InstrumentKey { get; set; }

	[JsonProperty("exchange_token")]
	public string ExchangeToken { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("lot_size")]
	public decimal? LotSize { get; set; }

	[JsonProperty("minimum_lot")]
	public decimal? MinimumLot { get; set; }

	[JsonProperty("tick_size")]
	public decimal? TickSize { get; set; }

	[JsonProperty("qty_multiplier")]
	public decimal? QuantityMultiplier { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("strike_price")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("underlying_symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("underlying_key")]
	public string UnderlyingKey { get; set; }

	[JsonProperty("asset_symbol")]
	public string AssetSymbol { get; set; }

	[JsonProperty("asset_key")]
	public string AssetKey { get; set; }

	[JsonProperty("asset_type")]
	public string AssetType { get; set; }

	[JsonProperty("security_type")]
	public string SecurityType { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxProfile
{
	[JsonProperty("user_id")]
	public string UserId { get; set; }

	[JsonProperty("user_name")]
	public string UserName { get; set; }

	[JsonProperty("email")]
	public string Email { get; set; }

	[JsonProperty("broker")]
	public string Broker { get; set; }

	[JsonProperty("is_active")]
	public bool IsActive { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxFunds
{
	[JsonProperty("equity")]
	public UpstoxFund Equity { get; set; }

	[JsonProperty("commodity")]
	public UpstoxFund Commodity { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxFund
{
	[JsonProperty("used_margin")]
	public decimal? UsedMargin { get; set; }

	[JsonProperty("payin_amount")]
	public decimal? PayInAmount { get; set; }

	[JsonProperty("span_margin")]
	public decimal? SpanMargin { get; set; }

	[JsonProperty("adhoc_margin")]
	public decimal? AdhocMargin { get; set; }

	[JsonProperty("notional_cash")]
	public decimal? NotionalCash { get; set; }

	[JsonProperty("available_margin")]
	public decimal? AvailableMargin { get; set; }

	[JsonProperty("exposure_margin")]
	public decimal? ExposureMargin { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class UpstoxPosition
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("instrument_token")]
	public string InstrumentToken { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("tradingsymbol")]
	public string TradingSymbolLegacy { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("average_price")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("last_price")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("pnl")]
	public decimal? PnL { get; set; }

	[JsonProperty("unrealised")]
	public decimal? UnrealizedPnL { get; set; }

	[JsonProperty("realised")]
	public decimal? RealizedPnL { get; set; }

	[JsonProperty("buy_value")]
	public decimal? BuyValue { get; set; }

	[JsonProperty("sell_value")]
	public decimal? SellValue { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxHolding : UpstoxPosition
{
	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("company_name")]
	public string CompanyName { get; set; }

	[JsonProperty("t1_quantity")]
	public decimal? T1Quantity { get; set; }

	[JsonProperty("collateral_quantity")]
	public decimal? CollateralQuantity { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxOrder
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }

	[JsonProperty("instrument_token")]
	public string InstrumentToken { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("tradingsymbol")]
	public string TradingSymbolLegacy { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("trigger_price")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("average_price")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("filled_quantity")]
	public decimal? FilledQuantity { get; set; }

	[JsonProperty("pending_quantity")]
	public decimal? PendingQuantity { get; set; }

	[JsonProperty("status_message")]
	public string StatusMessage { get; set; }

	[JsonProperty("exchange_order_id")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("order_timestamp")]
	public string OrderTimestamp { get; set; }

	[JsonProperty("exchange_timestamp")]
	public string ExchangeTimestamp { get; set; }

	[JsonProperty("is_amo")]
	public bool? IsAfterMarket { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxTrade
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("tradingsymbol")]
	public string TradingSymbolLegacy { get; set; }

	[JsonProperty("instrument_token")]
	public string InstrumentToken { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("exchange_timestamp")]
	public string ExchangeTimestamp { get; set; }

	[JsonProperty("average_price")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("order_timestamp")]
	public string OrderTimestamp { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxPlaceOrderRequest
{
	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("tag", NullValueHandling = NullValueHandling.Ignore)]
	public string Tag { get; set; }

	[JsonProperty("slice")]
	public bool Slice { get; set; }

	[JsonProperty("instrument_token")]
	public string InstrumentToken { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("disclosed_quantity")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("trigger_price")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("is_amo")]
	public bool IsAfterMarket { get; set; }

	[JsonProperty("market_protection", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? MarketProtection { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxModifyOrderRequest
{
	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("disclosed_quantity")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("trigger_price")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("market_protection", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? MarketProtection { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxOrderIds
{
	[JsonProperty("order_ids")]
	public string[] OrderIds { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxOrderId
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class UpstoxCandleData
{
	[JsonProperty("candles")]
	[JsonConverter(typeof(UpstoxCandleArrayConverter))]
	public UpstoxCandle[] Candles { get; set; }
}

sealed class UpstoxCandle
{
	public DateTime Time { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public decimal OpenInterest { get; set; }
}

sealed class UpstoxCandleArrayConverter : JsonConverter
{
	public override bool CanWrite => false;
	public override bool CanConvert(System.Type objectType) => objectType == typeof(UpstoxCandle[]);

	public override object ReadJson(JsonReader reader, System.Type objectType, object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return Array.Empty<UpstoxCandle>();
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Upstox candles must be a JSON array.");

		var candles = new List<UpstoxCandle>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
		{
			if (reader.TokenType != JsonToken.StartArray)
				throw new JsonSerializationException("An Upstox candle must be a JSON array.");

			ReadValue(reader);
			var candle = new UpstoxCandle
			{
				Time = DateTimeOffset.Parse(reader.Value?.ToString() ?? throw new JsonSerializationException("Upstox candle time is missing."), CultureInfo.InvariantCulture).UtcDateTime,
			};

			candle.Open = ReadDecimal(reader);
			candle.High = ReadDecimal(reader);
			candle.Low = ReadDecimal(reader);
			candle.Close = ReadDecimal(reader);
			candle.Volume = ReadDecimal(reader);

			ReadValue(reader);
			if (reader.TokenType != JsonToken.EndArray)
			{
				candle.OpenInterest = Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
				while (reader.Read() && reader.TokenType != JsonToken.EndArray)
				{
				}
			}

			candles.Add(candle);
		}

		return candles.ToArray();
	}

	private static decimal ReadDecimal(JsonReader reader)
	{
		ReadValue(reader);
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}

	private static void ReadValue(JsonReader reader)
	{
		if (!reader.Read())
			throw new JsonSerializationException("Unexpected end of an Upstox candle.");
	}

	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}
