namespace StockSharp.LBank.Native.Model;

sealed class LBankResponse<T>
{
	[JsonProperty("result")]
	[JsonConverter(typeof(LBankBooleanConverter))]
	public bool Result { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("error_code")]
	public int ErrorCode { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime ServerTime { get; set; }
}

sealed class LBankBooleanConverter : JsonConverter<bool>
{
	public override bool ReadJson(JsonReader reader, Type objectType, bool existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		return reader.TokenType switch
		{
			JsonToken.Boolean => (bool)reader.Value,
			JsonToken.Integer => Convert.ToInt64(reader.Value) != 0,
			JsonToken.String when bool.TryParse((string)reader.Value, out var value) => value,
			JsonToken.String => ((string)reader.Value).Equals("1", StringComparison.Ordinal),
			_ => throw new JsonSerializationException($"Unexpected LBank result token '{reader.TokenType}'."),
		};
	}

	public override void WriteJson(JsonWriter writer, bool value, JsonSerializer serializer)
		=> writer.WriteValue(value);
}

sealed class LBankEmpty
{
}

sealed class LBankAuthKeyResponse
{
	[JsonProperty("result")]
	[JsonConverter(typeof(LBankNullableBooleanConverter))]
	public bool? Result { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }

	[JsonProperty("key")]
	public string Key { get; set; }

	[JsonProperty("error_code")]
	public int ErrorCode { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class LBankAccount
{
	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("balances")]
	public LBankBalance[] Balances { get; set; }
}

sealed class LBankBalance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("free")]
	public decimal Free { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }
}

sealed class LBankOrdersPage
{
	[JsonProperty("total")]
	public int TotalPages { get; set; }

	[JsonProperty("current_page")]
	public int CurrentPage { get; set; }

	[JsonProperty("orders")]
	public Order[] Orders { get; set; }
}

sealed class LBankCreateOrderReply
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("custom_id")]
	public string ClientOrderId { get; set; }
}

sealed class LBankCancelOrderReply
{
	[JsonProperty("status")]
	public int Status { get; set; }
}

sealed class LBankWithdrawReply
{
	[JsonProperty("withdrawId")]
	public long WithdrawId { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }
}

sealed class LBankWithdrawResponse
{
	[JsonProperty("result")]
	[JsonConverter(typeof(LBankNullableBooleanConverter))]
	public bool? Result { get; set; }

	[JsonProperty("data")]
	public LBankWithdrawReply Data { get; set; }

	[JsonProperty("withdrawId")]
	public long WithdrawId { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("error_code")]
	public int ErrorCode { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class LBankNullableBooleanConverter : JsonConverter<bool?>
{
	public override bool? ReadJson(JsonReader reader, Type objectType, bool? existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType is JsonToken.Null or JsonToken.Undefined)
			return null;

		return reader.TokenType switch
		{
			JsonToken.Boolean => (bool)reader.Value,
			JsonToken.Integer => Convert.ToInt64(reader.Value) != 0,
			JsonToken.String when bool.TryParse((string)reader.Value, out var value) => value,
			JsonToken.String => ((string)reader.Value).Equals("1", StringComparison.Ordinal),
			_ => throw new JsonSerializationException($"Unexpected LBank result token '{reader.TokenType}'."),
		};
	}

	public override void WriteJson(JsonWriter writer, bool? value, JsonSerializer serializer)
		=> writer.WriteValue(value);
}

sealed class LBankChinaDateTimeConverter : JsonConverter<DateTime>
{
	private static readonly TimeSpan _offset = TimeSpan.FromHours(8);

	public override DateTime ReadJson(JsonReader reader, Type objectType, DateTime existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		var value = reader.Value switch
		{
			DateTime time => time,
			string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time) => time,
			_ => throw new JsonSerializationException($"Unexpected LBank date token '{reader.TokenType}'."),
		};

		value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
		return DateTime.SpecifyKind(value - _offset, DateTimeKind.Utc);
	}

	public override void WriteJson(JsonWriter writer, DateTime value, JsonSerializer serializer)
		=> writer.WriteValue(value.ToUniversalTime().Add(_offset).ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
}

class LBankSocketMessage
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("ping")]
	public string Ping { get; set; }

	[JsonProperty("TS")]
	[JsonConverter(typeof(LBankChinaDateTimeConverter))]
	public DateTime Timestamp { get; set; }
}

sealed class LBankSocketKlineMessage : LBankSocketMessage
{
	[JsonProperty("kbar")]
	public SocketOhlc Kline { get; set; }
}

sealed class LBankSocketDepthMessage : LBankSocketMessage
{
	[JsonProperty("depth")]
	public OrderBook Depth { get; set; }
}

sealed class LBankSocketTradeMessage : LBankSocketMessage
{
	[JsonProperty("trade")]
	public SocketTrade Trade { get; set; }
}

sealed class LBankSocketTickerMessage : LBankSocketMessage
{
	[JsonProperty("tick")]
	public SocketTicker Ticker { get; set; }
}

sealed class LBankSocketOrderMessage : LBankSocketMessage
{
	[JsonProperty("orderUpdate")]
	public SocketOrder Order { get; set; }
}

sealed class LBankSocketAssetMessage : LBankSocketMessage
{
	[JsonProperty("data")]
	public SocketBalance Balance { get; set; }
}

sealed class LBankSocketSubscriptionRequest
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("subscribe")]
	public string Subscribe { get; set; }

	[JsonProperty("pair", NullValueHandling = NullValueHandling.Ignore)]
	public string Pair { get; set; }

	[JsonProperty("depth", NullValueHandling = NullValueHandling.Ignore)]
	public int? Depth { get; set; }

	[JsonProperty("kbar", NullValueHandling = NullValueHandling.Ignore)]
	public string Kline { get; set; }

	[JsonProperty("subscribeKey", NullValueHandling = NullValueHandling.Ignore)]
	public string SubscribeKey { get; set; }
}

sealed class LBankSocketPongRequest
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("pong")]
	public string Pong { get; set; }
}

sealed class SocketBalance
{
	[JsonProperty("assetCode")]
	public string Asset { get; set; }

	[JsonProperty("free")]
	public decimal Free { get; set; }

	[JsonProperty("freeze")]
	public decimal Locked { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}
