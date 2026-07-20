namespace StockSharp.Pacifica.Native;

sealed class PacificaSigner : Disposable
{
	private sealed class AlphabeticalContractResolver : DefaultContractResolver
	{
		protected override IList<JsonProperty> CreateProperties(Type type,
			MemberSerialization memberSerialization)
			=> [.. base.CreateProperties(type, memberSerialization)
				.OrderBy(static property => property.PropertyName,
					StringComparer.Ordinal)];
	}

	private const string _base58Alphabet =
		"123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
	private readonly Ed25519PrivateKeyParameters _privateKey;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new AlphabeticalContractResolver(),
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};

	public PacificaSigner(string account, SecureString privateKey,
		string agentWallet, TimeSpan expiryWindow)
	{
		if (expiryWindow < TimeSpan.FromSeconds(1) ||
			expiryWindow > TimeSpan.FromSeconds(60))
			throw new ArgumentOutOfRangeException(nameof(expiryWindow), expiryWindow,
				"Pacifica signature expiry must be between 1 and 60 seconds.");

		account = NormalizeAddress(account, nameof(account), true);
		agentWallet = NormalizeAddress(agentWallet, nameof(agentWallet), true);
		ExpiryWindow = checked((long)expiryWindow.TotalMilliseconds);

		if (privateKey.IsEmpty())
		{
			if (!agentWallet.IsEmpty())
				throw new ArgumentException(
					"A Pacifica agent wallet requires its private key.",
					nameof(agentWallet));
			Account = account;
			return;
		}

		var encoded = privateKey.UnSecure().Trim();
		var keypair = DecodeBase58(encoded, nameof(privateKey));
		try
		{
			if (keypair.Length != 64)
				throw new ArgumentException(
					"Pacifica private key must be a base58-encoded 64-byte Solana keypair.",
					nameof(privateKey));
			var seed = keypair[..Ed25519PrivateKeyParameters.KeySize];
			try
			{
				_privateKey = new(seed, 0);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(seed);
			}
			var publicBytes = _privateKey.GeneratePublicKey().GetEncoded();
			var expected = keypair[Ed25519PrivateKeyParameters.KeySize..];
			if (!CryptographicOperations.FixedTimeEquals(publicBytes, expected))
				throw new ArgumentException(
					"Pacifica private key contains an inconsistent Solana public key.",
					nameof(privateKey));
			var derivedAddress = EncodeBase58(publicBytes);

			if (!agentWallet.IsEmpty())
			{
				if (account.IsEmpty())
					throw new ArgumentException(
						"Pacifica main account address is required with an agent wallet.",
						nameof(account));
				if (!derivedAddress.Equals(agentWallet, StringComparison.Ordinal))
					throw new ArgumentException(
						"Pacifica agent wallet does not match the configured private key.",
						nameof(agentWallet));
				Account = account;
				AgentWallet = agentWallet;
			}
			else
			{
				if (!account.IsEmpty() &&
					!derivedAddress.Equals(account, StringComparison.Ordinal))
					throw new ArgumentException(
						"Pacifica wallet address does not match the configured private key.",
						nameof(account));
				Account = account.IsEmpty() ? derivedAddress : account;
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(keypair);
		}
	}

	public string Account { get; }

	public string AgentWallet { get; }

	public long ExpiryWindow { get; }

	public bool IsAccountAvailable => !Account.IsEmpty();

	public bool IsSigningAvailable => _privateKey is not null;

	public PacificaSignature Sign<T>(PacificaOperationTypes type, T payload,
		DateTime serverTime)
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"A Pacifica private key is required for trading.");
		ArgumentNullException.ThrowIfNull(payload);
		var timestamp = serverTime.EnsureUtc().ToUnixMilliseconds();
		var message = JsonConvert.SerializeObject(new PacificaSigningEnvelope<T>
		{
			Type = type,
			Timestamp = timestamp,
			ExpiryWindow = ExpiryWindow,
			Data = payload,
		}, _jsonSettings);
		var bytes = Encoding.UTF8.GetBytes(message);
		try
		{
			var signer = new Ed25519Signer();
			signer.Init(true, _privateKey);
			signer.BlockUpdate(bytes, 0, bytes.Length);
			return new()
			{
				Account = Account,
				AgentWallet = AgentWallet,
				Value = EncodeBase58(signer.GenerateSignature()),
				Timestamp = timestamp,
				ExpiryWindow = ExpiryWindow,
			};
		}
		finally
		{
			CryptographicOperations.ZeroMemory(bytes);
		}
	}

	private static string NormalizeAddress(string value, string parameterName,
		bool isOptional)
	{
		value = value?.Trim();
		if (value.IsEmpty())
		{
			if (isOptional)
				return null;
			throw new ArgumentNullException(parameterName);
		}
		var decoded = DecodeBase58(value, parameterName);
		try
		{
			if (decoded.Length != 32 ||
				!EncodeBase58(decoded).Equals(value, StringComparison.Ordinal))
				throw new ArgumentException(
					"Pacifica wallet address must be a canonical base58 Solana public key.",
					parameterName);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(decoded);
		}
		return value;
	}

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
					"Value contains a character that is not valid base58.", parameterName);
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
