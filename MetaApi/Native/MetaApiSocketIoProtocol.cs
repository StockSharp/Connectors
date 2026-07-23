namespace StockSharp.MetaApi.Native;

static class MetaApiSocketIoProtocol
{
	public static Uri CreateWebSocketUri(Uri server, string token, string clientId)
	{
		if (server is null || !server.IsAbsoluteUri)
			throw new ArgumentException("MetaApi streaming server must be an absolute URI.", nameof(server));
		if (server.Scheme is not ("http" or "https" or "ws" or "wss"))
			throw new ArgumentException("MetaApi streaming server must use HTTP or WebSocket transport.", nameof(server));

		var builder = new UriBuilder(server)
		{
			Scheme = server.Scheme is "https" or "wss" ? "wss" : "ws",
			Path = server.AbsolutePath.TrimEnd('/') + "/ws/",
			Query = string.Join('&', new Dictionary<string, string>
			{
				["auth-token"] = token.ThrowIfEmpty(nameof(token)),
				["clientId"] = clientId.ThrowIfEmpty(nameof(clientId)),
				["protocol"] = "3",
				["EIO"] = "3",
				["transport"] = "websocket",
			}.Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")),
		};
		return builder.Uri;
	}

	public static string EncodeEvent<T>(string eventName, T payload)
	{
		if (payload is null)
			throw new ArgumentNullException(nameof(payload));

		using var textWriter = new StringWriter(CultureInfo.InvariantCulture);
		using var writer = new JsonTextWriter(textWriter);
		writer.WriteStartArray();
		writer.WriteValue(eventName.ThrowIfEmpty(nameof(eventName)));
		MetaApiJsonSerializer.Create().Serialize(writer, payload);
		writer.WriteEndArray();
		writer.Flush();
		return "42" + textWriter;
	}

	public static bool TryParseEvent(string frame, out MetaApiSocketEvent socketEvent)
	{
		socketEvent = null;
		if (frame.IsEmpty() || !frame.StartsWith("42", StringComparison.Ordinal))
			return false;

		try
		{
			using var textReader = new StringReader(frame[2..]);
			using var reader = new JsonTextReader(textReader);
			if (!reader.Read() || reader.TokenType != JsonToken.StartArray ||
				!reader.Read() || reader.TokenType != JsonToken.String)
				return false;
			var eventName = (string)reader.Value;
			if (eventName.IsEmpty() || !reader.Read())
				return false;

			var serializer = MetaApiJsonSerializer.Create();
			var parsedEvent = new MetaApiSocketEvent(eventName);
			switch (eventName)
			{
				case "response":
				case "tradeResult":
					parsedEvent.Response = serializer.Deserialize<MetaApiResponse>(reader);
					if (parsedEvent.Response is null)
						return false;
					break;
				case "processingError":
					parsedEvent.ProcessingError =
						serializer.Deserialize<MetaApiProcessingError>(reader);
					if (parsedEvent.ProcessingError is null)
						return false;
					break;
				case "synchronization":
					parsedEvent.Synchronization =
						serializer.Deserialize<MetaApiSynchronizationPacket>(reader);
					if (parsedEvent.Synchronization is null)
						return false;
					break;
				default:
					reader.Skip();
					break;
			}
			socketEvent = parsedEvent;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static MetaApiEngineHandshake ParseHandshake(string frame)
	{
		if (frame.IsEmpty() || frame[0] != '0')
			throw new InvalidDataException("MetaApi returned an invalid Engine.IO handshake.");
		return MetaApiJsonSerializer.Deserialize<MetaApiEngineHandshake>(frame[1..])
			?? throw new InvalidDataException("MetaApi returned an empty Engine.IO handshake.");
	}
}
