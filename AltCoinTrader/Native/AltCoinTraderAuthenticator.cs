namespace StockSharp.AltCoinTrader.Native;

sealed class AltCoinTraderAuthenticator
{
	private readonly byte[] _secret;

	public AltCoinTraderAuthenticator(
		SecureString key,
		SecureString secret)
	{
		Key = key.UnSecure();
		_secret = secret.IsEmpty()
			? null
			: Encoding.UTF8.GetBytes(secret.UnSecure());
	}

	public string Key { get; }

	public bool IsAvailable
		=> !Key.IsEmpty() && _secret is { Length: > 0 };

	public string Sign(
		long timestamp,
		string method,
		string path,
		string body)
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"AltCoinTrader API credentials are not configured.");
		return CreateSignature(
			_secret,
			timestamp,
			method,
			path,
			body);
	}

	internal static string CreateSignature(
		string secret,
		long timestamp,
		string method,
		string path,
		string body)
		=> CreateSignature(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))),
			timestamp,
			method,
			path,
			body);

	private static string CreateSignature(
		byte[] secret,
		long timestamp,
		string method,
		string path,
		string body)
	{
		method = method.ThrowIfEmpty(nameof(method))
			.Trim().ToUpperInvariant();
		path = "/" + path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		var payload = string.Join(
			"\n",
			timestamp.ToString(CultureInfo.InvariantCulture),
			method,
			path,
			body ?? string.Empty);
		return Convert.ToHexString(
			HMACSHA256.HashData(
				secret,
				Encoding.UTF8.GetBytes(payload)))
			.ToLowerInvariant();
	}
}
