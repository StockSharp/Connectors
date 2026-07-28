namespace StockSharp.BigOne.Native;

sealed class BigOneAuthenticator
{
	private static readonly string _header = Encode(
		Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));

	private readonly SecureString _key;
	private readonly SecureString _secret;

	public BigOneAuthenticator(SecureString key, SecureString secret)
	{
		_key = key;
		_secret = secret;
	}

	public bool IsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	public string CreateSpotToken()
	{
		EnsureAvailable();
		var nonce = checked(
			DateTime.UtcNow.ToBigOneMilliseconds() * 1_000_000L)
			.ToString(CultureInfo.InvariantCulture);
		return CreateSpotToken(
			_key.UnSecure(), _secret.UnSecure(), nonce);
	}

	public string CreateContractToken()
	{
		EnsureAvailable();
		var now = DateTime.UtcNow;
		var seconds = (long)(now - DateTime.UnixEpoch).TotalSeconds;
		var nonce = checked(
			now.ToBigOneMilliseconds() * 1_000L);
		return CreateContractToken(
			_key.UnSecure(), _secret.UnSecure(), nonce, seconds);
	}

	internal static string CreateSpotToken(
		string key, string secret, string nonce)
		=> CreateToken(
			new JObject
			{
				["type"] = "OpenAPIV2",
				["sub"] = key.ThrowIfEmpty(nameof(key)),
				["nonce"] = nonce.ThrowIfEmpty(nameof(nonce)),
			},
			secret);

	internal static string CreateContractToken(
		string key, string secret, long nonce, long issuedAt)
		=> CreateToken(
			new JObject
			{
				["sub"] = key.ThrowIfEmpty(nameof(key)),
				["nonce"] = nonce,
				["iat"] = issuedAt,
				["exp"] = issuedAt + 60,
			},
			secret);

	internal static string Sign(string value, string secret)
	{
		using var hmac = new HMACSHA256(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))));
		return Encode(hmac.ComputeHash(
			Encoding.ASCII.GetBytes(
				value.ThrowIfEmpty(nameof(value)))));
	}

	private static string CreateToken(
		JObject payload, string secret)
	{
		var encodedPayload = Encode(
			Encoding.UTF8.GetBytes(
				payload.ToString(Formatting.None)));
		var unsigned = $"{_header}.{encodedPayload}";
		return $"{unsigned}.{Sign(unsigned, secret)}";
	}

	private static string Encode(byte[] value)
		=> Convert.ToBase64String(value)
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');

	private void EnsureAvailable()
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"BigONE API key and secret are required.");
	}
}
