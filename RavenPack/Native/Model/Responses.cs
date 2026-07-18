namespace StockSharp.RavenPack.Native.Model;

sealed class RavenPackDataset
{
	[JsonProperty("uuid")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("frequency")]
	public string Frequency { get; set; }
}

sealed class RavenPackMappingResponse
{
	[JsonProperty("identifiers_mapped")]
	public RavenPackMappingResult[] Results { get; set; }
}

sealed class RavenPackMappingResult
{
	[JsonProperty("request_data")]
	public RavenPackIdentifier Request { get; set; }

	[JsonProperty("errors")]
	public RavenPackMappingError[] Errors { get; set; }

	[JsonProperty("rp_entities")]
	public RavenPackMappingCandidate[] Candidates { get; set; }
}

sealed class RavenPackMappingCandidate
{
	[JsonProperty("rp_entity_id")]
	public string EntityId { get; set; }

	[JsonProperty("rp_entity_name")]
	public string EntityName { get; set; }

	[JsonProperty("rp_entity_type")]
	public string EntityType { get; set; }

	[JsonProperty("score")]
	public decimal? Score { get; set; }
}

[JsonConverter(typeof(RavenPackMappingErrorConverter))]
sealed class RavenPackMappingError
{
	public string Type { get; set; }
	public string Field { get; set; }
	public string Message { get; set; }
}

sealed class RavenPackEntityReference
{
	[JsonProperty("name")]
	public RavenPackReferenceValue[] Names { get; set; }

	[JsonProperty("entity_name")]
	public RavenPackReferenceValue[] EntityNames { get; set; }

	[JsonProperty("ticker")]
	public RavenPackReferenceValue[] Tickers { get; set; }

	[JsonProperty("symbol")]
	public RavenPackReferenceValue[] Symbols { get; set; }

	[JsonProperty("listing")]
	public RavenPackReferenceValue[] Listings { get; set; }

	[JsonProperty("mic")]
	public RavenPackReferenceValue[] Mics { get; set; }

	[JsonProperty("isin")]
	public RavenPackReferenceValue[] Isins { get; set; }

	[JsonProperty("cusip")]
	public RavenPackReferenceValue[] Cusips { get; set; }

	[JsonProperty("sedol")]
	public RavenPackReferenceValue[] Sedols { get; set; }

	[JsonProperty("type")]
	public RavenPackReferenceValue[] Types { get; set; }
}

sealed class RavenPackReferenceValue
{
	[JsonProperty("data_value")]
	public string Value { get; set; }

	[JsonProperty("range_start")]
	public string Start { get; set; }

	[JsonProperty("range_end")]
	public string End { get; set; }
}

sealed class RavenPackDocumentUrlResponse
{
	[JsonProperty("url")]
	public string Url { get; set; }
}

sealed class RavenPackErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errors")]
	public RavenPackMappingError[] Errors { get; set; }
}

sealed class RavenPackMappingErrorConverter : JsonConverter<RavenPackMappingError>
{
	public override bool CanWrite => false;

	public override RavenPackMappingError ReadJson(JsonReader reader, Type objectType,
		RavenPackMappingError existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType == JsonToken.String)
			return new() { Message = reader.Value as string };
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("Invalid RavenPack mapping error.");

		var error = hasExistingValue && existingValue != null
			? existingValue : new RavenPackMappingError();
		while (reader.Read())
		{
			if (reader.TokenType == JsonToken.EndObject)
				return error;
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("Invalid RavenPack mapping error property.");
			var name = reader.Value as string;
			if (!reader.Read())
				throw new JsonSerializationException("Unexpected RavenPack mapping error end.");
			var value = reader.TokenType == JsonToken.String ? reader.Value as string : null;
			if (name.EqualsIgnoreCase("type") || name.EqualsIgnoreCase("code"))
				error.Type = value;
			else if (name.EqualsIgnoreCase("field"))
				error.Field = value;
			else if (name.EqualsIgnoreCase("message") || name.EqualsIgnoreCase("value") ||
				name.EqualsIgnoreCase("reason"))
				error.Message = value;
			else if (reader.TokenType is JsonToken.StartObject or JsonToken.StartArray)
				reader.Skip();
		}
		throw new JsonSerializationException("Unexpected RavenPack mapping error end.");
	}

	public override void WriteJson(JsonWriter writer, RavenPackMappingError value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}
