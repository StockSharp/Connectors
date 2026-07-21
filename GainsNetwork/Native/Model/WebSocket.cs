namespace StockSharp.GainsNetwork.Native.Model;

sealed class GainsPricePoint
{
	public int PairIndex { get; init; }
	public decimal Price { get; init; }
}

[JsonConverter(typeof(GainsPriceFrameConverter))]
sealed class GainsPriceFrame
{
	public GainsPricePoint[] MarkPrices { get; init; }
	public GainsPricePoint[] IndexPrices { get; init; }
	public long Timestamp { get; init; }
	public bool IsHeartbeat { get; init; }
}

sealed class GainsPriceFrameConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(GainsPriceFrame);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = serializer;
		if (reader.TokenType == JsonToken.None && !reader.Read())
			throw new JsonSerializationException(
				"Gains price frame is empty.");
		return reader.TokenType switch
		{
			JsonToken.StartArray => ReadLegacyFrame(reader),
			JsonToken.StartObject => ReadVersion4Frame(reader),
			_ => throw new JsonSerializationException(
				"Gains price frame must be an array or object."),
		};
	}

	private static GainsPriceFrame ReadLegacyFrame(JsonReader reader)
	{
		var values = ReadNumbers(reader, "legacy price frame");
		if (values.Count == 1)
		{
			return new()
			{
				Timestamp = ToLong(values[0], "heartbeat timestamp"),
				IsHeartbeat = true,
				MarkPrices = [],
				IndexPrices = [],
			};
		}
		return new()
		{
			MarkPrices = ToPoints(values, "mark prices"),
			IndexPrices = [],
		};
	}

	private static GainsPriceFrame ReadVersion4Frame(JsonReader reader)
	{
		GainsPricePoint[] marks = [];
		GainsPricePoint[] indexes = [];
		long timestamp = 0;
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"Gains v4 price property is missing.");
			var name = Convert.ToString(reader.Value,
				CultureInfo.InvariantCulture);
			if (!reader.Read())
				throw new JsonSerializationException(
					"Gains v4 price value is missing.");
			switch (name)
			{
				case "m":
					if (reader.TokenType != JsonToken.StartArray)
						throw new JsonSerializationException(
							"Gains mark prices must be an array.");
					marks = ToPoints(ReadNumbers(reader, "mark prices"),
						"mark prices");
					break;
				case "i":
					if (reader.TokenType != JsonToken.StartArray)
						throw new JsonSerializationException(
							"Gains index prices must be an array.");
					indexes = ToPoints(ReadNumbers(reader, "index prices"),
						"index prices");
					break;
				case "t":
					timestamp = ToLong(ReadNumber(reader, "timestamp"),
						"timestamp");
					break;
				default:
					reader.Skip();
					break;
			}
		}
		return new()
		{
			MarkPrices = marks,
			IndexPrices = indexes,
			Timestamp = timestamp,
		};
	}

	private static List<decimal> ReadNumbers(JsonReader reader, string name)
	{
		var result = new List<decimal>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			result.Add(ReadNumber(reader, name));
		if (reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"Gains " + name + " is truncated.");
		return result;
	}

	private static decimal ReadNumber(JsonReader reader, string name)
	{
		if (reader.TokenType is not (JsonToken.Integer or JsonToken.Float or
			JsonToken.String) || !decimal.TryParse(Convert.ToString(reader.Value,
				CultureInfo.InvariantCulture), NumberStyles.Float,
				CultureInfo.InvariantCulture, out var value))
			throw new JsonSerializationException(
				"Gains " + name + " contains a non-numeric value.");
		return value;
	}

	private static GainsPricePoint[] ToPoints(List<decimal> values,
		string name)
	{
		if (values.Count % 2 != 0)
			throw new JsonSerializationException(
				"Gains " + name + " must contain index/price pairs.");
		var result = new GainsPricePoint[values.Count / 2];
		for (var i = 0; i < values.Count; i += 2)
		{
			var pairIndex = ToLong(values[i], name + " pair index");
			if (pairIndex < 0 || pairIndex > int.MaxValue)
				throw new JsonSerializationException(
					"Gains price pair index is outside the supported range.");
			result[i / 2] = new()
			{
				PairIndex = (int)pairIndex,
				Price = values[i + 1],
			};
		}
		return result;
	}

	private static long ToLong(decimal value, string name)
	{
		if (value != decimal.Truncate(value) || value < long.MinValue ||
			value > long.MaxValue)
			throw new JsonSerializationException(
				"Gains " + name + " must be an integer.");
		return decimal.ToInt64(value);
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}
