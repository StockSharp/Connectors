namespace StockSharp.Tokocrypto.Native;

sealed class TokocryptoAuthenticator
{
	private readonly string _key;
	private readonly string _secret;

	public TokocryptoAuthenticator(
		SecureString key, SecureString secret)
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
				"Tokocrypto API key and secret are required.");

	public string Sign(string query)
		=> CreateSignature(
			IsAvailable
				? _secret
				: throw new InvalidOperationException(
					"Tokocrypto API key and secret are required."),
			query);

	internal static string CreateSignature(
		string secret, string query)
	{
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(
			secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(
				query.ThrowIfEmpty(nameof(query)))))
			.ToLowerInvariant();
	}
}
