namespace StockSharp.BTSE.Native.Model;

sealed class BTSEWsCommand
{
	[JsonProperty("op")]
	public BTSEWsOperations Operation { get; init; }

	[JsonProperty("args")]
	public string[] Arguments { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTSEWsOperations
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "authKeyExpires")]
	Authenticate,
}

sealed class BTSEWsHeader
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("msg")]
	public string ShortMessage { get; set; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("channel")]
	public string[] Channels { get; set; }
}

sealed class BTSEWsEnvelope<TData>
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

[JsonConverter(typeof(BTSEPriceLevelConverter))]
sealed class BTSEPriceLevel
{
	public decimal Price { get; set; }
	public decimal Size { get; set; }
}

sealed class BTSEWsBook
{
	[JsonProperty("bids")]
	public BTSEPriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BTSEPriceLevel[] Asks { get; set; }

	[JsonProperty("seqNum")]
	public long SequenceNumber { get; set; }

	[JsonProperty("prevSeqNum")]
	public long PreviousSequenceNumber { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class BTSEWsOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderID")]
	public string OrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("type")]
	public int? Type { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("status")]
	public int? Status { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("avgFilledPrice")]
	public decimal? AverageFilledPrice { get; set; }

	[JsonProperty("clOrderID")]
	public string ClientOrderId { get; set; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; set; }

	[JsonProperty("maker")]
	public bool? IsMaker { get; set; }

	[JsonProperty("originalOrderBaseSize")]
	public decimal? OriginalOrderBaseSize { get; set; }

	[JsonProperty("originalOrderQuoteSize")]
	public decimal? OriginalOrderQuoteSize { get; set; }

	[JsonProperty("currentOrderBaseSize")]
	public decimal? CurrentOrderBaseSize { get; set; }

	[JsonProperty("currentOrderQuoteSize")]
	public decimal? CurrentOrderQuoteSize { get; set; }

	[JsonProperty("filledBaseSize")]
	public decimal? FilledBaseSize { get; set; }

	[JsonProperty("totalFilledBaseSize")]
	public decimal? TotalFilledBaseSize { get; set; }

	[JsonProperty("remainingOrderBaseSize")]
	public decimal? RemainingOrderBaseSize { get; set; }

	[JsonProperty("remainingOrderQuoteSize")]
	public decimal? RemainingOrderQuoteSize { get; set; }

	[JsonProperty("remainingBaseSize")]
	public decimal? RemainingBaseSize { get; set; }

	[JsonProperty("remainingQuoteSize")]
	public decimal? RemainingQuoteSize { get; set; }

	[JsonProperty("orderCurrency")]
	public string OrderCurrency { get; set; }

	[JsonProperty("originalOrderSize")]
	public decimal? OriginalOrderSize { get; set; }

	[JsonProperty("currentOrderSize")]
	public decimal? CurrentOrderSize { get; set; }

	[JsonProperty("filledSize")]
	public decimal? FilledSize { get; set; }

	[JsonProperty("totalFilledSize")]
	public decimal? TotalFilledSize { get; set; }

	[JsonProperty("remainingSize")]
	public decimal? RemainingSize { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }
}

sealed class BTSEWsFill
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("serialId")]
	public string SerialId { get; set; }

	[JsonProperty("clOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("type")]
	public int OrderType { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("feeAmount")]
	public decimal FeeAmount { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("maker")]
	public bool IsMaker { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("contractSize")]
	public decimal? ContractSize { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }
}

sealed class BTSEWsPosition
{
	[JsonProperty("marketName")]
	public string MarketName { get; set; }

	[JsonProperty("entryPrice")]
	public decimal EntryPrice { get; set; }

	[JsonProperty("liquidationPrice")]
	public decimal? LiquidationPrice { get; set; }

	[JsonProperty("markedPrice")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("unrealizedProfitLoss")]
	public decimal UnrealizedPnL { get; set; }

	[JsonProperty("totalContracts")]
	public decimal TotalContracts { get; set; }

	[JsonProperty("orderModeName")]
	public string OrderModeName { get; set; }

	[JsonProperty("currentLeverage")]
	public decimal? CurrentLeverage { get; set; }

	[JsonProperty("contractSize")]
	public decimal? ContractSize { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("positionDirection")]
	public string PositionDirection { get; set; }

	[JsonProperty("avgFilledPrice")]
	public decimal? AverageFilledPrice { get; set; }
}

sealed class BTSEPriceLevelConverter : JsonConverter<BTSEPriceLevel>
{
	public override BTSEPriceLevel ReadJson(JsonReader reader, Type objectType,
		BTSEPriceLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("BTSE price level must be an array.");
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException("BTSE price level has no price.");
		var price = Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException("BTSE price level has no size.");
		var size = Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("BTSE price level has unexpected fields.");
		return new() { Price = price, Size = size };
	}

	public override void WriteJson(JsonWriter writer, BTSEPriceLevel value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Price);
		writer.WriteValue(value.Size);
		writer.WriteEndArray();
	}
}
