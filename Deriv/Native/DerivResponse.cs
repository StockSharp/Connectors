namespace StockSharp.Deriv.Native;

sealed class DerivResponse
{
	private readonly JObject _root;

	private DerivResponse(JObject root)
	{
		_root = root ?? throw new ArgumentNullException(nameof(root));
	}

	public string MessageType => _root.Value<string>("msg_type");

	public long RequestId => _root.Value<long?>("req_id") ?? 0;

	public string SubscriptionId => _root["subscription"]?.Value<string>("id");

	public bool IsError => _root["error"] is JObject;

	public static DerivResponse Parse(string payload)
	{
		if (payload.IsEmpty())
			throw new InvalidDataException("Deriv WebSocket returned an empty message.");

		JObject root;
		try
		{
			root = JObject.Parse(payload);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Deriv WebSocket returned invalid JSON.", error);
		}

		return new(root);
	}

	public T Get<T>(string property)
		where T : class
	{
		var token = _root[property.ThrowIfEmpty(nameof(property))];
		if (token is null || token.Type == JTokenType.Null)
			return null;
		try
		{
			return token.ToObject<T>();
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"Deriv WebSocket field '{property}' has an invalid shape.", error);
		}
	}

	public T[] GetArray<T>(string property)
		where T : class
		=> Get<T[]>(property) ?? [];

	public Exception CreateException()
	{
		var error = _root["error"] as JObject;
		if (error is null)
			return new InvalidOperationException("Deriv WebSocket returned an unspecified error.");

		var code = error.Value<string>("code").IsEmpty("UnknownError");
		var message = error.Value<string>("message").IsEmpty("No error message was supplied.");
		var field = error["details"]?.Value<string>("field");
		var fieldText = field.IsEmpty() ? string.Empty : $" Field: {field}.";
		return new InvalidOperationException($"Deriv error {code}:{fieldText} {message}");
	}
}
