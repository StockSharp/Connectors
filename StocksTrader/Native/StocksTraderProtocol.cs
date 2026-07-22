namespace StockSharp.StocksTrader.Native;

static class StocksTraderProtocol
{
	private static readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};

	public static T Parse<T>(string payload)
		where T : class
	{
		var envelope = Deserialize<T>(payload);
		EnsureSuccess(envelope.Code, envelope.Message);
		return envelope.Data ?? throw new InvalidOperationException(
			"StocksTrader returned a successful response without data.");
	}

	public static void EnsureSuccess(string payload)
	{
		var envelope = Deserialize<object>(payload);
		EnsureSuccess(envelope.Code, envelope.Message);
	}

	public static string GetError(string payload)
	{
		if (payload.IsEmpty())
			return null;

		try
		{
			var envelope = JsonConvert.DeserializeObject<StocksTraderEnvelope<object>>(
				payload, _settings);
			return envelope?.Message;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static StocksTraderEnvelope<T> Deserialize<T>(string payload)
	{
		if (payload.IsEmpty())
			throw new InvalidOperationException("StocksTrader returned an empty response.");

		try
		{
			return JsonConvert.DeserializeObject<StocksTraderEnvelope<T>>(payload, _settings)
				?? throw new InvalidOperationException(
					"StocksTrader returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidOperationException(
				"StocksTrader returned invalid JSON.", error);
		}
	}

	private static void EnsureSuccess(string code, string message)
	{
		if (code.EqualsIgnoreCase("ok"))
			return;
		if (code.EqualsIgnoreCase("error"))
			throw new InvalidOperationException(
				$"StocksTrader API error: {message.IsEmpty("No error message was supplied.")}");
		throw new InvalidOperationException(
			$"StocksTrader returned an unknown response code '{code.IsEmpty("<empty>")}'.");
	}
}
