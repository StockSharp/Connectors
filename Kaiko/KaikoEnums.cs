namespace StockSharp.Kaiko;

/// <summary>Kaiko REST API regions.</summary>
public enum KaikoRegions
{
	/// <summary>United States region.</summary>
	[Display(Name = "United States")]
	Us,

	/// <summary>European Union region.</summary>
	[Display(Name = "Europe")]
	Eu,
}

/// <summary>Kaiko instrument classes supported by the connector.</summary>
[JsonConverter(typeof(KaikoInstrumentClassConverter))]
public enum KaikoInstrumentClasses
{
	/// <summary>All classes when used as a lookup filter.</summary>
	[Display(Name = "All")]
	[EnumMember(Value = "")]
	Unknown,

	/// <summary>Spot instrument.</summary>
	[Display(Name = "Spot")]
	[EnumMember(Value = "spot")]
	Spot,

	/// <summary>Dated future.</summary>
	[Display(Name = "Future")]
	[EnumMember(Value = "future")]
	Future,

	/// <summary>Perpetual future.</summary>
	[Display(Name = "Perpetual future")]
	[EnumMember(Value = "perpetual-future")]
	PerpetualFuture,

	/// <summary>Option.</summary>
	[Display(Name = "Option")]
	[EnumMember(Value = "option")]
	Option,
}

[JsonConverter(typeof(KaikoResultConverter))]
enum KaikoResults
{
	Unknown,
	Success,
	Error,
}

enum KaikoStreamKinds
{
	Trades,
	TopOfBook,
	Ohlcv,
}

enum KaikoSubscriptionKinds
{
	Ticks,
	Level1,
	Candles,
}

sealed class KaikoInstrumentClassConverter : JsonConverter<KaikoInstrumentClasses>
{
	public override KaikoInstrumentClasses ReadJson(JsonReader reader,
		Type objectType, KaikoInstrumentClasses existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		return KaikoExtensions.TryParseInstrumentClass(reader.Value?.ToString(),
			out var value) ? value : KaikoInstrumentClasses.Unknown;
	}

	public override void WriteJson(JsonWriter writer,
		KaikoInstrumentClasses value, JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteValue(value.ToWire());
	}
}

sealed class KaikoResultConverter : JsonConverter<KaikoResults>
{
	public override KaikoResults ReadJson(JsonReader reader, Type objectType,
		KaikoResults existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		return reader.Value?.ToString()?.Trim().ToLowerInvariant() switch
		{
			"success" => KaikoResults.Success,
			"error" or "failed" or "failure" => KaikoResults.Error,
			_ => KaikoResults.Unknown,
		};
	}

	public override void WriteJson(JsonWriter writer, KaikoResults value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteValue(value switch
		{
			KaikoResults.Success => "success",
			KaikoResults.Error => "error",
			_ => string.Empty,
		});
	}
}
