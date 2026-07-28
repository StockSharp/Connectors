namespace StockSharp.NovaDax.Native;

static class NovaDaxSocketProtocol
{
	public static string CreateEndpoint(
		string endpoint,
		int engineIoVersion = 3)
	{
		if (engineIoVersion is not 3 and not 4)
			throw new ArgumentOutOfRangeException(
				nameof(engineIoVersion),
				engineIoVersion,
				"Engine.IO version must be 3 or 4.");

		if (!Uri.TryCreate(
			endpoint.ThrowIfEmpty(nameof(endpoint)).Trim(),
			UriKind.Absolute,
			out var uri) ||
			!uri.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"NovaDAX Socket.IO endpoint must be an absolute WSS URI.",
				nameof(endpoint));

		var builder = new UriBuilder(uri)
		{
			Path = "/socket.io/",
			Query = $"EIO={engineIoVersion}&transport=websocket",
		};
		return builder.Uri.AbsoluteUri;
	}

	public static string EncodeEvent(string name, object payload)
		=> "42" + JsonConvert.SerializeObject(
			new object[]
			{
				name.ThrowIfEmpty(nameof(name)),
				payload,
			},
			new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.None,
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None,
				Culture = CultureInfo.InvariantCulture,
			});

	public static bool TryParseEvent(
		string frame,
		out string name,
		out JToken payload)
	{
		name = null;
		payload = null;
		if (frame?.StartsWith(
			"42", StringComparison.Ordinal) != true)
			return false;

		try
		{
			var values = JArray.Parse(frame[2..]);
			if (values.Count < 2 ||
				values[0].Type != JTokenType.String)
				return false;
			name = values[0].Value<string>();
			payload = values[1];
			return !name.IsEmpty();
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
