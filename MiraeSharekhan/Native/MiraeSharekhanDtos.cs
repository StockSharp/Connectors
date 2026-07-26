namespace StockSharp.MiraeSharekhan.Native;

internal class MiraeSharekhanResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("success")]
	public string Success { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errorcode")]
	public string ErrorCode { get; set; }

	[JsonProperty("error_code")]
	public string ErrorCode2 { get; set; }

	[JsonProperty("error_type")]
	public string ErrorType { get; set; }

	[JsonProperty("errorType")]
	public string ErrorType2 { get; set; }

	public string GetErrorCode() => ErrorCode.IsEmpty(ErrorCode2);

	public bool IsFailed()
	{
		var errorType = ErrorType.IsEmpty(ErrorType2);
		if (!GetErrorCode().IsEmpty() || !errorType.IsEmpty())
			return true;
		var status = Status.IsEmpty(Success);
		if (int.TryParse(status, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var statusCode))
			return statusCode >= 400;
		return status.EqualsIgnoreCase("false") || status.EqualsIgnoreCase("failed") ||
			status.EqualsIgnoreCase("error");
	}
}

internal sealed class MiraeSharekhanItemsResponse<T> : MiraeSharekhanResponse
{
	[JsonProperty("data")]
	public T[] Data { get; set; }

	[JsonProperty("records")]
	public T[] Records { get; set; }

	[JsonProperty("result")]
	public T[] Result { get; set; }

	[JsonProperty("master")]
	public T[] Master { get; set; }

	[JsonProperty("orders")]
	public T[] Orders { get; set; }

	[JsonProperty("trades")]
	public T[] Trades { get; set; }

	[JsonProperty("holdings")]
	public T[] Holdings { get; set; }

	[JsonProperty("positions")]
	public T[] Positions { get; set; }

	[JsonProperty("candles")]
	public T[] Candles { get; set; }

	public T[] GetItems()
		=> Data ?? Records ?? Result ?? Master ?? Orders ?? Trades ?? Holdings ?? Positions ?? Candles ?? [];
}

internal sealed class MiraeSharekhanInstrument
{
	[JsonProperty("scripCode")]
	public string ScripCode { get; set; }

	[JsonProperty("scripcode")]
	public string ScripCode2 { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("scripName")]
	public string ScripName { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("companyName")]
	public string CompanyName { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("instType")]
	public string InstrumentType { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType2 { get; set; }

	[JsonProperty("lotSize")]
	public decimal? LotSize { get; set; }

	[JsonProperty("tickSize")]
	public decimal? TickSize { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("expiryDate")]
	public string ExpiryDate { get; set; }

	[JsonProperty("strike")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("strikePrice")]
	public decimal? StrikePrice2 { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("isinCode")]
	public string Isin { get; set; }

	[JsonProperty("isin")]
	public string Isin2 { get; set; }

	[OnDeserialized]
	private void OnDeserialized(StreamingContext context)
	{
		_ = context;
		InstrumentType = InstrumentType.IsEmpty(InstrumentType2);
		StrikePrice ??= StrikePrice2;
		Isin = Isin.IsEmpty(Isin2);
	}

	public long GetScripCode()
		=> long.TryParse(ScripCode.IsEmpty(ScripCode2), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var value) ? value : 0;

	public string GetSymbol()
		=> TradingSymbol.IsEmpty(Symbol).IsEmpty(ScripName).IsEmpty(GetScripCode().ToString(CultureInfo.InvariantCulture));

	public string GetName()
		=> CompanyName.IsEmpty(Name).IsEmpty(ScripName).IsEmpty(GetSymbol());

	public DateTime? GetExpiryDate() => ExpiryDate.IsEmpty(Expiry).ParseIndiaTime();
}

internal sealed class MiraeSharekhanHistoricalCandle
{
	[JsonProperty("tradeDate")]
	public string Date { get; set; }

	[JsonProperty("date")]
	public string Date2 { get; set; }

	[JsonProperty("tradeTime")]
	public string Time { get; set; }

	[JsonProperty("time")]
	public string Time2 { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("open")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("high")]
	public decimal HighPrice { get; set; }

	[JsonProperty("low")]
	public decimal LowPrice { get; set; }

	[JsonProperty("close")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("qty")]
	public decimal Volume { get; set; }

	[JsonProperty("volume")]
	public decimal Volume2 { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("oi")]
	public decimal? OpenInterest2 { get; set; }

	public DateTime? GetTime()
	{
		var date = Date.IsEmpty(Date2);
		var time = Time.IsEmpty(Time2);
		var value = Timestamp.IsEmpty(date.IsEmpty() ? time : $"{date} {time}".Trim());
		return value.ParseIndiaTime();
	}

	[OnDeserialized]
	private void OnDeserialized(StreamingContext context)
	{
		_ = context;
		if (Volume == 0)
			Volume = Volume2;
	}
}

internal sealed class MiraeSharekhanOrderRequest
{
	[JsonProperty("orderId", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderId { get; set; }

	[JsonProperty("customerId")]
	public string CustomerId { get; set; }

	[JsonProperty("scripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("disclosedQty")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("executedQty", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? ExecutedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("rmsCode")]
	public string RmsCode { get; set; }

	[JsonProperty("afterHour")]
	public string AfterHour { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; } = "NORMAL";

	[JsonProperty("channelUser")]
	public string ChannelUser { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; } = "GFD";

	[JsonProperty("requestType")]
	public string RequestType { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("strikePrice")]
	public decimal StrikePrice { get; set; } = -1;

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; } = "XX";
}

internal sealed class MiraeSharekhanOrderResponse : MiraeSharekhanResponse
{
	[JsonProperty("data")]
	public MiraeSharekhanOrderResult Data { get; set; }

	[JsonProperty("result")]
	public MiraeSharekhanOrderResult Result { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderNumber")]
	public string OrderNumber { get; set; }

	public string GetOrderId()
		=> (Data?.GetOrderId()).IsEmpty(Result?.GetOrderId()).IsEmpty(OrderId)
			.IsEmpty(OrderNumber);

	public string GetRmsCode() => (Data?.RmsCode).IsEmpty(Result?.RmsCode);

	public string GetErrorMessage()
		=> (Data?.ErrorMessage).IsEmpty(Result?.ErrorMessage);
}

internal sealed class MiraeSharekhanOrderResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderNumber")]
	public string OrderNumber { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("rmscode")]
	public string RmsCode { get; set; }

	[JsonProperty("errormsg")]
	public string ErrorMessage { get; set; }

	public string GetOrderId() => OrderId.IsEmpty(OrderNumber);
}

internal class MiraeSharekhanOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("customerId")]
	public string CustomerId { get; set; }

	[JsonProperty("scripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("buySell")]
	public string TransactionType { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType2 { get; set; }

	[JsonProperty("orderQty")]
	public decimal Quantity { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity2 { get; set; }

	[JsonProperty("disclosedQty")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("execQty")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("executedQty")]
	public decimal ExecutedQuantity2 { get; set; }

	[JsonProperty("filledQty")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("pendingQty")]
	public decimal? PendingQuantity { get; set; }

	[JsonProperty("orderPrice")]
	public decimal Price { get; set; }

	[JsonProperty("price")]
	public decimal Price2 { get; set; }

	[JsonProperty("execPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice2 { get; set; }

	[JsonProperty("trigPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice2 { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("lastModTime")]
	public string OrderTime { get; set; }

	[JsonProperty("orderDateTime")]
	public string OrderTime2 { get; set; }

	[JsonProperty("exchDateTime")]
	public string ExchangeTime { get; set; }

	[JsonProperty("exchangeTime")]
	public string ExchangeTime2 { get; set; }

	[JsonProperty("productCode")]
	public string ProductType { get; set; }

	[JsonProperty("productType")]
	public string ProductType2 { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("strikePrice")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("expiryDate")]
	public string Expiry { get; set; }

	[JsonProperty("expiry")]
	public string Expiry2 { get; set; }

	[JsonProperty("errorMsg")]
	public string RejectionReason { get; set; }

	[JsonProperty("rejectionReason")]
	public string RejectionReason2 { get; set; }

	[JsonProperty("rmsCode")]
	public string RmsCode { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[OnDeserialized]
	private void OnDeserialized(StreamingContext context)
	{
		_ = context;
		TransactionType = TransactionType.IsEmpty(TransactionType2);
		if (Quantity == 0)
			Quantity = Quantity2;
		if (ExecutedQuantity == 0)
			ExecutedQuantity = Math.Max(ExecutedQuantity2, FilledQuantity);
		if (Price == 0)
			Price = Price2;
		AveragePrice ??= AveragePrice2;
		if (TriggerPrice == 0)
			TriggerPrice = TriggerPrice2;
		OrderTime = OrderTime.IsEmpty(OrderTime2);
		ExchangeTime = ExchangeTime.IsEmpty(ExchangeTime2);
		ProductType = ProductType.IsEmpty(ProductType2);
		Expiry = Expiry.IsEmpty(Expiry2);
		RejectionReason = RejectionReason.IsEmpty(RejectionReason2);
	}

	public decimal GetFilledQuantity() => Math.Max(ExecutedQuantity, FilledQuantity);
	public string GetStatus() => OrderStatus.IsEmpty(Status);
	public DateTime? GetTime() => ExchangeTime.IsEmpty(OrderTime).ParseIndiaTime();
}

internal sealed class MiraeSharekhanPosition
{
	[JsonProperty("scripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("netQty")]
	public decimal? NetQuantity { get; set; }

	[JsonProperty("buyQty")]
	public decimal? BuyQuantity { get; set; }

	[JsonProperty("sellQty")]
	public decimal? SellQuantity { get; set; }

	[JsonProperty("avgPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("bpl")]
	public decimal? RealizedPnL { get; set; }

	[JsonProperty("mtm")]
	public decimal? UnrealizedPnL { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }
}

internal sealed class MiraeSharekhanHolding
{
	[JsonProperty("scripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("totalQty")]
	public decimal TotalQuantity { get; set; }

	[JsonProperty("availableQty")]
	public decimal AvailableQuantity { get; set; }

	[JsonProperty("aval")]
	public decimal? AvailableQuantity2 { get; set; }

	[JsonProperty("dp")]
	public decimal? DepositoryQuantity { get; set; }

	[JsonProperty("invstQty")]
	public decimal? InvestmentQuantity { get; set; }

	[JsonProperty("marginQty")]
	public decimal? MarginQuantity { get; set; }

	[JsonProperty("dpmarginQty")]
	public decimal? DepositoryMarginQuantity { get; set; }

	[JsonProperty("cncqty")]
	public decimal? CncQuantity { get; set; }

	[JsonProperty("holdPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice2 { get; set; }

	[JsonProperty("ltp")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	public decimal GetQuantity()
		=> TotalQuantity != 0 ? TotalQuantity :
			Quantity != 0 ? Quantity :
			DepositoryQuantity is not null and not 0 ? DepositoryQuantity.Value :
			AvailableQuantity2 is not null and not 0 ? AvailableQuantity2.Value :
			AvailableQuantity != 0 ? AvailableQuantity :
			InvestmentQuantity is not null and not 0 ? InvestmentQuantity.Value :
			CncQuantity ?? 0;

	[OnDeserialized]
	private void OnDeserialized(StreamingContext context)
	{
		_ = context;
		AveragePrice ??= AveragePrice2;
	}
}

internal sealed class MiraeSharekhanFunds
{
	[JsonProperty("currentCashBalance")]
	public decimal? CurrentCashBalance { get; set; }

	[JsonProperty("pendingWithdrawalRequest")]
	public decimal? PendingWithdrawalRequest { get; set; }

	[JsonProperty("nonCashLimit")]
	public decimal? NonCashLimit { get; set; }

	[JsonProperty("cashBpl")]
	public decimal? CashProfitLoss { get; set; }

	[JsonProperty("limitAgainstShares")]
	public decimal? LimitAgainstShares { get; set; }

	[JsonProperty("cashPreviousSettlementExposure")]
	public decimal? PreviousSettlementExposure { get; set; }

	[JsonProperty("intradayMarginCash")]
	public decimal? IntradayCashMargin { get; set; }

	[JsonProperty("fnoPremium")]
	public decimal? DerivativesPremium { get; set; }

	[JsonProperty("fnoBpl")]
	public decimal? DerivativesProfitLoss { get; set; }

	[JsonProperty("intradayMarginFno")]
	public decimal? IntradayDerivativesMargin { get; set; }

	[JsonProperty("holdFunds")]
	public decimal? HoldFunds { get; set; }

	[JsonProperty("availableBalance")]
	public decimal? AvailableBalance { get; set; }

	[JsonProperty("availableMargin")]
	public decimal? AvailableMargin { get; set; }

	[JsonProperty("cashBalance")]
	public decimal? CashBalance { get; set; }

	[JsonProperty("openingBalance")]
	public decimal? OpeningBalance { get; set; }

	[JsonProperty("utilizedAmount")]
	public decimal? UtilizedAmount { get; set; }

	[JsonProperty("usedMargin")]
	public decimal? UsedMargin { get; set; }

	[JsonProperty("collateral")]
	public decimal? Collateral { get; set; }

	public decimal? GetOpening() => OpeningBalance ?? CurrentCashBalance;
	public decimal? GetAvailable()
		=> AvailableBalance ?? AvailableMargin ?? CashBalance ?? CurrentCashBalance;
	public decimal? GetBlocked() => UtilizedAmount ?? UsedMargin ?? HoldFunds;
}

internal sealed class MiraeSharekhanSocketRequest
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("key")]
	public string[] Key { get; set; }

	[JsonProperty("value")]
	public string[] Value { get; set; }
}

internal class MiraeSharekhanStreamMessage
{
	[JsonProperty("exchangeCode")]
	public string Exchange { get; set; }

	[JsonProperty("exchange")]
	public string Exchange2 { get; set; }

	[JsonProperty("scripCode")]
	public string ScripCode { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ltp")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice2 { get; set; }

	[JsonProperty("ltq")]
	public decimal? LastQuantity { get; set; }

	[JsonProperty("lastQty")]
	public decimal? LastQuantity2 { get; set; }

	[JsonProperty("lastTradeQty")]
	public decimal? LastQuantity3 { get; set; }

	[JsonProperty("open")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("high")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("close")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("qty")]
	public decimal? Volume { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume2 { get; set; }

	[JsonProperty("currentOI")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("oi")]
	public decimal? OpenInterest2 { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest3 { get; set; }

	[JsonProperty("lastUpdatedTime")]
	public string Timestamp { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp2 { get; set; }

	[JsonProperty("ltt")]
	public string Time { get; set; }

	[JsonProperty("time")]
	public string Time2 { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BestBidPrice { get; set; }

	[JsonProperty("bid")]
	public decimal? BestBidPrice2 { get; set; }

	[JsonProperty("bidQty")]
	public decimal? BestBidQuantity { get; set; }

	[JsonProperty("offPrice")]
	public decimal? BestAskPrice { get; set; }

	[JsonProperty("ask")]
	public decimal? BestAskPrice2 { get; set; }

	[JsonProperty("offQty")]
	public decimal? BestAskQuantity { get; set; }

	[JsonProperty("askQty")]
	public decimal? BestAskQuantity2 { get; set; }

	[JsonProperty("bids")]
	public MiraeSharekhanDepthLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public MiraeSharekhanDepthLevel[] Asks { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	public decimal? GetLastPrice() => LastPrice ?? LastPrice2;
	public decimal? GetLastQuantity() => LastQuantity ?? LastQuantity2 ?? LastQuantity3;
	public decimal? GetVolume() => Volume ?? Volume2;
	public decimal? GetOpenInterest() => OpenInterest ?? OpenInterest2 ?? OpenInterest3;
	public decimal? GetBestBidPrice() => BestBidPrice ?? BestBidPrice2;
	public decimal? GetBestAskPrice() => BestAskPrice ?? BestAskPrice2;
	public decimal? GetBestAskQuantity() => BestAskQuantity ?? BestAskQuantity2;
	public DateTime? GetTime()
		=> Timestamp.IsEmpty(Time).IsEmpty(Timestamp2).IsEmpty(Time2).ParseIndiaTime();

	public string GetStreamKey()
	{
		if (!Token.IsEmpty())
			return Token.ToUpperInvariant();
		var exchange = Exchange.IsEmpty(Exchange2);
		if (!exchange.IsEmpty() && !ScripCode.IsEmpty())
			return exchange.ToUpperInvariant() + ScripCode;
		return null;
	}
}

internal sealed class MiraeSharekhanStreamEnvelope : MiraeSharekhanStreamMessage
{
	[JsonProperty("data")]
	public MiraeSharekhanStreamPayload Data { get; set; }

	[JsonProperty("feeds")]
	public MiraeSharekhanStreamMessage[] Feeds { get; set; }

	public MiraeSharekhanStreamMessage[] GetMessages() => Data?.Messages ?? Feeds ?? [];
}

[JsonConverter(typeof(MiraeSharekhanStreamPayloadConverter))]
internal sealed class MiraeSharekhanStreamPayload
{
	public string Text { get; init; }
	public MiraeSharekhanStreamMessage[] Messages { get; init; } = [];
}

internal sealed class MiraeSharekhanStreamPayloadConverter :
	JsonConverter<MiraeSharekhanStreamPayload>
{
	public override bool CanWrite => false;

	public override MiraeSharekhanStreamPayload ReadJson(JsonReader reader,
		Type objectType, MiraeSharekhanStreamPayload existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType == JsonToken.Null)
			return new();
		if (reader.TokenType == JsonToken.StartObject)
			return new()
			{
				Messages =
				[
					serializer.Deserialize<MiraeSharekhanStreamMessage>(reader)
				],
			};
		if (reader.TokenType == JsonToken.StartArray)
			return new()
			{
				Messages = serializer.Deserialize<MiraeSharekhanStreamMessage[]>(reader) ?? [],
			};
		if (reader.TokenType is JsonToken.String or JsonToken.Integer or
			JsonToken.Float or JsonToken.Boolean)
			return new()
			{
				Text = Convert.ToString(reader.Value, CultureInfo.InvariantCulture),
			};
		throw new JsonSerializationException(
			"Mirae Asset Sharekhan WebSocket data has an unexpected shape.");
	}

	public override void WriteJson(JsonWriter writer,
		MiraeSharekhanStreamPayload value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

internal sealed class MiraeSharekhanDepthLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity2 { get; set; }

	[JsonProperty("orders")]
	public int? Orders { get; set; }

	[JsonProperty("orderCount")]
	public int? OrderCount { get; set; }

	public decimal GetQuantity() => Quantity != 0 ? Quantity : Quantity2;
	public int? GetOrders() => Orders ?? OrderCount;
}
