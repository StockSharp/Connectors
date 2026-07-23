namespace StockSharp.MetaApi.Native;

static class MetaApiJsonSerializer
{
	private static readonly JsonSerializerSettings _settings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
	};

	public static string Serialize<T>(T value)
	{
		if (value is null)
			throw new ArgumentNullException(nameof(value));
		return JsonConvert.SerializeObject(value, _settings);
	}

	public static T Deserialize<T>(string value)
		=> JsonConvert.DeserializeObject<T>(
			value.ThrowIfEmpty(nameof(value)), _settings);

	public static JsonSerializer Create()
		=> JsonSerializer.Create(_settings);
}
