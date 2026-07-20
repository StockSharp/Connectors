namespace StockSharp.Aevo.Native;

sealed class AevoAuthenticator
{
	private readonly string _apiKey;
	private readonly SecureString _apiSecret;

	public AevoAuthenticator(string apiKey, SecureString apiSecret)
	{
		if (apiKey.IsEmpty() != apiSecret.IsEmpty())
			throw new ArgumentException(
				"Aevo API key and API secret must be configured together.");
		_apiKey = apiKey?.Trim();
		_apiSecret = apiSecret;
	}

	public bool IsAvailable => !_apiKey.IsEmpty();

	public void AddRestHeaders(HttpRequestMessage request, string path,
		string body)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!IsAvailable)
			throw new InvalidOperationException(
				"Aevo API credentials are required for this request.");
		var timestamp = DateTime.UtcNow.ToAevoNanoseconds()
			.ToString(CultureInfo.InvariantCulture);
		var message = string.Join(",", _apiKey, timestamp,
			request.Method.Method.ToUpperInvariant(), path, body ?? string.Empty);
		request.Headers.TryAddWithoutValidation("AEVO-KEY", _apiKey);
		request.Headers.TryAddWithoutValidation("AEVO-TIMESTAMP", timestamp);
		request.Headers.TryAddWithoutValidation("AEVO-SIGNATURE", Sign(message));
	}

	public AevoSocketAuth CreateSocketAuth(string operation, string data)
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"Aevo API credentials are required for private WebSocket channels.");
		var timestamp = DateTime.UtcNow.ToAevoNanoseconds()
			.ToString(CultureInfo.InvariantCulture);
		return new()
		{
			Key = _apiKey,
			Timestamp = timestamp,
			Signature = Sign(string.Join(",", _apiKey, timestamp, "ws",
				operation, data ?? string.Empty)),
		};
	}

	private string Sign(string message)
	{
		var key = _apiSecret.UnSecure().UTF8();
		try
		{
			using var hmac = new HMACSHA256(key);
			return Convert.ToHexStringLower(hmac.ComputeHash(message.UTF8()));
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}
}
