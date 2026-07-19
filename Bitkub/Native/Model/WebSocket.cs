namespace StockSharp.Bitkub.Native.Model;

enum BitkubPublicWebSocketEvents
{
	[EnumMember(Value = "tradeschanged")]
	TradesChanged,

	[EnumMember(Value = "depthchanged")]
	DepthChanged,

	[EnumMember(Value = "bidschanged")]
	BidsChanged,

	[EnumMember(Value = "askschanged")]
	AsksChanged,

	[EnumMember(Value = "ticker")]
	Ticker,

	[EnumMember(Value = "global.ticker")]
	GlobalTicker,
}

sealed class BitkubPublicWebSocketHeader
{
	[JsonProperty("event")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubPublicWebSocketEvents Event { get; set; }
}

sealed class BitkubPublicWebSocketEnvelope<TData>
{
	[JsonProperty("event")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubPublicWebSocketEvents Event { get; set; }

	[JsonProperty("pairing_id")]
	public int? PairingId { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class BitkubWebSocketDepth
{
	[JsonProperty("bids")]
	public BitkubWebSocketDepthLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BitkubWebSocketDepthLevel[] Asks { get; set; }
}

sealed class BitkubWebSocketDepthLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("base_volume")]
	public decimal BaseVolume { get; set; }

	[JsonProperty("quote_volume")]
	public decimal QuoteVolume { get; set; }
}

[JsonConverter(typeof(BitkubWebSocketTradeConverter))]
sealed class BitkubWebSocketTrade
{
	public long Timestamp { get; set; }
	public decimal Price { get; set; }
	public decimal Amount { get; set; }
	public BitkubSides Side { get; set; }
	public bool IsNew { get; set; }
	public bool IsBuyer { get; set; }
	public bool IsSeller { get; set; }
}

[JsonConverter(typeof(BitkubWebSocketOrderConverter))]
sealed class BitkubWebSocketOrder
{
	public decimal Volume { get; set; }
	public decimal Price { get; set; }
	public decimal Amount { get; set; }
	public bool IsNew { get; set; }
	public bool IsOwner { get; set; }
}

[JsonConverter(typeof(BitkubWebSocketChangedDataConverter))]
sealed class BitkubWebSocketChangedData
{
	public BitkubWebSocketTrade[] Trades { get; set; }
	public BitkubWebSocketOrder[] Bids { get; set; }
	public BitkubWebSocketOrder[] Asks { get; set; }
}

sealed class BitkubWebSocketTradeConverter : JsonConverter<BitkubWebSocketTrade>
{
	public override bool CanWrite => false;

	public override BitkubWebSocketTrade ReadJson(JsonReader reader, Type objectType,
		BitkubWebSocketTrade existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		BitkubBookLevelConverter.EnsureStartArray(reader, "WebSocket trade");
		var value = new BitkubWebSocketTrade
		{
			Timestamp = BitkubBookLevelConverter.ReadValue<long>(reader, serializer,
				"timestamp"),
			Price = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
				"price"),
			Amount = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
				"amount"),
			Side = BitkubBookLevelConverter.ReadValue<string>(reader, serializer,
				"side").ToBitkubSide(),
		};
		_ = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
			"reserved field");
		_ = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
			"reserved field");
		value.IsNew = BitkubBookLevelConverter.ReadValue<bool>(reader, serializer,
			"new flag");
		value.IsBuyer = BitkubBookLevelConverter.ReadValue<bool>(reader, serializer,
			"buyer flag");
		value.IsSeller = BitkubBookLevelConverter.ReadValue<bool>(reader, serializer,
			"seller flag");
		BitkubBookLevelConverter.EnsureEndArray(reader, "WebSocket trade");
		return value;
	}

	public override void WriteJson(JsonWriter writer, BitkubWebSocketTrade value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class BitkubWebSocketOrderConverter : JsonConverter<BitkubWebSocketOrder>
{
	public override bool CanWrite => false;

	public override BitkubWebSocketOrder ReadJson(JsonReader reader, Type objectType,
		BitkubWebSocketOrder existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		BitkubBookLevelConverter.EnsureStartArray(reader, "WebSocket order");
		var value = new BitkubWebSocketOrder
		{
			Volume = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
				"quote volume"),
			Price = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
				"price"),
			Amount = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
				"base amount"),
		};
		_ = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
			"reserved field");
		value.IsNew = BitkubBookLevelConverter.ReadValue<bool>(reader, serializer,
			"new flag");
		value.IsOwner = BitkubBookLevelConverter.ReadValue<bool>(reader, serializer,
			"owner flag");
		BitkubBookLevelConverter.EnsureEndArray(reader, "WebSocket order");
		return value;
	}

	public override void WriteJson(JsonWriter writer, BitkubWebSocketOrder value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class BitkubWebSocketChangedDataConverter :
	JsonConverter<BitkubWebSocketChangedData>
{
	public override bool CanWrite => false;

	public override BitkubWebSocketChangedData ReadJson(JsonReader reader,
		Type objectType, BitkubWebSocketChangedData existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		BitkubBookLevelConverter.EnsureStartArray(reader,
			"WebSocket changed data");
		return new()
		{
			Trades = ReadArray<BitkubWebSocketTrade>(reader, serializer, "trades"),
			Bids = ReadArray<BitkubWebSocketOrder>(reader, serializer, "bids"),
			Asks = ReadArray<BitkubWebSocketOrder>(reader, serializer, "asks"),
		};
	}

	public override void WriteJson(JsonWriter writer,
		BitkubWebSocketChangedData value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	private static TItem[] ReadArray<TItem>(JsonReader reader,
		JsonSerializer serializer, string name)
	{
		if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				$"Bitkub WebSocket changed data is missing {name}.");
		var value = serializer.Deserialize<TItem[]>(reader) ?? [];
		if (name == "asks")
			BitkubBookLevelConverter.EnsureEndArray(reader,
				"WebSocket changed data");
		return value;
	}
}

enum BitkubPrivateWebSocketEvents
{
	[EnumMember(Value = "auth")]
	Authenticate,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "ping")]
	Ping,

	[EnumMember(Value = "order_update")]
	OrderUpdate,

	[EnumMember(Value = "match_update")]
	MatchUpdate,
}

enum BitkubPrivateWebSocketChannels
{
	[EnumMember(Value = "order_update")]
	OrderUpdate,

	[EnumMember(Value = "match_update")]
	MatchUpdate,
}

sealed class BitkubWebSocketAuthenticationData
{
	[JsonProperty("X-BTK-APIKEY")]
	public string ApiKey { get; init; }

	[JsonProperty("X-BTK-SIGN")]
	public string Signature { get; init; }

	[JsonProperty("X-BTK-TIMESTAMP")]
	public string Timestamp { get; init; }
}

sealed class BitkubWebSocketAuthenticationRequest
{
	[JsonProperty("event")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubPrivateWebSocketEvents Event { get; init; } =
		BitkubPrivateWebSocketEvents.Authenticate;

	[JsonProperty("data")]
	public BitkubWebSocketAuthenticationData Data { get; init; }
}

sealed class BitkubWebSocketSubscriptionRequest
{
	[JsonProperty("event")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubPrivateWebSocketEvents Event { get; init; } =
		BitkubPrivateWebSocketEvents.Subscribe;

	[JsonProperty("channel")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubPrivateWebSocketChannels Channel { get; init; }
}

sealed class BitkubWebSocketPingRequest
{
	[JsonProperty("event")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubPrivateWebSocketEvents Event { get; init; } =
		BitkubPrivateWebSocketEvents.Ping;
}

sealed class BitkubPrivateWebSocketHeader
{
	[JsonProperty("event")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubPrivateWebSocketEvents Event { get; set; }

	[JsonProperty("channel")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubPrivateWebSocketChannels? Channel { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class BitkubPrivateWebSocketEnvelope<TData>
{
	[JsonProperty("event")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubPrivateWebSocketEvents Event { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }
}

sealed class BitkubOrderUpdate
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubSides Side { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubOrderTypes Type { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubOrderStatuses Status { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("order_currency")]
	public string OrderCurrency { get; set; }

	[JsonProperty("order_amount")]
	public decimal OrderAmount { get; set; }

	[JsonProperty("executed_currency")]
	public string ExecutedCurrency { get; set; }

	[JsonProperty("executed_amount")]
	public decimal ExecutedAmount { get; set; }

	[JsonProperty("received_currency")]
	public string ReceivedCurrency { get; set; }

	[JsonProperty("received_amount")]
	public decimal ReceivedAmount { get; set; }

	[JsonProperty("total_fee")]
	public decimal TotalFee { get; set; }

	[JsonProperty("credit_used")]
	public decimal CreditUsed { get; set; }

	[JsonProperty("net_fee_paid")]
	public decimal NetFeePaid { get; set; }

	[JsonProperty("avg_filled_price")]
	public decimal? AverageFilledPrice { get; set; }

	[JsonProperty("post_only")]
	public bool IsPostOnly { get; set; }

	[JsonProperty("canceled_by")]
	public string CanceledBy { get; set; }

	[JsonProperty("order_created_at")]
	public long CreatedAt { get; set; }

	[JsonProperty("order_triggered_at")]
	public long? TriggeredAt { get; set; }

	[JsonProperty("order_updated_at")]
	public long? UpdatedAt { get; set; }
}

sealed class BitkubMatchUpdate
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("txn_id")]
	public string TransactionId { get; set; }

	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubOrderTypes Type { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubOrderStatuses Status { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitkubSides Side { get; set; }

	[JsonProperty("is_maker")]
	public bool IsMaker { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("executed_currency")]
	public string ExecutedCurrency { get; set; }

	[JsonProperty("executed_amount")]
	public decimal ExecutedAmount { get; set; }

	[JsonProperty("received_currency")]
	public string ReceivedCurrency { get; set; }

	[JsonProperty("received_amount")]
	public decimal ReceivedAmount { get; set; }

	[JsonProperty("fee_rate")]
	public decimal FeeRate { get; set; }

	[JsonProperty("total_fee")]
	public decimal TotalFee { get; set; }

	[JsonProperty("credit_used")]
	public decimal CreditUsed { get; set; }

	[JsonProperty("net_fee_paid")]
	public decimal NetFeePaid { get; set; }

	[JsonProperty("txn_ts")]
	public long Timestamp { get; set; }
}
