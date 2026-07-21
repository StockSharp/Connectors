namespace StockSharp.ZeroHash.Native;

sealed class ZeroHashAuthenticator : Disposable
{
	private readonly string _apiKey;
	private readonly SecureString _passphrase;
	private readonly byte[] _secret;

	public ZeroHashAuthenticator(string apiKey, SecureString secret,
		SecureString passphrase)
	{
		if (apiKey.IsEmpty() || secret.IsEmpty() || passphrase.IsEmpty())
			throw new ArgumentException(
				"Zero Hash API key, base64 secret, and passphrase are required.");
		_apiKey = apiKey.Trim();
		_passphrase = passphrase.Copy();
		_passphrase.MakeReadOnly();
		try
		{
			_secret = Convert.FromBase64String(secret.UnSecure().Trim());
		}
		catch (FormatException error)
		{
			throw new ArgumentException(
				"Zero Hash API secret must be valid base64.", nameof(secret), error);
		}
		if (_secret.Length == 0)
			throw new ArgumentException("Zero Hash API secret is empty.",
				nameof(secret));
	}

	public void AddHeaders(HttpRequestMessage request, string route,
		string body)
	{
		ArgumentNullException.ThrowIfNull(request);
		route = NormalizeRoute(route);
		var timestamp = checked((long)(DateTime.UtcNow - DateTime.UnixEpoch)
			.TotalSeconds).ToString(CultureInfo.InvariantCulture);
		var signedBody = request.Method == HttpMethod.Get
			? "{}"
			: body ?? "{}";
		var value = timestamp + request.Method.Method.ToUpperInvariant() +
			route + signedBody;
		using var hmac = new HMACSHA256(_secret);
		var bytes = hmac.ComputeHash(value.UTF8());
		string signature;
		try
		{
			signature = Convert.ToBase64String(bytes);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(bytes);
		}
		request.Headers.TryAddWithoutValidation("X-SCX-API-KEY", _apiKey);
		request.Headers.TryAddWithoutValidation("X-SCX-SIGNED", signature);
		request.Headers.TryAddWithoutValidation("X-SCX-TIMESTAMP", timestamp);
		request.Headers.TryAddWithoutValidation("X-SCX-PASSPHRASE",
			_passphrase.UnSecure());
	}

	private static string NormalizeRoute(string route)
	{
		route = route.ThrowIfEmpty(nameof(route));
		return route.StartsWith('/') ? route : "/" + route;
	}

	protected override void DisposeManaged()
	{
		CryptographicOperations.ZeroMemory(_secret);
		_passphrase.Dispose();
		base.DisposeManaged();
	}
}
