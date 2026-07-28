namespace StockSharp.CoinSwitch.Native;

static class CoinSwitchSocketProtocol
{
	public static string CreateEndpoint(
		string endpoint,
		CoinSwitchProductTypes productType,
		string exchange)
	{
		if (productType == CoinSwitchProductTypes.Options)
			throw new NotSupportedException(
				"CoinSwitch Options uses the HFT NATS transport.");
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint))
			.TrimEnd('/');
		exchange = exchange.ThrowIfEmpty(nameof(exchange))
			.Trim().ToLowerInvariant();
		var surface = productType == CoinSwitchProductTypes.Spot
			? "spot"
			: "futures";
		return $"{endpoint}/pro/realtime-rates-socket/" +
			$"{surface}/{exchange}/?EIO=4&transport=websocket";
	}

	public static string EncodeEvent(
		string namespaceName,
		string eventName,
		JToken payload)
	{
		namespaceName = NormalizeNamespace(namespaceName);
		eventName = eventName.ThrowIfEmpty(
			nameof(eventName)).Trim();
		return "42" + namespaceName + "," +
			new JArray(eventName, payload ?? new JObject())
				.ToString(Formatting.None);
	}

	public static bool TryParseEvent(
		string frame,
		out string eventName,
		out JToken payload)
	{
		eventName = null;
		payload = null;
		if (frame?.StartsWith(
			"42/",
			StringComparison.Ordinal) != true)
			return false;
		var separator = frame.IndexOf(',');
		if (separator < 3 || separator >= frame.Length - 1)
			return false;
		try
		{
			var values = JArray.Parse(frame[(separator + 1)..]);
			if (values.Count < 2 ||
				values[0].Type != JTokenType.String)
				return false;
			eventName = values[0].Value<string>();
			payload = values[1];
			return !eventName.IsEmpty();
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static string NormalizeNamespace(string value)
		=> "/" + value.ThrowIfEmpty(nameof(value)).Trim()
			.Trim('/');
}
