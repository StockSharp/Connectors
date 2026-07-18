namespace StockSharp.NasdaqDataLink.Native.Model;

readonly struct NasdaqDataLinkValue
{
	private NasdaqDataLinkValue(NasdaqDataLinkValueTypes type, decimal? number,
		string text, bool? boolean, DateTime? date)
	{
		Type = type;
		Number = number;
		Text = text;
		Boolean = boolean;
		Date = date;
	}

	public NasdaqDataLinkValueTypes Type { get; }
	public decimal? Number { get; }
	public string Text { get; }
	public bool? Boolean { get; }
	public DateTime? Date { get; }

	public static NasdaqDataLinkValue Null()
		=> new(NasdaqDataLinkValueTypes.Null, null, null, null, null);

	public static NasdaqDataLinkValue FromNumber(decimal value)
		=> new(NasdaqDataLinkValueTypes.Number, value, null, null, null);

	public static NasdaqDataLinkValue FromText(string value)
		=> new(NasdaqDataLinkValueTypes.Text, null, value, null, null);

	public static NasdaqDataLinkValue FromBoolean(bool value)
		=> new(NasdaqDataLinkValueTypes.Boolean, null, null, value, null);

	public static NasdaqDataLinkValue FromDate(DateTime value)
		=> new(NasdaqDataLinkValueTypes.Date, null, null, null, value);

	public decimal? ToDecimal()
	{
		if (Type == NasdaqDataLinkValueTypes.Number)
			return Number;
		if (Type == NasdaqDataLinkValueTypes.Text &&
			decimal.TryParse(Text, NumberStyles.Number | NumberStyles.AllowExponent,
				CultureInfo.InvariantCulture, out var value))
		{
			return value;
		}
		return null;
	}
}

[JsonConverter(typeof(NasdaqDataLinkRowConverter))]
sealed class NasdaqDataLinkRow
{
	public DateTime Date { get; set; }
	public NasdaqDataLinkValue[] Values { get; set; }
}

sealed class NasdaqDataLinkRowConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(NasdaqDataLinkRow);

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Nasdaq Data Link row must be a JSON array.");
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException("Nasdaq Data Link row has no date.");

		var date = ReadDate(reader);
		var values = new List<NasdaqDataLinkValue>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			values.Add(ReadValue(reader));
		if (reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Nasdaq Data Link row is not terminated.");

		return new NasdaqDataLinkRow
		{
			Date = date,
			Values = values.ToArray(),
		};
	}

	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	private static DateTime ReadDate(JsonReader reader)
	{
		DateTime value;
		if (reader.TokenType == JsonToken.Date)
		{
			value = reader.Value switch
			{
				DateTime date => date,
				DateTimeOffset date => date.UtcDateTime,
				_ => throw new JsonSerializationException("Nasdaq Data Link row date is invalid."),
			};
		}
		else if (reader.TokenType == JsonToken.String &&
			DateTime.TryParse((string)reader.Value, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
		{
		}
		else
			throw new JsonSerializationException("Nasdaq Data Link row date is invalid.");

		return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
	}

	private static NasdaqDataLinkValue ReadValue(JsonReader reader)
	{
		switch (reader.TokenType)
		{
			case JsonToken.Null:
			case JsonToken.Undefined:
				return NasdaqDataLinkValue.Null();
			case JsonToken.Integer:
			case JsonToken.Float:
				try
				{
					return NasdaqDataLinkValue.FromNumber(
						Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture));
				}
				catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
				{
					throw new JsonSerializationException(
						"Nasdaq Data Link numeric value is invalid.", ex);
				}
			case JsonToken.String:
				return NasdaqDataLinkValue.FromText((string)reader.Value);
			case JsonToken.Boolean:
				return NasdaqDataLinkValue.FromBoolean((bool)reader.Value);
			case JsonToken.Date:
				return NasdaqDataLinkValue.FromDate(ReadDate(reader));
			default:
				throw new JsonSerializationException(
					$"Nasdaq Data Link row value token '{reader.TokenType}' is not supported.");
		}
	}
}

sealed class NasdaqDataLinkFrequencyConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(NasdaqDataLinkFrequencies);

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType is JsonToken.Null or JsonToken.Undefined)
			return NasdaqDataLinkFrequencies.Unknown;
		if (reader.TokenType != JsonToken.String)
			throw new JsonSerializationException("Nasdaq Data Link frequency must be text.");

		return ((string)reader.Value)?.ToLowerInvariant() switch
		{
			"daily" => NasdaqDataLinkFrequencies.Daily,
			"weekly" => NasdaqDataLinkFrequencies.Weekly,
			"monthly" => NasdaqDataLinkFrequencies.Monthly,
			"quarterly" => NasdaqDataLinkFrequencies.Quarterly,
			"annual" => NasdaqDataLinkFrequencies.Annual,
			_ => NasdaqDataLinkFrequencies.Unknown,
		};
	}

	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}
