namespace StockSharp.FalconX.Native;

sealed class FalconXAuthenticator : Disposable
{
	private readonly string _apiKey;
	private readonly SecureString _passphrase;
	private readonly byte[] _secret;

	public FalconXAuthenticator(string apiKey, SecureString secret,
		SecureString passphrase)
	{
		if (apiKey.IsEmpty() || secret.IsEmpty() || passphrase.IsEmpty())
			throw new ArgumentException(
				"FalconX API key, base64 secret, and passphrase are required.");
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
				"FalconX API secret must be valid base64.", nameof(secret), error);
		}
		if (_secret.Length == 0)
			throw new ArgumentException("FalconX API secret is empty.",
				nameof(secret));
	}

	public FalconXSocketAuthenticationRequest CreateSocketRequest(string path,
		string requestId)
	{
		path = NormalizePath(path);
		var timestamp = GetUnixSeconds();
		return new()
		{
			ApiKey = _apiKey,
			Passphrase = _passphrase.UnSecure(),
			Signature = Sign(timestamp.ToString(CultureInfo.InvariantCulture) +
				"GET" + path),
			Timestamp = timestamp,
			RequestId = requestId.ThrowIfEmpty(nameof(requestId)),
		};
	}

	public void AddRestHeaders(HttpRequestMessage request, string path,
		string body)
	{
		ArgumentNullException.ThrowIfNull(request);
		path = NormalizePath(path);
		var timestamp = GetUnixSeconds().ToString(CultureInfo.InvariantCulture);
		var signature = Sign(timestamp + request.Method.Method.ToUpperInvariant() +
			path + (body ?? string.Empty));
		request.Headers.TryAddWithoutValidation("FX-ACCESS-KEY", _apiKey);
		request.Headers.TryAddWithoutValidation("FX-ACCESS-SIGN", signature);
		request.Headers.TryAddWithoutValidation("FX-ACCESS-TIMESTAMP", timestamp);
		request.Headers.TryAddWithoutValidation("FX-ACCESS-PASSPHRASE",
			_passphrase.UnSecure());
	}

	private string Sign(string value)
	{
		using var hmac = new HMACSHA256(_secret);
		var signature = hmac.ComputeHash(value.UTF8());
		try
		{
			return Convert.ToBase64String(signature);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(signature);
		}
	}

	private static string NormalizePath(string path)
	{
		path = path.ThrowIfEmpty(nameof(path));
		return path.StartsWith('/') ? path : "/" + path;
	}

	private static long GetUnixSeconds()
		=> checked((long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds);

	protected override void DisposeManaged()
	{
		CryptographicOperations.ZeroMemory(_secret);
		_passphrase.Dispose();
		base.DisposeManaged();
	}
}
