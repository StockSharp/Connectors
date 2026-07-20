namespace StockSharp.Bluefin.Native;

sealed class BluefinSigner : IDisposable
{
	private const byte _ed25519Flag = 0;
	private const string _bech32Alphabet =
		"qpzry9x8gf2tvdw0s3jn54khce6mua7l";

	private readonly byte[] _privateKey;
	private readonly byte[] _publicKey;
	private bool _isDisposed;

	public BluefinSigner(string walletAddress, SecureString privateKey)
	{
		var keyText = privateKey.IsEmpty()
			? null
			: privateKey.UnSecure().Trim();
		if (!keyText.IsEmpty())
		{
			_privateKey = DecodePrivateKey(keyText);
			var key = new Ed25519PrivateKeyParameters(_privateKey, 0);
			_publicKey = key.GeneratePublicKey().GetEncoded();
			var derivedAddress = CreateAddress(_publicKey);
			if (!walletAddress.IsEmpty() &&
				walletAddress.NormalizeSuiAddress() != derivedAddress)
			{
				CryptographicOperations.ZeroMemory(_privateKey);
				throw new ArgumentException(
					"The Bluefin wallet address does not match the private key.",
					nameof(walletAddress));
			}
			WalletAddress = derivedAddress;
		}
		else if (!walletAddress.IsEmpty())
			WalletAddress = walletAddress.NormalizeSuiAddress();
	}

	public string WalletAddress { get; }
	public bool IsWalletAvailable => !WalletAddress.IsEmpty();
	public bool IsSigningAvailable => _privateKey is { Length: 32 };

	public string SignLogin(BluefinLoginRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var json = JsonConvert.SerializeObject(request, Formatting.None,
			CreateSettings());
		return SignPersonalMessage(json);
	}

	public string SignOrder(BluefinCreateOrderSignedFields fields)
	{
		ArgumentNullException.ThrowIfNull(fields);
		var signable = new BluefinOrderSignable
		{
			Type = "Bluefin Pro Order",
			Ids = fields.IdsId,
			Account = fields.AccountAddress,
			Market = fields.Symbol,
			Price = fields.PriceE9,
			Quantity = fields.QuantityE9,
			Leverage = fields.LeverageE9,
			Side = fields.Side,
			PositionType = fields.IsIsolated ? "ISOLATED" : "CROSS",
			Expiration = fields.ExpiresAtMillis.ToString(
				CultureInfo.InvariantCulture),
			Salt = fields.Salt,
			SignedAt = fields.SignedAtMillis.ToString(
				CultureInfo.InvariantCulture),
		};
		var json = JsonConvert.SerializeObject(signable, Formatting.Indented,
			CreateSettings()).Replace("\r\n", "\n", StringComparison.Ordinal);
		return SignPersonalMessage(json);
	}

	public void Dispose()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		if (_privateKey is not null)
			CryptographicOperations.ZeroMemory(_privateKey);
	}

	private string SignPersonalMessage(string message)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (!IsSigningAvailable || _publicKey is not { Length: 32 })
			throw new InvalidOperationException(
				"A Sui Ed25519 private key is required for Bluefin signing.");
		var payload = Encoding.UTF8.GetBytes(message);
		var length = EncodeUleb128(payload.Length);
		var intent = new byte[3 + length.Length + payload.Length];
		intent[0] = 3;
		Buffer.BlockCopy(length, 0, intent, 3, length.Length);
		Buffer.BlockCopy(payload, 0, intent, 3 + length.Length,
			payload.Length);
		var digest = Blake2b256(intent);
		try
		{
			var signer = new Ed25519Signer();
			signer.Init(true, new Ed25519PrivateKeyParameters(_privateKey, 0));
			signer.BlockUpdate(digest, 0, digest.Length);
			var signature = signer.GenerateSignature();
			if (signature.Length != 64)
				throw new CryptographicException(
					"Ed25519 returned an unexpected signature length.");
			var serialized = new byte[1 + signature.Length + _publicKey.Length];
			serialized[0] = _ed25519Flag;
			Buffer.BlockCopy(signature, 0, serialized, 1, signature.Length);
			Buffer.BlockCopy(_publicKey, 0, serialized, 1 + signature.Length,
				_publicKey.Length);
			return Convert.ToBase64String(serialized);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(payload);
			CryptographicOperations.ZeroMemory(intent);
			CryptographicOperations.ZeroMemory(digest);
		}
	}

	private static JsonSerializerSettings CreateSettings()
		=> new()
		{
			Culture = CultureInfo.InvariantCulture,
			NullValueHandling = NullValueHandling.Ignore,
		};

	private static byte[] EncodeUleb128(int value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		var result = new List<byte>(5);
		do
		{
			var item = (byte)(value & 0x7f);
			value >>= 7;
			if (value != 0)
				item |= 0x80;
			result.Add(item);
		}
		while (value != 0);
		return [.. result];
	}

	private static byte[] DecodePrivateKey(string value)
	{
		if (value.StartsWith("suiprivkey1", StringComparison.OrdinalIgnoreCase))
		{
			var decoded = DecodeBech32(value);
			try
			{
				if (decoded.Prefix != "suiprivkey" ||
					decoded.Data.Length != 33 ||
					decoded.Data[0] != _ed25519Flag)
					throw new ArgumentException(
						"Bluefin requires a suiprivkey Ed25519 private key.",
						nameof(value));
				return decoded.Data[1..];
			}
			finally
			{
				CryptographicOperations.ZeroMemory(decoded.Data);
			}
		}
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 64 ||
			value.Any(static character => !Uri.IsHexDigit(character)))
			throw new ArgumentException(
				"Bluefin private key must be a suiprivkey Ed25519 key or " +
				"32 hexadecimal bytes.", nameof(value));
		return Convert.FromHexString(value);
	}

	private static string CreateAddress(byte[] publicKey)
	{
		var input = new byte[publicKey.Length + 1];
		input[0] = _ed25519Flag;
		Buffer.BlockCopy(publicKey, 0, input, 1, publicKey.Length);
		var digest = Blake2b256(input);
		try
		{
			return ("0x" + Convert.ToHexString(digest).ToLowerInvariant())
				.NormalizeSuiAddress();
		}
		finally
		{
			CryptographicOperations.ZeroMemory(input);
			CryptographicOperations.ZeroMemory(digest);
		}
	}

	private static byte[] Blake2b256(byte[] value)
	{
		var digest = new Blake2bDigest(256);
		digest.BlockUpdate(value, 0, value.Length);
		var result = new byte[digest.GetDigestSize()];
		digest.DoFinal(result, 0);
		return result;
	}

	private static (string Prefix, byte[] Data) DecodeBech32(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Any(char.IsUpper) && value.Any(char.IsLower))
			throw new FormatException("A Bech32 value cannot mix case.");
		value = value.ToLowerInvariant();
		var separator = value.LastIndexOf('1');
		if (separator is < 1 || separator + 7 > value.Length)
			throw new FormatException("Invalid Bech32 private key.");
		var prefix = value[..separator];
		var words = new byte[value.Length - separator - 1];
		for (var index = 0; index < words.Length; index++)
		{
			var alphabetIndex = _bech32Alphabet.IndexOf(
				value[separator + 1 + index]);
			if (alphabetIndex < 0)
				throw new FormatException("Invalid Bech32 character.");
			words[index] = (byte)alphabetIndex;
		}
		if (Polymod([.. ExpandPrefix(prefix), .. words]) != 1)
			throw new FormatException("Invalid Bech32 checksum.");
		return (prefix, ConvertBits(words[..^6], 5, 8));
	}

	private static byte[] ExpandPrefix(string prefix)
	{
		var result = new byte[prefix.Length * 2 + 1];
		for (var index = 0; index < prefix.Length; index++)
		{
			result[index] = (byte)(prefix[index] >> 5);
			result[prefix.Length + 1 + index] =
				(byte)(prefix[index] & 31);
		}
		return result;
	}

	private static uint Polymod(ReadOnlySpan<byte> values)
	{
		ReadOnlySpan<uint> generators =
		[
			0x3b6a57b2u,
			0x26508e6du,
			0x1ea119fau,
			0x3d4233ddu,
			0x2a1462b3u,
		];
		var checksum = 1u;
		foreach (var item in values)
		{
			var top = checksum >> 25;
			checksum = (checksum & 0x1ffffffu) << 5 ^ item;
			for (var index = 0; index < generators.Length; index++)
				if (((top >> index) & 1) != 0)
					checksum ^= generators[index];
		}
		return checksum;
	}

	private static byte[] ConvertBits(ReadOnlySpan<byte> data, int fromBits,
		int toBits)
	{
		var accumulator = 0;
		var bits = 0;
		var mask = (1 << toBits) - 1;
		var maximum = (1 << (fromBits + toBits - 1)) - 1;
		var result = new List<byte>((data.Length * fromBits) / toBits);
		foreach (var item in data)
		{
			if ((item >> fromBits) != 0)
				throw new FormatException("Invalid Bech32 data word.");
			accumulator = (accumulator << fromBits | item) & maximum;
			bits += fromBits;
			while (bits >= toBits)
			{
				bits -= toBits;
				result.Add((byte)(accumulator >> bits & mask));
			}
		}
		if (bits >= fromBits ||
			(accumulator << (toBits - bits) & mask) != 0)
			throw new FormatException("Invalid Bech32 padding.");
		return [.. result];
	}
}
