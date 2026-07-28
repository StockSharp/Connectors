namespace StockSharp.AltCoinTrader.Native;

static class AltCoinTraderWsProtocol
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};

	public static string CreateEndpoint(
		string endpoint,
		bool isPrivate)
	{
		if (!Uri.TryCreate(
			endpoint.ThrowIfEmpty(nameof(endpoint)).Trim(),
			UriKind.Absolute,
			out var uri) ||
			!uri.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"AltCoinTrader WebSocket endpoint must be " +
					"an absolute WSS URI.",
				nameof(endpoint));

		var builder = new UriBuilder(uri)
		{
			Path = isPrivate ? "/ws/private" : "/ws",
			Query = string.Empty,
		};
		return builder.Uri.AbsoluteUri.TrimEnd('/');
	}

	public static string CreateSubscription(
		string channel,
		string market,
		int? depth,
		bool isSubscribe)
	{
		channel = channel.ThrowIfEmpty(nameof(channel))
			.Trim().ToLowerInvariant();
		market = market.IsEmpty()
			? null
			: market.ToAltCoinTraderSymbol();
		if (channel == "orderbook")
			depth = AltCoinTraderRestClient.NormalizeDepth(
				depth ?? 50);
		else
			depth = null;

		return JsonConvert.SerializeObject(
			new
			{
				action = isSubscribe
					? "subscribe"
					: "unsubscribe",
				channel,
				market,
				limit = depth,
			},
			_jsonSettings);
	}

	public static AltCoinTraderWsFrame DeserializeFrame(
		string payload)
	{
		try
		{
			var frame = JsonConvert.DeserializeObject<
				AltCoinTraderWsFrame>(
				payload.ThrowIfEmpty(nameof(payload)),
				_jsonSettings);
			if (frame?.Channel.IsEmpty() != false)
				throw new InvalidDataException(
					"AltCoinTrader WebSocket frame has no channel.");
			return frame;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"AltCoinTrader WebSocket returned malformed JSON.",
				error);
		}
	}
}
