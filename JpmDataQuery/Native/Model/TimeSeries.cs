namespace StockSharp.JpmDataQuery.Native.Model;

sealed class JpmDataQueryTimeSeriesResponse : JpmDataQueryPage
{
	[JsonProperty("instruments")]
	public JpmDataQuerySeriesInstrument[] Instruments { get; set; }
}

sealed class JpmDataQuerySeriesInstrument
{
	[JsonProperty("item")]
	public long? Item { get; set; }

	[JsonProperty("instrument-id")]
	public string InstrumentId { get; set; }

	[JsonProperty("instrument-name")]
	public string InstrumentName { get; set; }

	[JsonProperty("instrument-cusip")]
	public string Cusip { get; set; }

	[JsonProperty("instrument-isin")]
	public string Isin { get; set; }

	[JsonProperty("group")]
	public JpmDataQueryGroupReference Group { get; set; }

	[JsonProperty("attributes")]
	public JpmDataQueryAttribute[] Attributes { get; set; }
}

sealed class JpmDataQueryGroupReference
{
	[JsonProperty("group-id")]
	public string GroupId { get; set; }

	[JsonProperty("group-name")]
	public string GroupName { get; set; }
}

sealed class JpmDataQueryAttribute
{
	[JsonProperty("attribute-id")]
	public string AttributeId { get; set; }

	[JsonProperty("attribute-name")]
	public string AttributeName { get; set; }

	[JsonProperty("expression")]
	public string Expression { get; set; }

	[JsonProperty("label")]
	public string Label { get; set; }

	[JsonProperty("last-published")]
	public string LastPublished { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("time-series")]
	public JpmDataQueryTimeSeriesPoint[] TimeSeries { get; set; }
}

[JsonConverter(typeof(JpmDataQueryTimeSeriesPointConverter))]
sealed class JpmDataQueryTimeSeriesPoint
{
	public string Date { get; init; }
	public decimal? Value { get; init; }

	public DateTime? GetTime()
	{
		if (!DateTime.TryParseExact(Date, "yyyyMMdd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
			return null;
		return DateTime.SpecifyKind(date, DateTimeKind.Utc);
	}
}

sealed class JpmDataQueryTimeSeriesPointConverter : JsonConverter<JpmDataQueryTimeSeriesPoint>
{
	public override JpmDataQueryTimeSeriesPoint ReadJson(JsonReader reader, Type objectType,
		JpmDataQueryTimeSeriesPoint existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("DataQuery time-series point must be a JSON array.");
		if (!reader.Read() || reader.TokenType != JsonToken.String)
			throw new JsonSerializationException("DataQuery time-series point has no date.");
		var date = (string)reader.Value;

		if (!reader.Read())
			throw new JsonSerializationException("DataQuery time-series point has no value.");
		decimal? value = reader.TokenType switch
		{
			JsonToken.Null => null,
			JsonToken.Integer or JsonToken.Float or JsonToken.String =>
				Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture),
			_ => throw new JsonSerializationException(
				$"Unexpected DataQuery time-series value token '{reader.TokenType}'."),
		};

		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("DataQuery time-series point has an invalid tuple length.");
		return new() { Date = date, Value = value };
	}

	public override void WriteJson(JsonWriter writer, JpmDataQueryTimeSeriesPoint value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class JpmDataQueryTimeSeriesQuery
{
	public string InstrumentId { get; init; }
	public string Attribute { get; init; }
	public DateTime From { get; init; }
	public DateTime To { get; init; }
	public string Data { get; init; } = "ALL";
	public string Format { get; init; } = "JSON";
	public string Calendar { get; init; } = "CAL_USBANK";
	public string Frequency { get; init; } = "FREQ_DAY";
	public string Conversion { get; init; } = "CONV_LASTBUS_ABS";
	public string NanTreatment { get; init; } = "NA_NOTHING";

	public string ToQueryString()
		=> $"instruments={Encode(InstrumentId)}&attributes={Encode(Attribute)}" +
			$"&data={Encode(Data)}&format={Encode(Format)}" +
			$"&start-date={From:yyyyMMdd}&end-date={To:yyyyMMdd}" +
			$"&calendar={Encode(Calendar)}&frequency={Encode(Frequency)}" +
			$"&conversion={Encode(Conversion)}&nan-treatment={Encode(NanTreatment)}";

	private static string Encode(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);
}
