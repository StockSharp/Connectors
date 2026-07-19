namespace StockSharp.Gemini.Native.Model;

sealed class GeminiBalancesRequest : GeminiPrivateRequest
{
}

sealed class GeminiOrdersRequest : GeminiPrivateRequest
{
}

sealed class GeminiPositionsRequest : GeminiPrivateRequest
{
}

sealed class GeminiOrderHistoryRequest : GeminiPrivateRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; init; }

	[JsonProperty("limit_orders")]
	public int Limit { get; init; }
}

sealed class GeminiTradesHistoryRequest : GeminiPrivateRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; init; }

	[JsonProperty("limit_trades")]
	public int Limit { get; init; }
}

sealed class GeminiBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("availableForWithdrawal")]
	public decimal AvailableForWithdrawal { get; set; }

	[JsonProperty("pendingWithdrawal")]
	public decimal PendingWithdrawal { get; set; }

	[JsonProperty("pendingDeposit")]
	public decimal PendingDeposit { get; set; }

	[JsonProperty("_timestamp")]
	public long TimestampNanoseconds { get; set; }
}

sealed class GeminiOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("avg_execution_price")]
	public decimal AverageExecutionPrice { get; set; }

	[JsonProperty("side")]
	public GeminiSides Side { get; set; }

	[JsonProperty("type")]
	public GeminiRestOrderTypes OrderType { get; set; }

	[JsonProperty("options")]
	public GeminiOrderOptions[] Options { get; set; }

	[JsonProperty("timestampms")]
	public long TimestampMilliseconds { get; set; }

	[JsonProperty("is_live")]
	public bool IsLive { get; set; }

	[JsonProperty("is_cancelled")]
	public bool IsCanceled { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("executed_amount")]
	public decimal ExecutedAmount { get; set; }

	[JsonProperty("remaining_amount")]
	public decimal RemainingAmount { get; set; }

	[JsonProperty("original_amount")]
	public decimal OriginalAmount { get; set; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("trades")]
	public GeminiMyTrade[] Trades { get; set; }
}

sealed class GeminiMyTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("timestampms")]
	public long TimestampMilliseconds { get; set; }

	[JsonProperty("type")]
	public GeminiTradeSides Side { get; set; }

	[JsonProperty("aggressor")]
	public bool IsAggressor { get; set; }

	[JsonProperty("fee_currency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("fee_amount")]
	public decimal FeeAmount { get; set; }

	[JsonProperty("tid")]
	public long TradeId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }
}

[JsonConverter(typeof(GeminiPositionsResponseConverter))]
sealed class GeminiPositionsResponse
{
	public GeminiOpenPosition[] Positions { get; set; }
}

sealed class GeminiOpenPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instrument_type")]
	public string InstrumentType { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("notional_value")]
	public decimal NotionalValue { get; set; }

	[JsonProperty("realised_pnl")]
	public decimal RealizedPnL { get; set; }

	[JsonProperty("unrealised_pnl")]
	public decimal UnrealizedPnL { get; set; }

	[JsonProperty("average_cost")]
	public decimal AverageCost { get; set; }

	[JsonProperty("mark_price")]
	public decimal MarkPrice { get; set; }
}

sealed class GeminiPositionsResponseConverter : JsonConverter<GeminiPositionsResponse>
{
	public override GeminiPositionsResponse ReadJson(JsonReader reader, Type objectType,
		GeminiPositionsResponse existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.StartArray)
			return new()
			{
				Positions = serializer.Deserialize<GeminiOpenPosition[]>(reader) ?? [],
			};
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"Gemini positions response must be an object or array.");

		GeminiOpenPosition[] positions = [];
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"Gemini positions response contains an invalid property.");
			var name = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException(
					"Gemini positions response ended unexpectedly.");
			if (name.EqualsIgnoreCase("openPositions"))
				positions = serializer.Deserialize<GeminiOpenPosition[]>(reader) ?? [];
			else
				reader.Skip();
		}
		return new() { Positions = positions };
	}

	public override void WriteJson(JsonWriter writer, GeminiPositionsResponse value,
		JsonSerializer serializer)
	{
		writer.WriteStartObject();
		writer.WritePropertyName("openPositions");
		serializer.Serialize(writer, value.Positions ?? []);
		writer.WriteEndObject();
	}
}
