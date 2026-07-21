namespace StockSharp.BitGo.Native;

sealed class BitGoTimeInForceConverter :
	JsonConverter<BitGoTimeInForces?>
{
	public override BitGoTimeInForces? ReadJson(JsonReader reader,
		Type objectType, BitGoTimeInForces? existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType == JsonToken.Null)
			return null;
		var value = Convert.ToString(reader.Value,
			CultureInfo.InvariantCulture)?.Trim();
		return value switch
		{
			"1" or "GTC" or "gtc" => BitGoTimeInForces.GoodTillCanceled,
			"3" or "IOC" or "ioc" => BitGoTimeInForces.ImmediateOrCancel,
			"4" or "FOK" or "fok" => BitGoTimeInForces.FillOrKill,
			"6" or "GTD" or "gtd" => BitGoTimeInForces.GoodTillDate,
			_ => throw new JsonSerializationException(
				"Unknown BitGo time-in-force value '" + value + "'."),
		};
	}

	public override void WriteJson(JsonWriter writer,
		BitGoTimeInForces? value,
		JsonSerializer serializer)
	{
		_ = serializer;
		if (value is null)
		{
			writer.WriteNull();
			return;
		}
		writer.WriteValue(value.Value.ToBitGoWire());
	}
}

sealed class BitGoBookLevelConverter : JsonConverter<BitGoBookLevel>
{
	public override BitGoBookLevel ReadJson(JsonReader reader, Type objectType,
		BitGoBookLevel existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				"BitGo book level must be a JSON array.");
		var price = ReadDecimal(reader, "price");
		var size = ReadDecimal(reader, "size");
		decimal? cumulative = null;
		if (!reader.Read())
			throw new JsonSerializationException(
				"Unexpected end of a BitGo book level.");
		if (reader.TokenType != JsonToken.EndArray)
		{
			cumulative = ParseDecimal(reader, "cumulative size");
			if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
				throw new JsonSerializationException(
					"BitGo book level has too many values.");
		}
		return new()
		{
			Price = price,
			Size = size,
			CumulativeSize = cumulative,
		};
	}

	public override void WriteJson(JsonWriter writer, BitGoBookLevel value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Price.ToInvariant());
		writer.WriteValue(value.Size.ToInvariant());
		if (value.CumulativeSize is decimal cumulative)
			writer.WriteValue(cumulative.ToInvariant());
		writer.WriteEndArray();
	}

	private static decimal ReadDecimal(JsonReader reader, string name)
	{
		if (!reader.Read())
			throw new JsonSerializationException(
				"BitGo book level is missing " + name + ".");
		return ParseDecimal(reader, name);
	}

	private static decimal ParseDecimal(JsonReader reader, string name)
	{
		if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or
			JsonToken.Float))
			throw new JsonSerializationException(
				"BitGo book level has an invalid " + name + ".");
		var text = Convert.ToString(reader.Value,
			CultureInfo.InvariantCulture);
		if (!decimal.TryParse(text, NumberStyles.Number,
			CultureInfo.InvariantCulture, out var value))
			throw new JsonSerializationException(
				"BitGo book level has an invalid " + name + ".");
		return value;
	}
}
