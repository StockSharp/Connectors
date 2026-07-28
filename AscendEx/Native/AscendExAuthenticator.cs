namespace StockSharp.AscendEx.Native;

sealed class AscendExAuthenticator
{
	private readonly string _key;
	private readonly string _secret;

	public AscendExAuthenticator(SecureString key, SecureString secret)
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
				"AscendEX API key and secret are required.");

	public string Sign(long timestamp, string apiPath)
		=> CreateSignature(
			IsAvailable
				? _secret
				: throw new InvalidOperationException(
					"AscendEX API key and secret are required."),
			timestamp, apiPath);

	internal static string CreateSignature(
		string secret, long timestamp, string apiPath)
	{
		var message = timestamp.ToString(
			CultureInfo.InvariantCulture) + "+" +
			apiPath.ThrowIfEmpty(nameof(apiPath)).Trim('/');
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(
			secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToBase64String(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(message)));
	}
}
