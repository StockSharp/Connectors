namespace StockSharp.Tiingo.Native.Model;

sealed class TiingoStreamRequest
{
	[JsonProperty("eventName")]
	public string EventName { get; set; }

	[JsonProperty("authorization")]
	public string Authorization { get; set; }

	[JsonProperty("eventData")]
	public TiingoStreamEventData EventData { get; set; }
}

sealed class TiingoStreamEventData
{
	[JsonProperty("subscriptionId")]
	public long? SubscriptionId { get; set; }

	[JsonProperty("thresholdLevel")]
	public int? ThresholdLevel { get; set; }

	[JsonProperty("tickers")]
	public string[] Tickers { get; set; }
}

sealed class TiingoStreamEnvelope
{
	[JsonProperty("messageType")]
	public string MessageType { get; set; }

	[JsonProperty("service")]
	public string Service { get; set; }

	[JsonProperty("response")]
	public TiingoStreamResponse Response { get; set; }

	[JsonProperty("data")]
	public TiingoStreamPayload Data { get; set; }
}

sealed class TiingoStreamResponse
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

[JsonConverter(typeof(TiingoStreamPayloadConverter))]
sealed class TiingoStreamPayload
{
	public long? SubscriptionId { get; set; }
	public string[] Tickers { get; set; }
	public int? ThresholdLevel { get; set; }
	public TiingoStreamData MarketData { get; set; }
}

sealed class TiingoStreamData
{
	public TiingoStreamDataKinds Kind { get; set; }
	public DateTime Time { get; set; }
	public long? Nanoseconds { get; set; }
	public string Ticker { get; set; }
	public string Exchange { get; set; }
	public decimal? BidSize { get; set; }
	public decimal? BidPrice { get; set; }
	public decimal? MidPrice { get; set; }
	public decimal? AskPrice { get; set; }
	public decimal? AskSize { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? LastSize { get; set; }
	public decimal? ReferencePrice { get; set; }
	public decimal? LiquiditySpread { get; set; }
	public bool? IsHalted { get; set; }
	public bool? IsAfterHours { get; set; }
	public bool? IsIntermarketSweep { get; set; }
	public bool? IsOddLot { get; set; }
	public bool? IsNmsExempt { get; set; }
}

sealed class TiingoStreamPayloadConverter : JsonConverter<TiingoStreamPayload>
{
	public override TiingoStreamPayload ReadJson(JsonReader reader, Type objectType,
		TiingoStreamPayload existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType == JsonToken.StartObject)
			return ReadInformation(reader, serializer);
		if (reader.TokenType == JsonToken.StartArray)
			return new() { MarketData = ReadMarketData(reader) };
		throw Error(reader, "object or array");
	}

	public override void WriteJson(JsonWriter writer, TiingoStreamPayload value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();

	private static TiingoStreamPayload ReadInformation(JsonReader reader,
		JsonSerializer serializer)
	{
		var result = new TiingoStreamPayload();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw Error(reader, "property name");
			var name = (string)reader.Value;
			if (!reader.Read())
				throw Error(reader, "property value");
			switch (name)
			{
				case "subscriptionId":
					result.SubscriptionId = CurrentInt64(reader);
					break;
				case "tickers":
					result.Tickers = serializer.Deserialize<string[]>(reader);
					break;
				case "thresholdLevel":
					result.ThresholdLevel = CurrentInt32(reader);
					break;
				default:
					reader.Skip();
					break;
			}
		}
		if (reader.TokenType != JsonToken.EndObject)
			throw Error(reader, "end of object");
		return result;
	}

	private static TiingoStreamData ReadMarketData(JsonReader reader)
	{
		var first = NextString(reader);
		if (first is "Q" or "T" or "B")
		{
			var second = NextString(reader);
			if (Extensions.TryParseUtc(second, out var iexTime))
				return ReadIex(reader, first, iexTime);
			var ticker = second;
			var time = Extensions.ParseUtc(NextString(reader));
			ReadNext(reader);
			if (reader.TokenType == JsonToken.String)
				return ReadCrypto(reader, first, ticker, time);
			return ReadForex(reader, ticker, time);
		}

		var timeValue = Extensions.ParseUtc(first);
		var referenceTicker = NextString(reader);
		var third = NextDecimal(reader);
		ReadNext(reader);
		if (reader.TokenType == JsonToken.EndArray)
		{
			return new()
			{
				Kind = TiingoStreamDataKinds.ReferencePrice,
				Time = timeValue,
				Ticker = referenceTicker,
				ReferencePrice = third,
			};
		}

		var liquidity = new TiingoStreamData
		{
			Kind = TiingoStreamDataKinds.EquityLiquidity,
			Time = timeValue,
			Ticker = referenceTicker,
			LiquiditySpread = third,
			BidSize = CurrentDecimal(reader),
			BidPrice = NextDecimal(reader),
			ReferencePrice = NextDecimal(reader),
			AskPrice = NextDecimal(reader),
			AskSize = NextDecimal(reader),
		};
		ExpectEndArray(reader);
		return liquidity;
	}

	private static TiingoStreamData ReadIex(JsonReader reader, string updateType,
		DateTime time)
	{
		var result = new TiingoStreamData
		{
			Kind = updateType == "Q" ? TiingoStreamDataKinds.IexQuote :
				updateType == "T" ? TiingoStreamDataKinds.IexTrade :
				TiingoStreamDataKinds.IexBreak,
			Time = time,
			Nanoseconds = NextInt64(reader),
			Ticker = NextString(reader),
			BidSize = NextDecimal(reader),
			BidPrice = NextDecimal(reader),
			MidPrice = NextDecimal(reader),
			AskPrice = NextDecimal(reader),
			AskSize = NextDecimal(reader),
			LastPrice = NextDecimal(reader),
			LastSize = NextDecimal(reader),
			IsHalted = NextBooleanNumber(reader),
			IsAfterHours = NextBooleanNumber(reader),
			IsIntermarketSweep = NextBooleanNumber(reader),
			IsOddLot = NextBooleanNumber(reader),
			IsNmsExempt = NextBooleanNumber(reader),
		};
		ExpectEndArray(reader);
		return result;
	}

	private static TiingoStreamData ReadForex(JsonReader reader, string ticker, DateTime time)
	{
		var result = new TiingoStreamData
		{
			Kind = TiingoStreamDataKinds.ForexQuote,
			Time = time,
			Ticker = ticker,
			BidSize = CurrentDecimal(reader),
			BidPrice = NextDecimal(reader),
			MidPrice = NextDecimal(reader),
			AskSize = NextDecimal(reader),
			AskPrice = NextDecimal(reader),
		};
		ExpectEndArray(reader);
		return result;
	}

	private static TiingoStreamData ReadCrypto(JsonReader reader, string updateType,
		string ticker, DateTime time)
	{
		var result = new TiingoStreamData
		{
			Kind = updateType == "T" ? TiingoStreamDataKinds.CryptoTrade :
				TiingoStreamDataKinds.CryptoQuote,
			Time = time,
			Ticker = ticker,
			Exchange = (string)reader.Value,
		};
		if (updateType == "T")
		{
			result.LastSize = NextDecimal(reader);
			result.LastPrice = NextDecimal(reader);
		}
		else
		{
			result.BidSize = NextDecimal(reader);
			result.BidPrice = NextDecimal(reader);
			result.MidPrice = NextDecimal(reader);
			result.AskSize = NextDecimal(reader);
			result.AskPrice = NextDecimal(reader);
		}
		ExpectEndArray(reader);
		return result;
	}

	private static void ReadNext(JsonReader reader)
	{
		if (!reader.Read())
			throw Error(reader, "array value");
	}

	private static string NextString(JsonReader reader)
	{
		ReadNext(reader);
		if (reader.TokenType != JsonToken.String)
			throw Error(reader, "string");
		return (string)reader.Value;
	}

	private static decimal? NextDecimal(JsonReader reader)
	{
		ReadNext(reader);
		return CurrentDecimal(reader);
	}

	private static long? NextInt64(JsonReader reader)
	{
		ReadNext(reader);
		return CurrentInt64(reader);
	}

	private static bool? NextBooleanNumber(JsonReader reader)
	{
		var value = NextInt64(reader);
		return value == null ? null : value.Value != 0;
	}

	private static decimal? CurrentDecimal(JsonReader reader)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType is not JsonToken.Integer and not JsonToken.Float)
			throw Error(reader, "number or null");
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}

	private static long? CurrentInt64(JsonReader reader)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType == JsonToken.String && long.TryParse((string)reader.Value,
			NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
		{
			return parsed;
		}
		if (reader.TokenType is not JsonToken.Integer and not JsonToken.Float)
			throw Error(reader, "integer or null");
		return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
	}

	private static int? CurrentInt32(JsonReader reader)
	{
		var value = CurrentInt64(reader);
		return value == null ? null : checked((int)value.Value);
	}

	private static void ExpectEndArray(JsonReader reader)
	{
		ReadNext(reader);
		if (reader.TokenType != JsonToken.EndArray)
			throw Error(reader, "end of array");
	}

	private static JsonSerializationException Error(JsonReader reader, string expected)
		=> new($"Invalid Tiingo WebSocket payload at path '{reader.Path}': expected {expected}.");
}
