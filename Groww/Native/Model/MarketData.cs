namespace StockSharp.Groww.Native.Model;

internal sealed class GrowwCandlesPayload
{
	[JsonProperty("candles")]
	public GrowwCandle[] Candles { get; set; }

	[JsonProperty("closing_price")]
	public decimal? ClosingPrice { get; set; }

	[JsonProperty("start_time")]
	public string StartTime { get; set; }

	[JsonProperty("end_time")]
	public string EndTime { get; set; }

	[JsonProperty("interval_in_minutes")]
	public int? IntervalInMinutes { get; set; }
}

[JsonConverter(typeof(GrowwCandleConverter))]
internal sealed class GrowwCandle
{
	public DateTime OpenTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public decimal? OpenInterest { get; set; }
}

internal sealed class GrowwCandleConverter : JsonConverter
{
	public override bool CanWrite => false;
	public override bool CanConvert(Type objectType) => objectType == typeof(GrowwCandle);

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Groww candle must be a JSON array.");

		var timestamp = ReadValue(reader)?.ToString();
		var candle = new GrowwCandle
		{
			OpenTime = GrowwNativeExtensions.ParseIndiaTime(timestamp) ?? throw new JsonSerializationException($"Invalid Groww candle timestamp '{timestamp}'."),
			Open = ReadDecimal(reader),
			High = ReadDecimal(reader),
			Low = ReadDecimal(reader),
			Close = ReadDecimal(reader),
			Volume = ReadDecimal(reader),
		};

		var openInterest = ReadValue(reader);
		if (openInterest != null)
			candle.OpenInterest = Convert.ToDecimal(openInterest, CultureInfo.InvariantCulture);

		while (reader.TokenType != JsonToken.EndArray && reader.Read())
		{
		}

		return candle;
	}

	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	private static object ReadValue(JsonReader reader)
	{
		if (!reader.Read())
			throw new JsonSerializationException("Unexpected end of Groww candle payload.");
		if (reader.TokenType == JsonToken.EndArray)
			return null;
		return reader.TokenType is JsonToken.Null or JsonToken.Undefined ? null : reader.Value;
	}

	private static decimal ReadDecimal(JsonReader reader)
	{
		var value = ReadValue(reader);
		if (value == null)
			return 0;
		return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
	}
}
