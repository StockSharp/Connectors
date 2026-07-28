namespace StockSharp.MaxExchange.Native;

sealed class MaxExchangeAuthenticator
{
	private readonly string _key;
	private readonly string _secret;

	public MaxExchangeAuthenticator(SecureString key, SecureString secret)
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
				"MAX Exchange API key and secret are required.");

	public string BuildPayload(string path, long nonce,
		IReadOnlyDictionary<string, object> values)
		=> CreatePayload(path, nonce, values);

	public string Sign(string payload)
		=> CreateSignature(
			IsAvailable
				? _secret
				: throw new InvalidOperationException(
					"MAX Exchange API key and secret are required."),
			payload);

	internal static string CreatePayload(string path, long nonce,
		IReadOnlyDictionary<string, object> values)
	{
		var payload = new SortedDictionary<string, object>(
			StringComparer.Ordinal)
		{
			["path"] = path.ThrowIfEmpty(nameof(path)),
			["nonce"] = nonce,
		};
		foreach (var pair in values ?? new Dictionary<string, object>())
		{
			if (!pair.Key.IsEmpty() && pair.Value is not null)
				payload[pair.Key] = pair.Value;
		}
		var json = JsonConvert.SerializeObject(payload,
			new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.None,
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None,
				Culture = CultureInfo.InvariantCulture,
			});
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
	}

	internal static string CreateSignature(string secret, string payload)
	{
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(
			secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(
				payload.ThrowIfEmpty(nameof(payload)))))
			.ToLowerInvariant();
	}

	internal static string CreateWebSocketSignature(
		string secret, long nonce)
		=> CreateSignature(secret,
			nonce.ToString(CultureInfo.InvariantCulture));
}
