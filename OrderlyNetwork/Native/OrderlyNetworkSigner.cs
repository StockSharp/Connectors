namespace StockSharp.OrderlyNetwork.Native;

sealed class OrderlyNetworkSignature
{
	public long Timestamp { get; init; }
	public string PublicKey { get; init; }
	public string Value { get; init; }
}

sealed class OrderlyNetworkSigner : Disposable
{
	private const string _base58Alphabet =
		"123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
	private readonly Ed25519PrivateKeyParameters _privateKey;

	public OrderlyNetworkSigner(string accountId, SecureString secret)
	{
		AccountId = NormalizeAccountId(accountId);
		if (secret.IsEmpty())
			return;
		if (AccountId.IsEmpty())
			throw new ArgumentException(
				"An Orderly account ID is required with the ED25519 secret.",
				nameof(accountId));

		var encoded = secret.UnSecure().Trim();
		var seed = DecodeBase58(encoded, nameof(secret));
		try
		{
			if (seed.Length != Ed25519PrivateKeyParameters.KeySize)
				throw new ArgumentException(
					$"Orderly ED25519 secret must decode to {Ed25519PrivateKeyParameters.KeySize} bytes.",
					nameof(secret));
			_privateKey = new(seed, 0);
			PublicKey = "ed25519:" + EncodeBase58(
				_privateKey.GeneratePublicKey().GetEncoded());
		}
		finally
		{
			CryptographicOperations.ZeroMemory(seed);
		}
	}

	public string AccountId { get; }

	public string PublicKey { get; }

	public bool IsAccountAvailable => !AccountId.IsEmpty();

	public bool IsSigningAvailable => _privateKey is not null;

	public OrderlyNetworkSignature SignRequest(HttpMethod method,
		string pathWithQuery, string body, DateTime serverTime)
	{
		ArgumentNullException.ThrowIfNull(method);
		pathWithQuery = pathWithQuery.ThrowIfEmpty(nameof(pathWithQuery));
		return Sign(serverTime,
			method.Method.ToUpperInvariant() + pathWithQuery + (body ?? string.Empty));
	}

	public OrderlyNetworkSignature SignTimestamp(DateTime serverTime)
		=> Sign(serverTime, string.Empty);

	private OrderlyNetworkSignature Sign(DateTime serverTime, string suffix)
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"An Orderly ED25519 secret is required for signing.");
		var timestamp = serverTime.EnsureOrderlyUtc().ToOrderlyMilliseconds();
		var payload = timestamp.ToString(CultureInfo.InvariantCulture) + suffix;
		var bytes = Encoding.UTF8.GetBytes(payload);
		try
		{
			var signer = new Ed25519Signer();
			signer.Init(true, _privateKey);
			signer.BlockUpdate(bytes, 0, bytes.Length);
			return new()
			{
				Timestamp = timestamp,
				PublicKey = PublicKey,
				Value = ToBase64Url(signer.GenerateSignature()),
			};
		}
		finally
		{
			CryptographicOperations.ZeroMemory(bytes);
		}
	}

	private static string NormalizeAccountId(string accountId)
	{
		accountId = accountId?.Trim();
		if (accountId.IsEmpty())
			return null;
		if (accountId.Length != 66 ||
			!accountId.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			accountId.Skip(2).Any(static value => !Uri.IsHexDigit(value)))
			throw new ArgumentException(
				"Orderly account ID must be a 0x-prefixed 32-byte hexadecimal value.",
				nameof(accountId));
		return accountId;
	}

	private static string ToBase64Url(byte[] value)
		=> Convert.ToBase64String(value).TrimEnd('=')
			.Replace('+', '-').Replace('/', '_');

	private static byte[] DecodeBase58(string value, string parameterName)
	{
		value = value.ThrowIfEmpty(parameterName);
		var zeros = value.TakeWhile(static character => character == '1').Count();
		var decoded = new byte[value.Length];
		var length = 0;
		foreach (var character in value)
		{
			var digit = _base58Alphabet.IndexOf(character);
			if (digit < 0)
				throw new ArgumentException(
					"Orderly secret contains a character that is not valid base58.",
					parameterName);
			var carry = digit;
			for (var index = 0; index < length; index++)
			{
				carry += decoded[index] * 58;
				decoded[index] = (byte)(carry & 0xff);
				carry >>= 8;
			}
			while (carry > 0)
			{
				decoded[length++] = (byte)(carry & 0xff);
				carry >>= 8;
			}
		}
		var result = new byte[zeros + length];
		for (var index = 0; index < length; index++)
			result[result.Length - 1 - index] = decoded[index];
		CryptographicOperations.ZeroMemory(decoded);
		return result;
	}

	private static string EncodeBase58(byte[] value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (value.Length == 0)
			return string.Empty;
		var zeros = value.TakeWhile(static item => item == 0).Count();
		var encoded = new char[value.Length * 2];
		var length = 0;
		foreach (var item in value)
		{
			var carry = (int)item;
			for (var index = 0; index < length; index++)
			{
				carry += encoded[index] * 256;
				encoded[index] = (char)(carry % 58);
				carry /= 58;
			}
			while (carry > 0)
			{
				encoded[length++] = (char)(carry % 58);
				carry /= 58;
			}
		}
		var result = new char[zeros + length];
		for (var index = 0; index < zeros; index++)
			result[index] = '1';
		for (var index = 0; index < length; index++)
			result[result.Length - 1 - index] =
				_base58Alphabet[encoded[index]];
		return new(result);
	}
}
