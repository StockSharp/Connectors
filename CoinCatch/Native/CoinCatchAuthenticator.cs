namespace StockSharp.CoinCatch.Native;

sealed class CoinCatchAuthenticator : IDisposable
{
	private readonly string _key;
	private readonly string _secret;
	private readonly string _passphrase;

	public CoinCatchAuthenticator(SecureString key, SecureString secret,
		SecureString passphrase)
	{
		_key = key.UnSecure();
		_secret = secret.UnSecure();
		_passphrase = passphrase.UnSecure();
	}

	public bool IsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty() && !_passphrase.IsEmpty();

	public string Key => _key;
	public string Secret => _secret;
	public string Passphrase => _passphrase;

	public string Sign(long timestamp, string method, string path,
		IEnumerable<KeyValuePair<string, string>> query, string body)
	{
		var queryString = BuildQuery(query);
		return CreateSignature(_secret, CreatePreHash(
			timestamp, method, path, queryString, body));
	}

	public static string CreatePreHash(long timestamp, string method,
		string path, string queryString, string body)
	{
		method = method.ThrowIfEmpty(nameof(method)).Trim()
			.ToUpperInvariant();
		path = path.ThrowIfEmpty(nameof(path)).Trim();
		if (!path.StartsWith('/'))
			path = "/" + path;
		return timestamp.ToString(CultureInfo.InvariantCulture) + method +
			path + (queryString.IsEmpty() ? string.Empty : "?" +
				queryString) + (body ?? string.Empty);
	}

	public static string CreateSignature(string secret, string prehash)
	{
		secret.ThrowIfEmpty(nameof(secret));
		prehash.ThrowIfEmpty(nameof(prehash));
		using var hmac = new HMACSHA256(
			Encoding.UTF8.GetBytes(secret));
		return Convert.ToBase64String(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(prehash)));
	}

	public static string BuildQuery(
		IEnumerable<KeyValuePair<string, string>> query)
		=> (query ?? [])
			.Where(static pair => !pair.Key.IsEmpty() &&
				pair.Value is not null)
			.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
			.Select(static pair =>
				Uri.EscapeDataString(pair.Key) + "=" +
				Uri.EscapeDataString(pair.Value))
			.Join("&");

	public void Dispose()
	{
	}
}
