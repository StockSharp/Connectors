namespace StockSharp.AliceBlue.Native.Model;

sealed class AliceBlueResponse<T>
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("stat")]
	public string LegacyStatus { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("infoMessage")]
	public string InfoMessage { get; set; }

	[JsonProperty("emsg")]
	public string ErrorMessage { get; set; }

	[JsonProperty("result")]
	public T Result { get; set; }
}

sealed class AliceBlueProfile
{
	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("clientName")]
	public string ClientName { get; set; }

	[JsonProperty("exchanges")]
	public string[] Exchanges { get; set; }

	[JsonProperty("products")]
	public string[] Products { get; set; }
}

sealed class AliceBlueSocketSessionRequest
{
	[JsonProperty("source")]
	public string Source { get; set; } = "API";

	[JsonProperty("userId")]
	public string UserId { get; set; }
}

sealed class AliceBlueSocketSessionResult
{
	[JsonProperty("Status")]
	public string Status { get; set; }
}

sealed class AliceBlueOrderToken
{
	[JsonProperty("orderToken")]
	public string Token { get; set; }
}

sealed class AliceBlueInstrumentEnvelope
{
	[JsonProperty("NSE")]
	public AliceBlueInstrument[] Nse { get; set; }

	[JsonProperty("BSE")]
	public AliceBlueInstrument[] Bse { get; set; }

	[JsonProperty("NFO")]
	public AliceBlueInstrument[] Nfo { get; set; }

	[JsonProperty("BFO")]
	public AliceBlueInstrument[] Bfo { get; set; }

	[JsonProperty("CDS")]
	public AliceBlueInstrument[] Cds { get; set; }

	[JsonProperty("BCD")]
	public AliceBlueInstrument[] Bcd { get; set; }

	[JsonProperty("MCX")]
	public AliceBlueInstrument[] Mcx { get; set; }
}

sealed class AliceBlueInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("group_name")]
	public string GroupName { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("lot_size")]
	public string LotSize { get; set; }

	[JsonProperty("instrument_type")]
	public string InstrumentType { get; set; }

	[JsonProperty("exchange_segment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("formatted_ins_name")]
	public string FormattedName { get; set; }

	[JsonProperty("tick_size")]
	public string TickSize { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("strike_price")]
	public string StrikePrice { get; set; }

	[JsonProperty("expiry_date")]
	public long? ExpiryTime { get; set; }

	[JsonProperty("board_lot_qty")]
	public string BoardLotQuantity { get; set; }

	[JsonIgnore]
	public bool IsIndex { get; set; }
}

sealed class AliceBlueOrderRequest
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("instrumentId")]
	public string InstrumentId { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("orderComplexity")]
	public string OrderComplexity { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("slLegPrice")]
	public string StopLossLegPrice { get; set; }

	[JsonProperty("targetLegPrice")]
	public string TargetLegPrice { get; set; }

	[JsonProperty("slTriggerPrice")]
	public string StopLossTriggerPrice { get; set; }

	[JsonProperty("disclosedQuantity")]
	public string DisclosedQuantity { get; set; }

	[JsonProperty("marketProtectionPercent")]
	public string MarketProtectionPercent { get; set; }

	[JsonProperty("deviceId")]
	public string DeviceId { get; set; }

	[JsonProperty("trailingSlAmount")]
	public string TrailingStopLoss { get; set; }

	[JsonProperty("apiOrderSource")]
	public string ApiOrderSource { get; set; } = "StockSharp";

	[JsonProperty("algoId")]
	public string AlgoId { get; set; }

	[JsonProperty("orderTag")]
	public string OrderTag { get; set; }
}

sealed class AliceBlueModifyOrderRequest
{
	[JsonProperty("brokerOrderId")]
	public string OrderId { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("slTriggerPrice")]
	public string StopLossTriggerPrice { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("slLegPrice")]
	public string StopLossLegPrice { get; set; }

	[JsonProperty("trailingSLAmount")]
	public string TrailingStopLoss { get; set; }

	[JsonProperty("targetLegPrice")]
	public string TargetLegPrice { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("disclosedQuantity")]
	public string DisclosedQuantity { get; set; }

	[JsonProperty("marketProtectionPercent")]
	public string MarketProtectionPercent { get; set; }

	[JsonProperty("deviceId")]
	public string DeviceId { get; set; }
}

sealed class AliceBlueCancelOrderRequest
{
	[JsonProperty("brokerOrderId")]
	public string OrderId { get; set; }
}

sealed class AliceBlueOrderResult
{
	[JsonProperty("requestTime")]
	public string RequestTime { get; set; }

	[JsonProperty("brokerOrderId")]
	public string OrderId { get; set; }
}

sealed class AliceBlueOrder
{
	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("brokerOrderId")]
	public string OrderId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchangeOrderId")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("formattedInstrumentName")]
	public string FormattedInstrumentName { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("instrumentId")]
	public string InstrumentId { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("orderComplexity")]
	public string OrderComplexity { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("averageTradedPrice")]
	public decimal AverageTradedPrice { get; set; }

	[JsonProperty("slTriggerPrice")]
	public decimal StopLossTriggerPrice { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("disclosedQuantity")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("orderTime")]
	public string OrderTime { get; set; }

	[JsonProperty("exchangeUpdateTime")]
	public string ExchangeUpdateTime { get; set; }

	[JsonProperty("rejectionReason")]
	public string RejectionReason { get; set; }

	[JsonProperty("cancelledQuantity")]
	public decimal CancelledQuantity { get; set; }

	[JsonProperty("pendingQuantity")]
	public decimal PendingQuantity { get; set; }

	[JsonProperty("filledQuantity")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("algoId")]
	public string AlgoId { get; set; }

	[JsonProperty("orderTag")]
	public string OrderTag { get; set; }

	[JsonProperty("trailingSlAmount")]
	public decimal TrailingStopLoss { get; set; }

	[JsonProperty("marketProtectionPercent")]
	public string MarketProtectionPercent { get; set; }

	[JsonProperty("brokerUpdateTime")]
	public string BrokerUpdateTime { get; set; }

	[JsonProperty("exchangeTimestamp")]
	public string ExchangeTimestamp { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }
}

sealed class AliceBlueTrade
{
	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("brokerOrderId")]
	public string OrderId { get; set; }

	[JsonProperty("exchangeOrderId")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("exchangeTradeId")]
	public string TradeId { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("instrumentId")]
	public string InstrumentId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("tradedPrice")]
	public decimal TradedPrice { get; set; }

	[JsonProperty("filledQuantity")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("orderTime")]
	public string OrderTime { get; set; }

	[JsonProperty("fillTimestamp")]
	public string FillTime { get; set; }
}

sealed class AliceBluePosition
{
	[JsonProperty("instrumentId")]
	public string InstrumentId { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("netQuantity")]
	public decimal NetQuantity { get; set; }

	[JsonProperty("netAveragePrice")]
	public decimal NetAveragePrice { get; set; }

	[JsonProperty("realizedPnl")]
	public decimal RealizedPnL { get; set; }

	[JsonProperty("buyPrice")]
	public decimal BuyPrice { get; set; }

	[JsonProperty("sellPrice")]
	public decimal SellPrice { get; set; }

	[JsonProperty("previousDayClose")]
	public decimal? PreviousDayClose { get; set; }
}

sealed class AliceBlueHolding
{
	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("nseInstrumentId")]
	public string NseInstrumentId { get; set; }

	[JsonProperty("bseInstrumentId")]
	public string BseInstrumentId { get; set; }

	[JsonProperty("nseTradingSymbol")]
	public string NseTradingSymbol { get; set; }

	[JsonProperty("bseTradingSymbol")]
	public string BseTradingSymbol { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("formattedInstrumentName")]
	public string FormattedInstrumentName { get; set; }

	[JsonProperty("averageTradedPrice")]
	public decimal AverageTradedPrice { get; set; }

	[JsonProperty("collateralQuantity")]
	public decimal CollateralQuantity { get; set; }

	[JsonProperty("authorizedQuantity")]
	public decimal AuthorizedQuantity { get; set; }

	[JsonProperty("dpQuantity")]
	public decimal DpQuantity { get; set; }

	[JsonProperty("totalQuantity")]
	public decimal TotalQuantity { get; set; }

	[JsonProperty("t1Quantity")]
	public decimal T1Quantity { get; set; }

	[JsonProperty("ltp")]
	public decimal LastPrice { get; set; }

	[JsonProperty("sellableQty")]
	public decimal SellableQuantity { get; set; }

	[JsonProperty("investedPrice")]
	public decimal InvestedPrice { get; set; }
}

sealed class AliceBlueLimits
{
	[JsonProperty("tradingLimit")]
	public decimal TradingLimit { get; set; }

	[JsonProperty("openingCashLimit")]
	public decimal OpeningCashLimit { get; set; }

	[JsonProperty("intradayPayin")]
	public decimal IntradayPayIn { get; set; }

	[JsonProperty("collateralMargin")]
	public decimal CollateralMargin { get; set; }

	[JsonProperty("creditForSell")]
	public decimal CreditForSell { get; set; }

	[JsonProperty("adhocMargin")]
	public decimal AdhocMargin { get; set; }

	[JsonProperty("utilizedMargin")]
	public decimal UtilizedMargin { get; set; }

	[JsonProperty("blockedForPayout")]
	public decimal BlockedForPayout { get; set; }
}

sealed class AliceBlueHistoryRequest
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("resolution")]
	public string Resolution { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }

	[JsonProperty("to")]
	public string To { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }
}

sealed class AliceBlueHistoryResponse
{
	[JsonProperty("stat")]
	public string Status { get; set; }

	[JsonProperty("emsg")]
	public string ErrorMessage { get; set; }

	[JsonProperty("result")]
	[JsonConverter(typeof(AliceBlueCandleArrayConverter))]
	public AliceBlueCandle[] Result { get; set; }
}

sealed class AliceBlueCandle
{
	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

sealed class AliceBlueCandleArrayConverter : JsonConverter<AliceBlueCandle[]>
{
	public override AliceBlueCandle[] ReadJson(JsonReader reader, Type objectType,
		AliceBlueCandle[] existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return [];
		if (reader.TokenType == JsonToken.String)
			return JsonConvert.DeserializeObject<AliceBlueCandle[]>(reader.Value?.ToString()) ?? [];
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Alice Blue history result must be a JSON array or an encoded JSON array.");

		var result = new List<AliceBlueCandle>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
		{
			var candle = serializer.Deserialize<AliceBlueCandle>(reader);
			if (candle != null)
				result.Add(candle);
		}
		return [.. result];
	}

	public override void WriteJson(JsonWriter writer, AliceBlueCandle[] value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

class AliceBlueSocketEnvelope
{
	[JsonProperty("t")]
	public string Type { get; set; }
}

sealed class AliceBlueMarketLoginRequest
{
	[JsonProperty("susertoken")]
	public string SessionToken { get; set; }

	[JsonProperty("t")]
	public string Type { get; set; } = "c";

	[JsonProperty("actid")]
	public string AccountId { get; set; }

	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; } = "API";
}

sealed class AliceBlueMarketSubscriptionRequest
{
	[JsonProperty("k")]
	public string Instruments { get; set; }

	[JsonProperty("t")]
	public string Type { get; set; }
}

sealed class AliceBlueMarketHeartbeat
{
	[JsonProperty("k")]
	public string Key { get; set; } = string.Empty;

	[JsonProperty("t")]
	public string Type { get; set; } = "h";
}

sealed class AliceBlueMarketAcknowledgement : AliceBlueSocketEnvelope
{
	[JsonProperty("k")]
	public string Key { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class AliceBlueMarketUpdate : AliceBlueSocketEnvelope
{
	[JsonProperty("e")]
	public string Exchange { get; set; }
	[JsonProperty("tk")]
	public string Token { get; set; }
	[JsonProperty("ts")]
	public string TradingSymbol { get; set; }
	[JsonProperty("pp")]
	public string Precision { get; set; }
	[JsonProperty("ti")]
	public string TickSize { get; set; }
	[JsonProperty("ls")]
	public string LotSize { get; set; }
	[JsonProperty("lp")]
	public string LastPrice { get; set; }
	[JsonProperty("ltq")]
	public string LastQuantity { get; set; }
	[JsonProperty("ltt")]
	public string LastTradeTime { get; set; }
	[JsonProperty("v")]
	public string Volume { get; set; }
	[JsonProperty("o")]
	public string Open { get; set; }
	[JsonProperty("h")]
	public string High { get; set; }
	[JsonProperty("l")]
	public string Low { get; set; }
	[JsonProperty("c")]
	public string Close { get; set; }
	[JsonProperty("ap")]
	public string AveragePrice { get; set; }
	[JsonProperty("oi")]
	public string OpenInterest { get; set; }
	[JsonProperty("tbq")]
	public string TotalBuyQuantity { get; set; }
	[JsonProperty("tsq")]
	public string TotalSellQuantity { get; set; }
	[JsonProperty("lc")]
	public string LowerCircuit { get; set; }
	[JsonProperty("uc")]
	public string UpperCircuit { get; set; }
	[JsonProperty("52h")]
	public string YearHigh { get; set; }
	[JsonProperty("52l")]
	public string YearLow { get; set; }
	[JsonProperty("ft")]
	public string FeedTime { get; set; }

	[JsonProperty("bp1")]
	public string BidPrice1 { get; set; }
	[JsonProperty("bq1")]
	public string BidQuantity1 { get; set; }
	[JsonProperty("bo1")]
	public string BidOrders1 { get; set; }
	[JsonProperty("sp1")]
	public string AskPrice1 { get; set; }
	[JsonProperty("sq1")]
	public string AskQuantity1 { get; set; }
	[JsonProperty("so1")]
	public string AskOrders1 { get; set; }
	[JsonProperty("bp2")]
	public string BidPrice2 { get; set; }
	[JsonProperty("bq2")]
	public string BidQuantity2 { get; set; }
	[JsonProperty("bo2")]
	public string BidOrders2 { get; set; }
	[JsonProperty("sp2")]
	public string AskPrice2 { get; set; }
	[JsonProperty("sq2")]
	public string AskQuantity2 { get; set; }
	[JsonProperty("so2")]
	public string AskOrders2 { get; set; }
	[JsonProperty("bp3")]
	public string BidPrice3 { get; set; }
	[JsonProperty("bq3")]
	public string BidQuantity3 { get; set; }
	[JsonProperty("bo3")]
	public string BidOrders3 { get; set; }
	[JsonProperty("sp3")]
	public string AskPrice3 { get; set; }
	[JsonProperty("sq3")]
	public string AskQuantity3 { get; set; }
	[JsonProperty("so3")]
	public string AskOrders3 { get; set; }
	[JsonProperty("bp4")]
	public string BidPrice4 { get; set; }
	[JsonProperty("bq4")]
	public string BidQuantity4 { get; set; }
	[JsonProperty("bo4")]
	public string BidOrders4 { get; set; }
	[JsonProperty("sp4")]
	public string AskPrice4 { get; set; }
	[JsonProperty("sq4")]
	public string AskQuantity4 { get; set; }
	[JsonProperty("so4")]
	public string AskOrders4 { get; set; }
	[JsonProperty("bp5")]
	public string BidPrice5 { get; set; }
	[JsonProperty("bq5")]
	public string BidQuantity5 { get; set; }
	[JsonProperty("bo5")]
	public string BidOrders5 { get; set; }
	[JsonProperty("sp5")]
	public string AskPrice5 { get; set; }
	[JsonProperty("sq5")]
	public string AskQuantity5 { get; set; }
	[JsonProperty("so5")]
	public string AskOrders5 { get; set; }
}

sealed class AliceBlueDepthLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public int OrdersCount { get; set; }
}

sealed class AliceBlueOrderSocketLogin
{
	[JsonProperty("orderToken")]
	public string OrderToken { get; set; }

	[JsonProperty("userId")]
	public string UserId { get; set; }
}

sealed class AliceBlueOrderSocketHeartbeat
{
	[JsonProperty("heartbeat")]
	public string Heartbeat { get; set; } = "h";

	[JsonProperty("userId")]
	public string UserId { get; set; }
}

class AliceBlueOrderSocketEnvelope : AliceBlueSocketEnvelope
{
	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class AliceBlueOrderUpdate : AliceBlueOrderSocketEnvelope
{
	[JsonProperty("norenordno")]
	public string OrderId { get; set; }
	[JsonProperty("uid")]
	public string UserId { get; set; }
	[JsonProperty("actid")]
	public string AccountId { get; set; }
	[JsonProperty("exch")]
	public string Exchange { get; set; }
	[JsonProperty("tsym")]
	public string TradingSymbol { get; set; }
	[JsonProperty("qty")]
	public string Quantity { get; set; }
	[JsonProperty("prc")]
	public string Price { get; set; }
	[JsonProperty("pcode")]
	public string Product { get; set; }
	[JsonProperty("prd")]
	public string LegacyProduct { get; set; }
	[JsonIgnore]
	public string EffectiveProduct => Product.IsEmpty(LegacyProduct);
	[JsonProperty("reporttype")]
	public string ReportType { get; set; }
	[JsonProperty("trantype")]
	public string TransactionType { get; set; }
	[JsonProperty("prctyp")]
	public string OrderType { get; set; }
	[JsonProperty("ret")]
	public string Validity { get; set; }
	[JsonProperty("fillshares")]
	public string FilledQuantity { get; set; }
	[JsonProperty("avgprc")]
	public string AveragePrice { get; set; }
	[JsonProperty("fltm")]
	public string FillTime { get; set; }
	[JsonProperty("flid")]
	public string FillId { get; set; }
	[JsonProperty("flqty")]
	public string FillQuantity { get; set; }
	[JsonProperty("flprc")]
	public string FillPrice { get; set; }
	[JsonProperty("rejreason")]
	public string RejectionReason { get; set; }
	[JsonProperty("exchordid")]
	public string ExchangeOrderId { get; set; }
	[JsonProperty("cancelqty")]
	public string CancelledQuantity { get; set; }
	[JsonProperty("remarks")]
	public string OrderTag { get; set; }
	[JsonProperty("dscqty")]
	public string DisclosedQuantity { get; set; }
	[JsonProperty("trgprc")]
	public string TriggerPrice { get; set; }
	[JsonProperty("blprc")]
	public string StopLossLegPrice { get; set; }
	[JsonProperty("bpprc")]
	public string TargetLegPrice { get; set; }
	[JsonProperty("trailprc")]
	public string TrailingStopLoss { get; set; }
	[JsonProperty("exch_tm")]
	public string ExchangeTime { get; set; }
	[JsonProperty("tm")]
	public string Time { get; set; }
}
