namespace StockSharp.BitoPro.Native;

sealed class BitoProAuthenticator
{
	private readonly string _identity;
	private readonly string _key;
	private readonly string _secret;

	public BitoProAuthenticator(string identity, SecureString key,
		SecureString secret)
	{
		_identity = identity?.Trim();
		_key = key.UnSecure();
		_secret = secret.UnSecure();
	}

	public bool IsAvailable
		=> !_identity.IsEmpty() && !_key.IsEmpty() && !_secret.IsEmpty();

	public string Key => _key;

	public string CreateGetPayload(long nonce)
		=> CreateGetPayload(_identity, nonce);

	public string CreatePostPayload(string json)
		=> EncodePayload(json.ThrowIfEmpty(nameof(json)));

	public string Sign(string payload)
		=> CreateSignature(_secret, payload);

	public static string CreateGetPayload(string identity, long nonce)
		=> EncodePayload(JsonConvert.SerializeObject(new
		{
			identity = identity.ThrowIfEmpty(nameof(identity)),
			nonce,
		}, Formatting.None));

	public static string EncodePayload(string json)
		=> Convert.ToBase64String(Encoding.UTF8.GetBytes(
			json.ThrowIfEmpty(nameof(json))))
			.Replace('+', '-')
			.Replace('/', '_');

	public static string CreateSignature(string secret, string payload)
	{
		secret.ThrowIfEmpty(nameof(secret));
		payload.ThrowIfEmpty(nameof(payload));
		using var hmac = new HMACSHA384(
			Encoding.UTF8.GetBytes(secret));
		return Convert.ToHexString(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
	}
}
