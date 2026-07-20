namespace StockSharp.SunIo.Native;

sealed class SunIoSigner : IDisposable
{
	private const string _base58Alphabet =
		"123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
	private const byte _mainnetPrefix = 0x41;

	private readonly byte[] _privateKey;
	private readonly EthECKey _key;
	private bool _isDisposed;

	public SunIoSigner(string walletAddress, SecureString privateKey)
	{
		var keyText = privateKey.IsEmpty()
			? null
			: privateKey.UnSecure().Trim();
		if (!keyText.IsEmpty())
		{
			if (keyText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				keyText = keyText[2..];
			if (keyText.Length != 64 || keyText.Any(static ch =>
				!Uri.IsHexDigit(ch)))
				throw new ArgumentException(
					"TRON private key must contain 32 hexadecimal bytes.",
					nameof(privateKey));
			_privateKey = keyText.HexToByteArray();
			_key = new(_privateKey, true);
			var derived = CreateAddress(_key.GetPubKey(false));
			if (!walletAddress.IsEmpty() && !walletAddress.NormalizeTronAddress()
				.Equals(derived, StringComparison.Ordinal))
				throw new ArgumentException(
					"The TRON wallet address does not match the private key.",
					nameof(walletAddress));
			WalletAddress = derived;
		}
		else if (!walletAddress.IsEmpty())
		{
			WalletAddress = walletAddress.NormalizeTronAddress();
		}
	}

	public string WalletAddress { get; }
	public bool IsWalletAvailable => !WalletAddress.IsEmpty();
	public bool IsSigningAvailable => _key is not null;

	public string SignTransaction(string rawDataHex, string transactionId)
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"A TRON private key is required to sign a transaction.");
		rawDataHex = rawDataHex.ThrowIfEmpty(nameof(rawDataHex)).Trim();
		if (rawDataHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			rawDataHex = rawDataHex[2..];
		if (rawDataHex.Length == 0 || (rawDataHex.Length & 1) != 0 ||
			rawDataHex.Any(static ch => !Uri.IsHexDigit(ch)))
			throw new FormatException("TRON raw transaction data is not hex.");
		var rawData = rawDataHex.HexToByteArray();
		var hash = SHA256.HashData(rawData);
		var expectedId = Convert.ToHexString(hash).ToLowerInvariant();
		if (!expectedId.Equals(transactionId.NormalizeTransactionHash(),
			StringComparison.Ordinal))
			throw new InvalidDataException(
				"TRON transaction ID does not match its raw data hash.");

		var signed = _key.SignAndCalculateV(hash);
		var result = new byte[65];
		CopyScalar(signed.R, result, 0);
		CopyScalar(signed.S, result, 32);
		var recovery = signed.V is { Length: > 0 }
			? signed.V[^1]
			: throw new CryptographicException(
				"The secp256k1 signer returned no recovery identifier.");
		if (recovery is 27 or 28)
			recovery -= 27;
		if (recovery > 1)
			throw new CryptographicException(
				$"Unsupported TRON recovery identifier '{recovery}'.");
		result[64] = recovery;
		return Convert.ToHexString(result).ToLowerInvariant();
	}

	public static byte[] DecodeAddress(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var decoded = DecodeBase58(value);
		if (decoded.Length != 25 || decoded[0] != _mainnetPrefix)
			throw new FormatException(
				$"'{value}' is not a TRON mainnet address.");
		var payload = decoded[..21];
		var expected = CreateChecksum(payload);
		if (!CryptographicOperations.FixedTimeEquals(expected,
			decoded.AsSpan(21, 4)))
			throw new FormatException(
				$"TRON address '{value}' has an invalid checksum.");
		return payload;
	}

	public static byte[] ToAbiAddress(string value)
		=> DecodeAddress(value)[1..];

	public static bool AreSameAddresses(string first, string second)
	{
		try
		{
			return DecodeAddressOrHex(first).SequenceEqual(
				DecodeAddressOrHex(second));
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private static byte[] DecodeAddressOrHex(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length is 40 or 42 && value.All(Uri.IsHexDigit))
		{
			var bytes = value.HexToByteArray();
			if (bytes.Length == 20)
				return [_mainnetPrefix, .. bytes];
			if (bytes.Length == 21 && bytes[0] == _mainnetPrefix)
				return bytes;
		}
		return DecodeAddress(value)[..21];
	}

	private static string CreateAddress(byte[] publicKey)
	{
		if (publicKey is not { Length: 65 } || publicKey[0] != 4)
			throw new CryptographicException(
				"Expected an uncompressed secp256k1 public key.");
		var hash = Sha3Keccack.Current.CalculateHash(publicKey[1..]);
		var payload = new byte[21];
		payload[0] = _mainnetPrefix;
		Buffer.BlockCopy(hash, hash.Length - 20, payload, 1, 20);
		return EncodeBase58([.. payload, .. CreateChecksum(payload)]);
	}

	private static byte[] CreateChecksum(ReadOnlySpan<byte> payload)
	{
		var first = SHA256.HashData(payload);
		var second = SHA256.HashData(first);
		return second[..4];
	}

	private static byte[] DecodeBase58(string value)
	{
		var number = BigInteger.Zero;
		foreach (var character in value)
		{
			var digit = _base58Alphabet.IndexOf(character);
			if (digit < 0)
				throw new FormatException(
					$"Invalid Base58 character '{character}'.");
			number = number * 58 + digit;
		}
		var bytes = number.ToByteArray(true, true);
		var zeros = value.TakeWhile(static character => character == '1')
			.Count();
		return [.. Enumerable.Repeat((byte)0, zeros), .. bytes];
	}

	private static string EncodeBase58(byte[] value)
	{
		ArgumentNullException.ThrowIfNull(value);
		var number = new BigInteger(value, true, true);
		var result = new StringBuilder();
		while (number > 0)
		{
			number = BigInteger.DivRem(number, 58, out var remainder);
			result.Append(_base58Alphabet[(int)remainder]);
		}
		foreach (var item in value)
		{
			if (item != 0)
				break;
			result.Append('1');
		}
		var characters = result.ToString().ToCharArray();
		Array.Reverse(characters);
		return new(characters);
	}

	private static void CopyScalar(byte[] source, byte[] destination,
		int destinationOffset)
	{
		ArgumentNullException.ThrowIfNull(source);
		var start = 0;
		while (start < source.Length - 1 && source[start] == 0)
			start++;
		var length = source.Length - start;
		if (length > 32)
			throw new CryptographicException(
				"secp256k1 signature scalar exceeds 32 bytes.");
		Buffer.BlockCopy(source, start, destination,
			destinationOffset + 32 - length, length);
	}

	public void Dispose()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		if (_privateKey is not null)
			CryptographicOperations.ZeroMemory(_privateKey);
	}
}
