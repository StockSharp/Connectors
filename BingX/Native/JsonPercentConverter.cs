namespace StockSharp.BingX.Native;

using System.Globalization;

/// <summary>
/// Reads a number the venue formats as a percentage string ("-1.52%").
/// </summary>
class JsonPercentConverter : JsonConverter<double?>
{
	public override double? ReadJson(JsonReader reader, Type objectType, double? existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		var value = reader.Value;

		if (value is null)
			return null;

		if (value is double d)
			return d;

		if (value is long l)
			return l;

		var str = value.ToString().Trim().Remove("%");

		if (str.IsEmpty())
			return null;

		return double.Parse(str, NumberStyles.Any, CultureInfo.InvariantCulture);
	}

	public override void WriteJson(JsonWriter writer, double? value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}
