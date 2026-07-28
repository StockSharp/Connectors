namespace StockSharp.ZondaCrypto.Native;

sealed class ZondaCryptoAuthenticator
{
	private readonly string _key;
	private readonly string _secret;

	public ZondaCryptoAuthenticator(
		SecureString key,
		SecureString secret)
	{
		_key = key.IsEmpty() ? null : key.UnSecure().Trim();
		_secret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
	}

	public bool IsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	public string Key => _key;

	public string Secret => _secret;

	public string Sign(string timestamp, string body = null)
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"zondacrypto API key and secret are required.");
		return Sign(_key, _secret, timestamp, body);
	}

	internal static string Sign(
		string key,
		string secret,
		string timestamp,
		string body = null)
	{
		var value =
			key.ThrowIfEmpty(nameof(key)) +
			timestamp.ThrowIfEmpty(nameof(timestamp)) +
			(body ?? string.Empty);
		using var hash = new HMACSHA512(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(hash.ComputeHash(
			Encoding.UTF8.GetBytes(value)))
			.ToLowerInvariant();
	}
}
