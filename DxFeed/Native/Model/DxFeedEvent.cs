namespace StockSharp.DxFeed.Native.Model;

internal sealed class DxFeedEvent
{
	[JsonProperty("eventType")]
	public string EventType { get; set; }

	[JsonProperty("eventSymbol")]
	public string EventSymbol { get; set; }

	[JsonProperty("eventTime")]
	public long? EventTime { get; set; }

	[JsonProperty("eventFlags")]
	public int EventFlags { get; set; }

	[JsonProperty("index")]
	public long? Index { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("timeNanoPart")]
	public int? TimeNanoPart { get; set; }

	[JsonProperty("sequence")]
	public long? Sequence { get; set; }

	[JsonProperty("bidTime")]
	public long? BidTime { get; set; }

	[JsonProperty("bidExchangeCode")]
	public string BidExchangeCode { get; set; }

	[JsonProperty("bidPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bidSize")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? BidSize { get; set; }

	[JsonProperty("askTime")]
	public long? AskTime { get; set; }

	[JsonProperty("askExchangeCode")]
	public string AskExchangeCode { get; set; }

	[JsonProperty("askPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askSize")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? AskSize { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("shortSaleRestriction")]
	public string ShortSaleRestriction { get; set; }

	[JsonProperty("tradingStatus")]
	public string TradingStatus { get; set; }

	[JsonProperty("statusReason")]
	public string StatusReason { get; set; }

	[JsonProperty("haltStartTime")]
	public long? HaltStartTime { get; set; }

	[JsonProperty("haltEndTime")]
	public long? HaltEndTime { get; set; }

	[JsonProperty("highLimitPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? HighLimitPrice { get; set; }

	[JsonProperty("lowLimitPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? LowLimitPrice { get; set; }

	[JsonProperty("high52WeekPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? High52WeekPrice { get; set; }

	[JsonProperty("low52WeekPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Low52WeekPrice { get; set; }

	[JsonProperty("beta")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Beta { get; set; }

	[JsonProperty("earningsPerShare")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? EarningsPerShare { get; set; }

	[JsonProperty("dividendFrequency")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? DividendFrequency { get; set; }

	[JsonProperty("exDividendAmount")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? ExDividendAmount { get; set; }

	[JsonProperty("exDividendDayId")]
	public int? ExDividendDayId { get; set; }

	[JsonProperty("shares")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Shares { get; set; }

	[JsonProperty("freeFloat")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? FreeFloat { get; set; }

	[JsonProperty("exchangeCode")]
	public string ExchangeCode { get; set; }

	[JsonProperty("price")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Price { get; set; }

	[JsonProperty("change")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Change { get; set; }

	[JsonProperty("size")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Size { get; set; }

	[JsonProperty("dayId")]
	public int? DayId { get; set; }

	[JsonProperty("dayVolume")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? DayVolume { get; set; }

	[JsonProperty("dayTurnover")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? DayTurnover { get; set; }

	[JsonProperty("tickDirection")]
	public string TickDirection { get; set; }

	[JsonProperty("extendedTradingHours")]
	public bool? IsExtendedTradingHours { get; set; }

	[JsonProperty("dayOpenPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? DayOpenPrice { get; set; }

	[JsonProperty("dayHighPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? DayHighPrice { get; set; }

	[JsonProperty("dayLowPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? DayLowPrice { get; set; }

	[JsonProperty("dayClosePrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? DayClosePrice { get; set; }

	[JsonProperty("dayClosePriceType")]
	public string DayClosePriceType { get; set; }

	[JsonProperty("prevDayId")]
	public int? PreviousDayId { get; set; }

	[JsonProperty("prevDayClosePrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? PreviousDayClosePrice { get; set; }

	[JsonProperty("prevDayClosePriceType")]
	public string PreviousDayClosePriceType { get; set; }

	[JsonProperty("prevDayVolume")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? PreviousDayVolume { get; set; }

	[JsonProperty("openInterest")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("count")]
	public long? Count { get; set; }

	[JsonProperty("open")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Volume { get; set; }

	[JsonProperty("VWAP")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Vwap { get; set; }

	[JsonProperty("bidVolume")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? BidVolume { get; set; }

	[JsonProperty("askVolume")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? AskVolume { get; set; }

	[JsonProperty("impVolatility")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? ImpliedVolatility { get; set; }

	[JsonProperty("volatility")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Volatility { get; set; }

	[JsonProperty("delta")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Delta { get; set; }

	[JsonProperty("gamma")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Gamma { get; set; }

	[JsonProperty("theta")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Theta { get; set; }

	[JsonProperty("rho")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Rho { get; set; }

	[JsonProperty("vega")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Vega { get; set; }

	[JsonProperty("underlyingPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? UnderlyingPrice { get; set; }

	[JsonProperty("dividend")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Dividend { get; set; }

	[JsonProperty("interest")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Interest { get; set; }

	[JsonProperty("frontVolatility")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? FrontVolatility { get; set; }

	[JsonProperty("backVolatility")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? BackVolatility { get; set; }

	[JsonProperty("callVolume")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? CallVolume { get; set; }

	[JsonProperty("putVolume")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? PutVolume { get; set; }

	[JsonProperty("putCallRatio")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? PutCallRatio { get; set; }

	[JsonProperty("exchangeSaleConditions")]
	public string ExchangeSaleConditions { get; set; }

	[JsonProperty("tradeThroughExempt")]
	public string TradeThroughExempt { get; set; }

	[JsonProperty("aggressorSide")]
	public string AggressorSide { get; set; }

	[JsonProperty("spreadLeg")]
	public bool? IsSpreadLeg { get; set; }

	[JsonProperty("validTick")]
	public bool? IsValidTick { get; set; }

	[JsonProperty("type")]
	public string SaleType { get; set; }

	[JsonProperty("buyer")]
	public string Buyer { get; set; }

	[JsonProperty("seller")]
	public string Seller { get; set; }

	[JsonProperty("tradeId")]
	public long? TradeId { get; set; }

	[JsonProperty("optionSymbol")]
	public string OptionSymbol { get; set; }

	[JsonProperty("expiration")]
	public long? Expiration { get; set; }

	[JsonProperty("forwardPrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? ForwardPrice { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("actionTime")]
	public long? ActionTime { get; set; }

	[JsonProperty("orderId")]
	public long? OrderId { get; set; }

	[JsonProperty("auxOrderId")]
	public long? AuxiliaryOrderId { get; set; }

	[JsonProperty("executedSize")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? ExecutedSize { get; set; }

	[JsonProperty("orderSide")]
	public string OrderSide { get; set; }

	[JsonProperty("scope")]
	public string Scope { get; set; }

	[JsonProperty("tradePrice")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? TradePrice { get; set; }

	[JsonProperty("tradeSize")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? TradeSize { get; set; }

	[JsonProperty("marketMaker")]
	public string MarketMaker { get; set; }

	[JsonProperty("spreadSymbol")]
	public string SpreadSymbol { get; set; }

	[JsonProperty("icebergPeakSize")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? IcebergPeakSize { get; set; }

	[JsonProperty("icebergHiddenSize")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? IcebergHiddenSize { get; set; }

	[JsonProperty("icebergExecutedSize")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? IcebergExecutedSize { get; set; }

	[JsonProperty("icebergType")]
	public string IcebergType { get; set; }

	[JsonProperty("version")]
	public long? Version { get; set; }
}

internal sealed class DxJsonDoubleConverter : JsonConverter<decimal?>
{
	public override decimal? ReadJson(JsonReader reader, Type objectType, decimal? existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType is JsonToken.Null or JsonToken.Undefined)
			return null;

		if (reader.TokenType is JsonToken.Integer or JsonToken.Float)
			return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);

		if (reader.TokenType == JsonToken.String &&
			decimal.TryParse(reader.Value?.ToString(), NumberStyles.Float,
				CultureInfo.InvariantCulture, out var value))
			return value;

		return null;
	}

	public override void WriteJson(JsonWriter writer, decimal? value, JsonSerializer serializer)
		=> writer.WriteValue(value);
}
