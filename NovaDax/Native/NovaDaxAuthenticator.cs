namespace StockSharp.NovaDax.Native;

sealed class NovaDaxAuthenticator
{
	private readonly byte[] _secret;

	public NovaDaxAuthenticator(
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
		string method,
		string path,
		string query,
		string body,
		long timestamp)
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"NovaDAX API credentials are not configured.");
		return CreateSignature(
			_secret,
			method,
			path,
			query,
			body,
			timestamp);
	}

	internal static string CreateSignature(
		string secret,
		string method,
		string path,
		string query,
		string body,
		long timestamp)
		=> CreateSignature(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))),
			method,
			path,
			query,
			body,
			timestamp);

	private static string CreateSignature(
		byte[] secret,
		string method,
		string path,
		string query,
		string body,
		long timestamp)
	{
		method = method.ThrowIfEmpty(nameof(method))
			.ToUpperInvariant();
		path = "/" + path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		var content = method is "GET" or "DELETE" or "HEAD"
			? query ?? string.Empty
			: CreateContentHash(body ?? string.Empty);
		var payload = string.Join(
			"\n",
			method,
			path,
			content,
			timestamp.ToString(CultureInfo.InvariantCulture));
		var hash = HMACSHA256.HashData(
			secret,
			Encoding.UTF8.GetBytes(payload));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	internal static string CreateContentHash(string body)
		=> Convert.ToHexString(
			MD5.HashData(
				Encoding.UTF8.GetBytes(body ?? string.Empty)))
			.ToLowerInvariant();
}
