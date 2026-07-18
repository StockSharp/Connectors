namespace StockSharp.Bullish.Native.Model;

sealed class BullishErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errorCode")]
	public int? ErrorCode { get; set; }

	[JsonProperty("errorCodeName")]
	public string ErrorCodeName { get; set; }
}

sealed class BullishLoginResponse
{
	[JsonProperty("authorizer")]
	public string Authorizer { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }
}

sealed class BullishMarket
{
	[JsonProperty("marketId")]
	public string MarketId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("baseSymbol")]
	public string BaseSymbol { get; set; }

	[JsonProperty("underlyingBaseSymbol")]
	public string UnderlyingBaseSymbol { get; set; }

	[JsonProperty("quoteSymbol")]
	public string QuoteSymbol { get; set; }

	[JsonProperty("underlyingQuoteSymbol")]
	public string UnderlyingQuoteSymbol { get; set; }

	[JsonProperty("settlementAssetSymbol")]
	public string SettlementAssetSymbol { get; set; }

	[JsonProperty("basePrecision")]
	public int BasePrecision { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("quantityPrecision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("minQuantityLimit")]
	public string MinimumQuantity { get; set; }

	[JsonProperty("maxQuantityLimit")]
	public string MaximumQuantity { get; set; }

	[JsonProperty("marketType")]
	public string MarketType { get; set; }

	[JsonProperty("contractMultiplier")]
	public decimal? ContractMultiplier { get; set; }

	[JsonProperty("marketEnabled")]
	public bool IsMarketEnabled { get; set; }

	[JsonProperty("createOrderEnabled")]
	public bool IsCreateOrderEnabled { get; set; }

	[JsonProperty("cancelOrderEnabled")]
	public bool IsCancelOrderEnabled { get; set; }

	[JsonProperty("expiryDatetime")]
	public string ExpiryDateTime { get; set; }

	[JsonProperty("optionStrikePrice")]
	public string OptionStrikePrice { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }
}

sealed class BullishBookLevel
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("priceLevelQuantity")]
	public string Quantity { get; set; }
}

sealed class BullishOrderBook
{
	[JsonProperty("bids")]
	public BullishBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BullishBookLevel[] Asks { get; set; }

	[JsonProperty("datetime")]
	public string DateTime { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("sequenceNumber")]
	public long SequenceNumber { get; set; }
}

sealed class BullishTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("tradingAccountId")]
	public string TradingAccountId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("quoteAmount")]
	public string QuoteAmount { get; set; }

	[JsonProperty("baseFee")]
	public string BaseFee { get; set; }

	[JsonProperty("quoteFee")]
	public string QuoteFee { get; set; }

	[JsonProperty("tradeRebateAmount")]
	public string RebateAmount { get; set; }

	[JsonProperty("tradeRebateAssetSymbol")]
	public string RebateAssetSymbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("isTaker")]
	public bool IsTaker { get; set; }

	[JsonProperty("createdAtDatetime")]
	public string CreatedAtDateTime { get; set; }

	[JsonProperty("createdAtTimestamp")]
	public string CreatedAtTimestamp { get; set; }

	[JsonProperty("publishedAtTimestamp")]
	public string PublishedAtTimestamp { get; set; }

	[JsonProperty("auctionId")]
	public string AuctionId { get; set; }
}

sealed class BullishTick
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("createdAtDatetime")]
	public string CreatedAtDateTime { get; set; }

	[JsonProperty("createdAtTimestamp")]
	public string CreatedAtTimestamp { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("bestBid")]
	public string BestBid { get; set; }

	[JsonProperty("bidVolume")]
	public string BidVolume { get; set; }

	[JsonProperty("bestAsk")]
	public string BestAsk { get; set; }

	[JsonProperty("askVolume")]
	public string AskVolume { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("last")]
	public string Last { get; set; }

	[JsonProperty("baseVolume")]
	public string BaseVolume { get; set; }

	[JsonProperty("quoteVolume")]
	public string QuoteVolume { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("fundingRate")]
	public string FundingRate { get; set; }

	[JsonProperty("openInterest")]
	public string OpenInterest { get; set; }

	[JsonProperty("lastTradeDatetime")]
	public string LastTradeDateTime { get; set; }

	[JsonProperty("lastTradeTimestamp")]
	public string LastTradeTimestamp { get; set; }

	[JsonProperty("lastTradeQuantity")]
	public string LastTradeQuantity { get; set; }

	[JsonProperty("publishedAtTimestamp")]
	public string PublishedAtTimestamp { get; set; }
}

sealed class BullishCandle
{
	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }

	[JsonProperty("createdAtTimestamp")]
	public string CreatedAtTimestamp { get; set; }

	[JsonProperty("createdAtDatetime")]
	public string CreatedAtDateTime { get; set; }

	[JsonProperty("publishedAtTimestamp")]
	public string PublishedAtTimestamp { get; set; }
}

sealed class BullishTradingAccount
{
	[JsonProperty("tradingAccountId")]
	public string TradingAccountId { get; set; }

	[JsonProperty("tradingAccountName")]
	public string Name { get; set; }

	[JsonProperty("tradingAccountDescription")]
	public string Description { get; set; }

	[JsonProperty("isPrimaryAccount")]
	public bool IsPrimaryAccount { get; set; }

	[JsonProperty("rateLimitToken")]
	public string RateLimitToken { get; set; }

	[JsonProperty("referenceAssetSymbol")]
	public string ReferenceAssetSymbol { get; set; }

	[JsonProperty("totalLiabilitiesUSD")]
	public string TotalLiabilitiesUsd { get; set; }

	[JsonProperty("totalBorrowedUSD")]
	public string TotalBorrowedUsd { get; set; }

	[JsonProperty("totalCollateralUSD")]
	public string TotalCollateralUsd { get; set; }

	[JsonProperty("initialMarginUSD")]
	public string InitialMarginUsd { get; set; }

	[JsonProperty("warningMarginUSD")]
	public string WarningMarginUsd { get; set; }

	[JsonProperty("liquidationMarginUSD")]
	public string LiquidationMarginUsd { get; set; }

	[JsonProperty("fullLiquidationMarginUSD")]
	public string FullLiquidationMarginUsd { get; set; }

	[JsonProperty("updatedAtTimestamp")]
	public string UpdatedAtTimestamp { get; set; }

	[JsonProperty("publishedAtTimestamp")]
	public string PublishedAtTimestamp { get; set; }
}

sealed class BullishAssetAccount
{
	[JsonProperty("tradingAccountId")]
	public string TradingAccountId { get; set; }

	[JsonProperty("assetId")]
	public string AssetId { get; set; }

	[JsonProperty("assetSymbol")]
	public string AssetSymbol { get; set; }

	[JsonProperty("availableQuantity")]
	public string AvailableQuantity { get; set; }

	[JsonProperty("borrowedQuantity")]
	public string BorrowedQuantity { get; set; }

	[JsonProperty("lockedQuantity")]
	public string LockedQuantity { get; set; }

	[JsonProperty("loanedQuantity")]
	public string LoanedQuantity { get; set; }

	[JsonProperty("updatedAtDatetime")]
	public string UpdatedAtDateTime { get; set; }

	[JsonProperty("updatedAtTimestamp")]
	public string UpdatedAtTimestamp { get; set; }

	[JsonProperty("publishedAtTimestamp")]
	public string PublishedAtTimestamp { get; set; }
}

sealed class BullishDerivativePosition
{
	[JsonProperty("tradingAccountId")]
	public string TradingAccountId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("notional")]
	public string Notional { get; set; }

	[JsonProperty("entryNotional")]
	public string EntryNotional { get; set; }

	[JsonProperty("mtmPnl")]
	public string MarkToMarketPnl { get; set; }

	[JsonProperty("reportedMtmPnl")]
	public string ReportedMarkToMarketPnl { get; set; }

	[JsonProperty("reportedFundingPnl")]
	public string ReportedFundingPnl { get; set; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnl { get; set; }

	[JsonProperty("settlementAssetSymbol")]
	public string SettlementAssetSymbol { get; set; }

	[JsonProperty("createdAtDatetime")]
	public string CreatedAtDateTime { get; set; }

	[JsonProperty("createdAtTimestamp")]
	public string CreatedAtTimestamp { get; set; }

	[JsonProperty("updatedAtDatetime")]
	public string UpdatedAtDateTime { get; set; }

	[JsonProperty("updatedAtTimestamp")]
	public string UpdatedAtTimestamp { get; set; }

	[JsonProperty("publishedAtTimestamp")]
	public string PublishedAtTimestamp { get; set; }
}

sealed class BullishOrder
{
	[JsonProperty("tradingAccountId")]
	public string TradingAccountId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("averageFillPrice")]
	public string AverageFillPrice { get; set; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; set; }

	[JsonProperty("allowBorrow")]
	public bool IsBorrowAllowed { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("quantityFilled")]
	public string FilledQuantity { get; set; }

	[JsonProperty("quoteAmount")]
	public string QuoteAmount { get; set; }

	[JsonProperty("baseFee")]
	public string BaseFee { get; set; }

	[JsonProperty("quoteFee")]
	public string QuoteFee { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("statusReason")]
	public string StatusReason { get; set; }

	[JsonProperty("statusReasonCode")]
	public string StatusReasonCode { get; set; }

	[JsonProperty("createdAtDatetime")]
	public string CreatedAtDateTime { get; set; }

	[JsonProperty("createdAtTimestamp")]
	public string CreatedAtTimestamp { get; set; }

	[JsonProperty("publishedAtTimestamp")]
	public string PublishedAtTimestamp { get; set; }
}

abstract class BullishSignedCommand
{
	[JsonProperty("commandType", Order = 0)]
	public string CommandType { get; init; }
}

sealed class BullishCreateOrderRequest : BullishSignedCommand
{
	[JsonProperty("symbol", Order = 1)]
	public string Symbol { get; init; }

	[JsonProperty("type", Order = 2)]
	public string Type { get; init; }

	[JsonProperty("side", Order = 3)]
	public string Side { get; init; }

	[JsonProperty("quantity", Order = 4)]
	public string Quantity { get; init; }

	[JsonProperty("price", Order = 5)]
	public string Price { get; init; }

	[JsonProperty("stopPrice", Order = 6)]
	public string StopPrice { get; init; }

	[JsonProperty("timeInForce", Order = 7)]
	public string TimeInForce { get; init; }

	[JsonProperty("allowBorrow", Order = 8)]
	public bool IsBorrowAllowed { get; init; }

	[JsonProperty("isMMP", Order = 9)]
	public bool IsMarketMakerProtection { get; init; }

	[JsonProperty("clientOrderId", Order = 10)]
	public string ClientOrderId { get; init; }

	[JsonProperty("tradingAccountId", Order = 11)]
	public string TradingAccountId { get; init; }
}

sealed class BullishAmendOrderRequest : BullishSignedCommand
{
	[JsonProperty("orderId", Order = 1)]
	public string OrderId { get; init; }

	[JsonProperty("symbol", Order = 2)]
	public string Symbol { get; init; }

	[JsonProperty("type", Order = 3)]
	public string Type { get; init; }

	[JsonProperty("price", Order = 4)]
	public string Price { get; init; }

	[JsonProperty("clientOrderId", Order = 5)]
	public string ClientOrderId { get; init; }

	[JsonProperty("quantity", Order = 6)]
	public string Quantity { get; init; }

	[JsonProperty("tradingAccountId", Order = 7)]
	public string TradingAccountId { get; init; }
}

sealed class BullishCancelOrderRequest : BullishSignedCommand
{
	[JsonProperty("orderId", Order = 1)]
	public string OrderId { get; init; }

	[JsonProperty("clientOrderId", Order = 2)]
	public string ClientOrderId { get; init; }

	[JsonProperty("symbol", Order = 3)]
	public string Symbol { get; init; }

	[JsonProperty("tradingAccountId", Order = 4)]
	public string TradingAccountId { get; init; }
}

sealed class BullishCancelAllOrdersRequest : BullishSignedCommand
{
	[JsonProperty("tradingAccountId", Order = 1)]
	public string TradingAccountId { get; init; }
}

sealed class BullishCancelAllByMarketRequest : BullishSignedCommand
{
	[JsonProperty("symbol", Order = 1)]
	public string Symbol { get; init; }

	[JsonProperty("tradingAccountId", Order = 2)]
	public string TradingAccountId { get; init; }
}

sealed class BullishCommandResponse
{
	[JsonProperty("commandType")]
	public string CommandType { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }
}

enum BullishWsKinds
{
	OrderBook,
	Trades,
	Tick,
	Private,
}

readonly record struct BullishWsChannel(string Topic, string Symbol, string TradingAccountId);

abstract class BullishWsParameters
{
}

sealed class BullishWsMarketParameters : BullishWsParameters
{
	[JsonProperty("topic")]
	public string Topic { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class BullishWsPrivateParameters : BullishWsParameters
{
	[JsonProperty("topic")]
	public string Topic { get; init; }

	[JsonProperty("tradingAccountId")]
	public string TradingAccountId { get; init; }
}

sealed class BullishWsEmptyParameters : BullishWsParameters
{
}

sealed class BullishWsCommand
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";

	[JsonProperty("type")]
	public string Type { get; init; } = "command";

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public BullishWsParameters Parameters { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }
}

sealed class BullishWsError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("errorCodeName")]
	public string ErrorCodeName { get; set; }
}

sealed class BullishWsHeader
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("dataType")]
	public string DataType { get; set; }

	[JsonProperty("error")]
	public BullishWsError Error { get; set; }
}

sealed class BullishWsDataMessage<TData>
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("dataType")]
	public string DataType { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class BullishWsTradesData
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("createdAtDatetime")]
	public string CreatedAtDateTime { get; set; }

	[JsonProperty("publishedAtTimestamp")]
	public string PublishedAtTimestamp { get; set; }

	[JsonProperty("trades")]
	public BullishTrade[] Trades { get; set; }
}

readonly record struct BullishSequenceRange(long First, long Last);

sealed class BullishWsLevel2Data
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bids")]
	[JsonConverter(typeof(BullishFlatBookLevelsConverter))]
	public BullishBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	[JsonConverter(typeof(BullishFlatBookLevelsConverter))]
	public BullishBookLevel[] Asks { get; set; }

	[JsonProperty("sequenceNumberRange")]
	[JsonConverter(typeof(BullishSequenceRangeConverter))]
	public BullishSequenceRange SequenceRange { get; set; }

	[JsonProperty("datetime")]
	public string DateTime { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("publishedAtTimestamp")]
	public string PublishedAtTimestamp { get; set; }
}

sealed class BullishFlatBookLevelsConverter : JsonConverter<BullishBookLevel[]>
{
	public override BullishBookLevel[] ReadJson(JsonReader reader, Type objectType,
		BullishBookLevel[] existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return [];
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Bullish order-book levels must be an array.");

		var levels = new List<BullishBookLevel>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
		{
			if (reader.TokenType == JsonToken.StartArray)
			{
				var price = BullishJson.ReadNextScalar(reader, "order-book price");
				var quantity = BullishJson.ReadNextScalar(reader, "order-book quantity");
				if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
					throw new JsonSerializationException("Bullish nested order-book level is malformed.");
				levels.Add(new() { Price = price, Quantity = quantity });
				continue;
			}

			var flatPrice = BullishJson.ReadCurrentScalar(reader, "order-book price");
			if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
				throw new JsonSerializationException("Bullish flat order-book level has no quantity.");
			levels.Add(new()
			{
				Price = flatPrice,
				Quantity = BullishJson.ReadCurrentScalar(reader, "order-book quantity"),
			});
		}
		return [.. levels];
	}

	public override void WriteJson(JsonWriter writer, BullishBookLevel[] value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class BullishSequenceRangeConverter : JsonConverter<BullishSequenceRange>
{
	public override BullishSequenceRange ReadJson(JsonReader reader, Type objectType,
		BullishSequenceRange existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Bullish sequence range must be an array.");
		var first = BullishJson.ReadNextInt64(reader, "sequence start");
		var last = BullishJson.ReadNextInt64(reader, "sequence end");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Bullish sequence range is malformed.");
		return new(first, last);
	}

	public override void WriteJson(JsonWriter writer, BullishSequenceRange value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

static class BullishJson
{
	public static string ReadNextScalar(JsonReader reader, string field)
	{
		if (!reader.Read())
			throw new JsonSerializationException($"Bullish {field} is missing.");
		return ReadCurrentScalar(reader, field);
	}

	public static string ReadCurrentScalar(JsonReader reader, string field)
	{
		if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or JsonToken.Float))
			throw new JsonSerializationException($"Bullish {field} must be numeric or a string.");
		return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
	}

	public static long ReadNextInt64(JsonReader reader, string field)
	{
		var value = ReadNextScalar(reader, field);
		return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
			? number
			: throw new JsonSerializationException($"Bullish {field} is not an Int64 value.");
	}
}
