namespace StockSharp.Fxcm.Native;

internal static class FxcmModelNames
{
	public const string Offer = nameof(Offer);
	public const string OpenPosition = nameof(OpenPosition);
	public const string ClosedPosition = nameof(ClosedPosition);
	public const string Order = nameof(Order);
	public const string Account = nameof(Account);
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class FxcmFormFieldAttribute(string name) : Attribute
{
	public string Name { get; } = name;
}

internal sealed class FxcmApiResponse
{
	[JsonProperty("executed")]
	public bool IsExecuted { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}

internal sealed class FxcmApiEnvelope<T>
{
	[JsonProperty("response")]
	public FxcmApiResponse Response { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }
}

internal sealed class FxcmEmptyData
{
}

internal sealed class FxcmErrorDetails
{
	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}

internal sealed class FxcmInstrumentsData
{
	[JsonProperty("instrument")]
	public FxcmInstrument[] Instruments { get; set; }
}

internal sealed class FxcmInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("visible")]
	public bool IsVisible { get; set; }

	[JsonProperty("order")]
	public int Order { get; set; }
}

internal sealed class FxcmModelsData
{
	[JsonProperty("offers")]
	public FxcmOffer[] Offers { get; set; }

	[JsonProperty("open_positions")]
	public FxcmPosition[] OpenPositions { get; set; }

	[JsonProperty("closed_positions")]
	public FxcmPosition[] ClosedPositions { get; set; }

	[JsonProperty("orders")]
	public FxcmOrder[] Orders { get; set; }

	[JsonProperty("accounts")]
	public FxcmAccount[] Accounts { get; set; }
}

internal abstract class FxcmTableRow
{
	[JsonProperty("t")]
	public int TableId { get; set; }

	[JsonProperty("isTotal")]
	public bool? IsTotal { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }
}

internal sealed class FxcmOffer : FxcmTableRow
{
	[JsonProperty("offerId")]
	public long OfferId { get; set; }

	[JsonProperty("currency")]
	public string Symbol { get; set; }

	[JsonProperty("instrumentType")]
	public int InstrumentType { get; set; }

	[JsonProperty("ratePrecision")]
	public int RatePrecision { get; set; }

	[JsonProperty("pip")]
	public decimal? Pip { get; set; }

	[JsonProperty("time")]
	public DateTime? Time { get; set; }

	[JsonProperty("buy")]
	public decimal? Ask { get; set; }

	[JsonProperty("sell")]
	public decimal? Bid { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("buyTradable")]
	public bool IsBuyTradable { get; set; }

	[JsonProperty("sellTradable")]
	public bool IsSellTradable { get; set; }
}

internal sealed class FxcmOrder : FxcmTableRow
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("tradeId")]
	public long? TradeId { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("accountName")]
	public string AccountName { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("expireDate")]
	public string ExpireDate { get; set; }

	[JsonProperty("currency")]
	public string Symbol { get; set; }

	[JsonProperty("isBuy")]
	public bool? IsBuy { get; set; }

	[JsonProperty("buy")]
	public decimal? Buy { get; set; }

	[JsonProperty("sell")]
	public decimal? Sell { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("amountK")]
	public decimal? Amount { get; set; }

	[JsonProperty("stop")]
	public decimal? Stop { get; set; }

	[JsonProperty("limit")]
	public decimal? Limit { get; set; }

	[JsonProperty("isEntryOrder")]
	public bool IsEntryOrder { get; set; }

	[JsonProperty("range")]
	public decimal? Range { get; set; }

	[JsonProperty("trailingStep")]
	public decimal? TrailingStep { get; set; }
}

internal sealed class FxcmPosition : FxcmTableRow
{
	[JsonProperty("tradeId")]
	public long? TradeId { get; set; }

	[JsonProperty("accountName")]
	public string AccountName { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("open")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("close")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("grossPL")]
	public decimal? GrossPnL { get; set; }

	[JsonProperty("visiblePL")]
	public decimal? VisiblePnL { get; set; }

	[JsonProperty("currency")]
	public string Symbol { get; set; }

	[JsonProperty("isBuy")]
	public bool IsBuy { get; set; }

	[JsonProperty("amountK")]
	public decimal? Amount { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("usedMargin")]
	public decimal? UsedMargin { get; set; }

	[JsonProperty("roll")]
	public decimal? Rollover { get; set; }

	[JsonProperty("com")]
	public decimal? Commission { get; set; }

	[JsonProperty("stop")]
	public decimal? Stop { get; set; }

	[JsonProperty("limit")]
	public decimal? Limit { get; set; }
}

internal sealed class FxcmAccount : FxcmTableRow
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("accountName")]
	public string AccountName { get; set; }

	[JsonProperty("balance")]
	public decimal? Balance { get; set; }

	[JsonProperty("equity")]
	public decimal? Equity { get; set; }

	[JsonProperty("usableMargin")]
	public decimal? UsableMargin { get; set; }

	[JsonProperty("dayPL")]
	public decimal? DayPnL { get; set; }

	[JsonProperty("grossPL")]
	public decimal? GrossPnL { get; set; }

	[JsonProperty("hedging")]
	public string Hedging { get; set; }

	[JsonProperty("mc")]
	public string MarginCall { get; set; }
}

internal sealed class FxcmOrderResult
{
	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }
}

internal sealed class FxcmCandlesResponse
{
	[JsonProperty("response")]
	public FxcmApiResponse Response { get; set; }

	[JsonProperty("candles")]
	public FxcmCandle[] Candles { get; set; }
}

[JsonConverter(typeof(FxcmCandleConverter))]
internal sealed class FxcmCandle
{
	public long Timestamp { get; set; }
	public decimal BidOpen { get; set; }
	public decimal BidClose { get; set; }
	public decimal BidHigh { get; set; }
	public decimal BidLow { get; set; }
	public decimal AskOpen { get; set; }
	public decimal AskClose { get; set; }
	public decimal AskHigh { get; set; }
	public decimal AskLow { get; set; }
	public decimal TickQuantity { get; set; }
}

internal sealed class FxcmCandleConverter : JsonConverter<FxcmCandle>
{
	public override FxcmCandle ReadJson(JsonReader reader, Type objectType, FxcmCandle existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("FXCM candle must be a JSON array.");

		var candle = new FxcmCandle
		{
			Timestamp = Read<long>(reader, serializer),
			BidOpen = Read<decimal>(reader, serializer),
			BidClose = Read<decimal>(reader, serializer),
			BidHigh = Read<decimal>(reader, serializer),
			BidLow = Read<decimal>(reader, serializer),
			AskOpen = Read<decimal>(reader, serializer),
			AskClose = Read<decimal>(reader, serializer),
			AskHigh = Read<decimal>(reader, serializer),
			AskLow = Read<decimal>(reader, serializer),
			TickQuantity = Read<decimal>(reader, serializer),
		};

		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("FXCM candle has an invalid number of fields.");
		return candle;
	}

	private static T Read<T>(JsonReader reader, JsonSerializer serializer)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException("FXCM candle is incomplete.");
		return serializer.Deserialize<T>(reader);
	}

	public override void WriteJson(JsonWriter writer, FxcmCandle value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

internal sealed class FxcmSocketOpen
{
	[JsonProperty("sid")]
	public string SessionId { get; set; }

	[JsonProperty("upgrades")]
	public string[] Upgrades { get; set; }

	[JsonProperty("pingInterval")]
	public int PingInterval { get; set; }

	[JsonProperty("pingTimeout")]
	public int PingTimeout { get; set; }
}

internal sealed class FxcmPriceUpdate
{
	[JsonProperty("Updated")]
	public long Updated { get; set; }

	[JsonProperty("Symbol")]
	public string Symbol { get; set; }

	[JsonProperty("Rates")]
	public decimal[] Rates { get; set; }
}

internal sealed class FxcmPositionUpdate
{
	public FxcmPosition Position { get; init; }
	public bool IsClosed { get; init; }
}

internal sealed class FxcmModelRequest
{
	[FxcmFormField("models")]
	public string Models { get; set; }
}

internal sealed class FxcmPairsRequest
{
	[FxcmFormField("pairs")]
	public string Pairs { get; set; }
}

internal sealed class FxcmCandlesRequest
{
	[FxcmFormField("num")]
	public int Count { get; set; }

	[FxcmFormField("from")]
	public long? From { get; set; }

	[FxcmFormField("to")]
	public long? To { get; set; }
}

internal sealed class FxcmOpenTradeRequest
{
	[FxcmFormField("account_id")]
	public string AccountId { get; set; }

	[FxcmFormField("symbol")]
	public string Symbol { get; set; }

	[FxcmFormField("is_buy")]
	public bool IsBuy { get; set; }

	[FxcmFormField("amount")]
	public decimal Amount { get; set; }

	[FxcmFormField("stop")]
	public decimal? Stop { get; set; }

	[FxcmFormField("trailing_step")]
	public decimal? TrailingStep { get; set; }

	[FxcmFormField("limit")]
	public decimal? Limit { get; set; }

	[FxcmFormField("is_in_pips")]
	public bool IsInPips { get; set; }

	[FxcmFormField("at_market")]
	public decimal? AtMarket { get; set; }

	[FxcmFormField("order_type")]
	public string OrderType { get; set; }

	[FxcmFormField("time_in_force")]
	public string TimeInForce { get; set; }
}

internal sealed class FxcmEntryOrderRequest
{
	[FxcmFormField("account_id")]
	public string AccountId { get; set; }

	[FxcmFormField("symbol")]
	public string Symbol { get; set; }

	[FxcmFormField("is_buy")]
	public bool IsBuy { get; set; }

	[FxcmFormField("rate")]
	public decimal Rate { get; set; }

	[FxcmFormField("amount")]
	public decimal Amount { get; set; }

	[FxcmFormField("stop")]
	public decimal? Stop { get; set; }

	[FxcmFormField("trailing_step")]
	public decimal? TrailingStep { get; set; }

	[FxcmFormField("limit")]
	public decimal? Limit { get; set; }

	[FxcmFormField("is_in_pips")]
	public bool IsInPips { get; set; }

	[FxcmFormField("range")]
	public decimal? Range { get; set; }

	[FxcmFormField("order_type")]
	public string OrderType { get; set; }

	[FxcmFormField("time_in_force")]
	public string TimeInForce { get; set; }

	[FxcmFormField("expiration")]
	public string Expiration { get; set; }
}

internal sealed class FxcmChangeOrderRequest
{
	[FxcmFormField("order_id")]
	public long OrderId { get; set; }

	[FxcmFormField("rate")]
	public decimal? Rate { get; set; }

	[FxcmFormField("range")]
	public decimal? Range { get; set; }

	[FxcmFormField("amount")]
	public decimal? Amount { get; set; }

	[FxcmFormField("trailing_step")]
	public decimal? TrailingStep { get; set; }
}

internal sealed class FxcmDeleteOrderRequest
{
	[FxcmFormField("order_id")]
	public long OrderId { get; set; }
}

internal sealed class FxcmCloseTradeRequest
{
	[FxcmFormField("trade_id")]
	public long TradeId { get; set; }

	[FxcmFormField("amount")]
	public decimal Amount { get; set; }

	[FxcmFormField("rate")]
	public decimal? Rate { get; set; }

	[FxcmFormField("at_market")]
	public decimal? AtMarket { get; set; }

	[FxcmFormField("order_type")]
	public string OrderType { get; set; }

	[FxcmFormField("time_in_force")]
	public string TimeInForce { get; set; }
}
