namespace StockSharp.Coinstore.Native;

sealed class CoinstoreAuthenticator
{
	private readonly string _key;
	private readonly string _secret;

	public CoinstoreAuthenticator(SecureString key, SecureString secret)
	{
		_key = key?.UnSecure();
		_secret = secret?.UnSecure();
	}

	public bool IsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	public string Key
		=> IsAvailable
			? _key
			: throw new InvalidOperationException(
				"Coinstore API key and secret are required.");

	public string Sign(long expires, string payload)
		=> CreateSignature(
			IsAvailable
				? _secret
				: throw new InvalidOperationException(
					"Coinstore API key and secret are required."),
			expires, payload);

	internal static string CreateSignature(
		string secret, long expires, string payload)
	{
		var window = (expires / 30000).ToString(
			CultureInfo.InvariantCulture);
		var derived = HmacHex(
			secret.ThrowIfEmpty(nameof(secret)), window);
		return HmacHex(derived, payload ?? string.Empty);
	}

	private static string HmacHex(string key, string value)
	{
		using var hmac = new HMACSHA256(
			Encoding.UTF8.GetBytes(key));
		return Convert.ToHexString(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(value ?? string.Empty)))
			.ToLowerInvariant();
	}
}
