namespace StockSharp.Polymarket.Native;

sealed class PolymarketAuthenticator
{
	private readonly string _apiKey;
	private readonly SecureString _apiSecret;
	private readonly SecureString _passphrase;
	private readonly string _signerAddress;

	public PolymarketAuthenticator(string apiKey, SecureString apiSecret,
		SecureString passphrase, string signerAddress)
	{
		var configured = !apiKey.IsEmpty() || !apiSecret.IsEmpty() ||
			!passphrase.IsEmpty();
		if (configured && (apiKey.IsEmpty() || apiSecret.IsEmpty() ||
			passphrase.IsEmpty() || signerAddress.IsEmpty()))
			throw new ArgumentException("Polymarket API key, API secret, " +
				"passphrase and signer address must be configured together.");
		_apiKey = apiKey?.Trim();
		_apiSecret = apiSecret;
		_passphrase = passphrase;
		_signerAddress = configured
			? signerAddress.NormalizeAddress(nameof(signerAddress))
			: null;
	}

	public bool IsAvailable => !_apiKey.IsEmpty();

	public string ApiKey => _apiKey;

	public void AddRestHeaders(HttpRequestMessage request, string path,
		string body)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!IsAvailable)
			throw new InvalidOperationException(
				"Polymarket API credentials are required for this request.");
		path = path.ThrowIfEmpty(nameof(path));
		if (!path.StartsWith('/'))
			path = "/" + path;
		var timestamp = ((long)DateTime.UtcNow.ToUnix()).ToString(
			CultureInfo.InvariantCulture);
		var message = timestamp + request.Method.Method.ToUpperInvariant() + path +
			(body ?? string.Empty);
		request.Headers.TryAddWithoutValidation("POLY_ADDRESS", _signerAddress);
		request.Headers.TryAddWithoutValidation("POLY_SIGNATURE", Sign(message));
		request.Headers.TryAddWithoutValidation("POLY_TIMESTAMP", timestamp);
		request.Headers.TryAddWithoutValidation("POLY_API_KEY", _apiKey);
		request.Headers.TryAddWithoutValidation("POLY_PASSPHRASE",
			_passphrase.UnSecure());
	}

	public PolymarketSocketAuthentication CreateSocketAuthentication()
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"Polymarket API credentials are required for the user stream.");
		return new()
		{
			ApiKey = _apiKey,
			Secret = _apiSecret.UnSecure(),
			Passphrase = _passphrase.UnSecure(),
		};
	}

	private string Sign(string message)
	{
		var encoded = _apiSecret.UnSecure().Replace('-', '+').Replace('_', '/');
		encoded += new string('=', (4 - encoded.Length % 4) % 4);
		var key = Convert.FromBase64String(encoded);
		try
		{
			using var hmac = new HMACSHA256(key);
			return Convert.ToBase64String(hmac.ComputeHash(message.UTF8()))
				.Replace('+', '-').Replace('/', '_');
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}
}
