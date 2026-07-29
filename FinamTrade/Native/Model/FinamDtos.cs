namespace StockSharp.FinamTrade.Native.Model;

sealed class FinamAuthRequest
{
	public string Secret { get; set; }
	public string SourceAppId { get; set; }
}

sealed class FinamAuthResponse
{
	public string Token { get; set; }
}

sealed class FinamTokenDetailsRequest
{
	public string Token { get; set; }
}

sealed class FinamTokenDetails
{
	public DateTime? CreatedAt { get; set; }
	public DateTime? ExpiresAt { get; set; }
	public string[] AccountIds { get; set; }
	public bool Readonly { get; set; }
}

sealed class FinamError
{
	public int Code { get; set; }
	public string Message { get; set; }
	public JToken Details { get; set; }
}

[JsonConverter(typeof(FinamDecimalConverter))]
sealed class FinamDecimal
{
	public string Value { get; set; }
}

sealed class FinamDecimalConverter : JsonConverter<FinamDecimal>
{
	public override FinamDecimal ReadJson(JsonReader reader,
		Type objectType, FinamDecimal existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;

		if (reader.TokenType == JsonToken.StartObject)
			return new()
			{
				Value = JObject.Load(reader).Value<string>("value"),
			};

		if (reader.TokenType is JsonToken.String or
			JsonToken.Integer or JsonToken.Float)
			return new()
			{
				Value = Convert.ToString(reader.Value,
					CultureInfo.InvariantCulture),
			};

		throw new JsonSerializationException(
			$"Unsupported Finam decimal token '{reader.TokenType}'.");
	}

	public override void WriteJson(JsonWriter writer,
		FinamDecimal value, JsonSerializer serializer)
	{
		if (value is null)
		{
			writer.WriteNull();
			return;
		}

		writer.WriteStartObject();
		writer.WritePropertyName("value");
		writer.WriteValue(value.Value);
		writer.WriteEndObject();
	}
}

sealed class FinamMoney
{
	[JsonProperty("currency_code")]
	public string CurrencyCode { get; set; }

	[JsonProperty("currencyCode")]
	private string SocketCurrencyCode { set => CurrencyCode = value; }

	public string Units { get; set; }
	public int Nanos { get; set; }
}

sealed class FinamAssetPage
{
	public FinamAsset[] Assets { get; set; }
	public string NextCursor { get; set; }
}

sealed class FinamAsset
{
	public string Symbol { get; set; }
	public string Id { get; set; }
	public string Ticker { get; set; }
	public string Mic { get; set; }
	public string Isin { get; set; }
	public string Type { get; set; }
	public string Name { get; set; }
	public bool IsArchived { get; set; }
}

sealed class FinamAssetDetails
{
	public string Board { get; set; }
	public string Id { get; set; }
	public string Ticker { get; set; }
	public string Mic { get; set; }
	public string Isin { get; set; }
	public string Type { get; set; }
	public string Name { get; set; }
	public int? Decimals { get; set; }
	public long? MinStep { get; set; }
	public FinamDecimal LotSize { get; set; }
	public string QuoteCurrency { get; set; }
	public FinamFutureDetails FutureDetails { get; set; }
	public FinamOptionDetails OptionDetails { get; set; }
	public FinamBondDetails BondDetails { get; set; }
}

sealed class FinamFutureDetails
{
	public DateTime? ExpirationDate { get; set; }
	public FinamDecimal ContractSize { get; set; }
}

sealed class FinamOptionDetails
{
	public DateTime? ExpirationDate { get; set; }
	public FinamDecimal ContractSize { get; set; }
	public FinamDecimal Strike { get; set; }
}

sealed class FinamBondDetails
{
	public FinamDecimal BondFaceValue { get; set; }
	public string Currency { get; set; }
}

sealed class FinamQuoteResponse
{
	public string Symbol { get; set; }
	public FinamQuote Quote { get; set; }
}

sealed class FinamQuotePayload
{
	public FinamQuote[] Quote { get; set; }
}

sealed class FinamQuote
{
	public string Symbol { get; set; }
	public DateTime Timestamp { get; set; }
	public FinamDecimal Ask { get; set; }
	public FinamDecimal AskSize { get; set; }
	public FinamDecimal Bid { get; set; }
	public FinamDecimal BidSize { get; set; }
	public FinamDecimal Last { get; set; }
	public FinamDecimal LastSize { get; set; }
	public FinamDecimal Volume { get; set; }
	public FinamDecimal Turnover { get; set; }
	public FinamDecimal Open { get; set; }
	public FinamDecimal High { get; set; }
	public FinamDecimal Low { get; set; }
	public FinamDecimal Close { get; set; }
	public FinamDecimal Change { get; set; }
	public FinamDecimal OpenInterest { get; set; }
}

sealed class FinamBarsResponse
{
	public string Symbol { get; set; }
	public FinamBar[] Bars { get; set; }
}

sealed class FinamBar
{
	public DateTime Timestamp { get; set; }
	public FinamDecimal Open { get; set; }
	public FinamDecimal High { get; set; }
	public FinamDecimal Low { get; set; }
	public FinamDecimal Close { get; set; }
	public FinamDecimal Volume { get; set; }
}

sealed class FinamOrderBookResponse
{
	public string Symbol { get; set; }
	public FinamOrderBook Orderbook { get; set; }
}

sealed class FinamOrderBookPayload
{
	public FinamStreamOrderBook[] OrderBook { get; set; }
}

sealed class FinamOrderBook
{
	public FinamBookRow[] Rows { get; set; }
}

sealed class FinamStreamOrderBook
{
	public string Symbol { get; set; }
	public FinamBookRow[] Rows { get; set; }
}

sealed class FinamBookRow
{
	public FinamDecimal Price { get; set; }
	public FinamDecimal SellSize { get; set; }
	public FinamDecimal BuySize { get; set; }
	public string Action { get; set; }
	public string Mpid { get; set; }
	public DateTime Timestamp { get; set; }
}

sealed class FinamMarketTradesResponse
{
	public string Symbol { get; set; }
	public FinamMarketTrade[] Trades { get; set; }
}

sealed class FinamMarketTrade
{
	public string TradeId { get; set; }
	public string Mpid { get; set; }
	public DateTime Timestamp { get; set; }
	public FinamDecimal Price { get; set; }
	public FinamDecimal Size { get; set; }
	public string Side { get; set; }
	public FinamDecimal OpenInterest { get; set; }
}

sealed class FinamAccount
{
	public string AccountId { get; set; }
	public string Type { get; set; }
	public string Status { get; set; }
	public FinamDecimal Equity { get; set; }
	public FinamDecimal UnrealizedProfit { get; set; }
	public FinamPosition[] Positions { get; set; }
	public FinamMoney[] Cash { get; set; }
}

sealed class FinamPosition
{
	public string Symbol { get; set; }
	public FinamDecimal Quantity { get; set; }
	public FinamDecimal AveragePrice { get; set; }
	public FinamDecimal CurrentPrice { get; set; }
	public FinamDecimal MaintenanceMargin { get; set; }
	public FinamDecimal DailyPnl { get; set; }
	public FinamDecimal UnrealizedPnl { get; set; }
}

sealed class FinamOrdersResponse
{
	public FinamOrderState[] Orders { get; set; }
}

sealed class FinamOrderRequest
{
	public string Symbol { get; set; }
	public FinamDecimal Quantity { get; set; }
	public string Side { get; set; }
	public string Type { get; set; }
	public string TimeInForce { get; set; }
	public FinamDecimal LimitPrice { get; set; }
	public FinamDecimal StopPrice { get; set; }
	public string StopCondition { get; set; }
	public string ClientOrderId { get; set; }
	public string Comment { get; set; }
}

sealed class FinamOrderState
{
	public string OrderId { get; set; }
	public string ExecId { get; set; }
	public string Status { get; set; }
	public FinamOrder Order { get; set; }
	public DateTime? TransactAt { get; set; }
	public DateTime? AcceptAt { get; set; }
	public DateTime? WithdrawAt { get; set; }
	public FinamDecimal InitialQuantity { get; set; }
	public FinamDecimal ExecutedQuantity { get; set; }
	public FinamDecimal RemainingQuantity { get; set; }
	public string TriggeredOrderId { get; set; }
}

sealed class FinamOrder
{
	public string AccountId { get; set; }
	public string Symbol { get; set; }
	public FinamDecimal Quantity { get; set; }
	public string Side { get; set; }
	public string Type { get; set; }
	public string TimeInForce { get; set; }
	public FinamDecimal LimitPrice { get; set; }
	public FinamDecimal StopPrice { get; set; }
	public string StopCondition { get; set; }
	public string ClientOrderId { get; set; }
	public string Comment { get; set; }
}

sealed class FinamAccountTradesResponse
{
	public FinamAccountTrade[] Trades { get; set; }
}

sealed class FinamAccountTrade
{
	public string TradeId { get; set; }
	public string Symbol { get; set; }
	public FinamDecimal Price { get; set; }
	public FinamDecimal Size { get; set; }
	public string Side { get; set; }
	public DateTime Timestamp { get; set; }
	public string OrderId { get; set; }
	public string AccountId { get; set; }
	public string Comment { get; set; }
	public FinamDecimal AccruedInterest { get; set; }
	public string Currency { get; set; }
}

readonly record struct FinamSocketSubscription(
	string Type,
	string Symbol,
	string TimeFrame,
	string AccountId);

sealed class FinamSocketRequest
{
	public string Action { get; set; }
	public string Type { get; set; }
	public object Data { get; set; }
	public string Token { get; set; }
}

sealed class FinamSocketEnvelope
{
	public string Type { get; set; }
	public string SubscriptionKey { get; set; }
	public string SubscriptionType { get; set; }
	public long Timestamp { get; set; }
	public JToken Payload { get; set; }
	public FinamSocketError ErrorInfo { get; set; }
	public FinamSocketEvent EventInfo { get; set; }
}

sealed class FinamSocketError
{
	public int Code { get; set; }
	public string Type { get; set; }
	public string Message { get; set; }
}

sealed class FinamSocketEvent
{
	public string Event { get; set; }
	public int Code { get; set; }
	public string Reason { get; set; }
}
