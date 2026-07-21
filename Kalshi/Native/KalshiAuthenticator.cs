namespace StockSharp.Kalshi.Native;

sealed class KalshiAuthenticator : Disposable
{
	private readonly string _apiKey;
	private readonly RSA _signer;
	private readonly Lock _sync = new();

	public KalshiAuthenticator(string apiKey, SecureString privateKey)
	{
		var configured = !apiKey.IsEmpty() || !privateKey.IsEmpty();
		if (configured && (apiKey.IsEmpty() || privateKey.IsEmpty()))
			throw new ArgumentException(
				"Kalshi API key ID and RSA private key must be configured together.");
		_apiKey = apiKey?.Trim();
		if (!configured)
			return;
		_signer = RSA.Create();
		try
		{
			var pem = privateKey.UnSecure();
			if (!pem.Contains('\n') && pem.Contains("\\n",
				StringComparison.Ordinal))
				pem = pem.Replace("\\n", "\n", StringComparison.Ordinal);
			_signer.ImportFromPem(pem);
		}
		catch
		{
			_signer.Dispose();
			throw;
		}
	}

	public bool IsAvailable => !_apiKey.IsEmpty();

	public string ApiKey => _apiKey;

	public void AddRestHeaders(HttpRequestMessage request, string path)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!IsAvailable)
			throw new InvalidOperationException(
				"Kalshi API credentials are required for this request.");
		AddHeaders(request.Headers, request.Method.Method, path);
	}

	public void AddSocketHeaders(ClientWebSocketOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (!IsAvailable)
			throw new InvalidOperationException(
				"Kalshi API credentials are required for WebSocket streaming.");
		var timestamp = checked((long)(DateTime.UtcNow - DateTime.UnixEpoch)
			.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
		var signature = Sign(timestamp + "GET/trade-api/ws/v2");
		options.SetRequestHeader("KALSHI-ACCESS-KEY", _apiKey);
		options.SetRequestHeader("KALSHI-ACCESS-SIGNATURE", signature);
		options.SetRequestHeader("KALSHI-ACCESS-TIMESTAMP", timestamp);
	}

	private void AddHeaders(HttpRequestHeaders headers, string method,
		string path)
	{
		path = path.ThrowIfEmpty(nameof(path));
		if (!path.StartsWith('/'))
			path = "/" + path;
		var query = path.IndexOf('?');
		if (query >= 0)
			path = path[..query];
		var timestamp = checked((long)(DateTime.UtcNow - DateTime.UnixEpoch)
			.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
		var signature = Sign(timestamp + method.ToUpperInvariant() + path);
		headers.TryAddWithoutValidation("KALSHI-ACCESS-KEY", _apiKey);
		headers.TryAddWithoutValidation("KALSHI-ACCESS-SIGNATURE", signature);
		headers.TryAddWithoutValidation("KALSHI-ACCESS-TIMESTAMP", timestamp);
	}

	private string Sign(string message)
	{
		byte[] signature;
		using (_sync.EnterScope())
			signature = _signer.SignData(message.UTF8(), HashAlgorithmName.SHA256,
				RSASignaturePadding.Pss);
		try
		{
			return Convert.ToBase64String(signature);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(signature);
		}
	}

	protected override void DisposeManaged()
	{
		_signer?.Dispose();
		base.DisposeManaged();
	}
}
