namespace StockSharp.THORChain.Native;

sealed class THORChainSigner : IDisposable
{
	private const ulong _gasLimit = 600_000_000;
	private const string _depositTypeUrl = "/types.MsgDeposit";
	private const string _publicKeyTypeUrl =
		"/cosmos.crypto.secp256k1.PubKey";
	private const string _bech32Alphabet =
		"qpzry9x8gf2tvdw0s3jn54khce6mua7l";

	private readonly byte[] _privateKey;
	private readonly byte[] _publicKey;
	private readonly byte[] _addressBytes;
	private bool _isDisposed;

	public THORChainSigner(string walletAddress, SecureString privateKey)
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
					"THORChain private key must contain 32 hexadecimal bytes.",
					nameof(privateKey));
			_privateKey = keyText.HexToByteArray();
			var key = new EthECKey(_privateKey, true);
			_publicKey = key.GetPubKey(true);
			_addressBytes = CreateAddressBytes(_publicKey);
			var derived = EncodeAddress("thor", _addressBytes);
			if (!walletAddress.IsEmpty() &&
				!walletAddress.NormalizeThorAddress().Equals(
					derived, StringComparison.Ordinal))
				throw new ArgumentException(
					"The THORChain wallet address does not match the private " +
					"key.", nameof(walletAddress));
			WalletAddress = derived;
		}
		else if (!walletAddress.IsEmpty())
		{
			WalletAddress = walletAddress.NormalizeThorAddress();
			_addressBytes = DecodeAddress(WalletAddress).Data;
		}
	}

	public string WalletAddress { get; }
	public bool IsWalletAvailable => !WalletAddress.IsEmpty();
	public bool IsSigningAvailable => _privateKey is { Length: 32 };

	public byte[] SignDeposit(BigInteger amount, string memo, string chainId,
		ulong accountNumber, ulong sequence)
	{
		if (!IsSigningAvailable || _publicKey is not { Length: 33 })
			throw new InvalidOperationException(
				"A THORChain private key is required to sign a deposit.");
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		memo = memo.ThrowIfEmpty(nameof(memo));
		chainId = chainId.ThrowIfEmpty(nameof(chainId));

		var asset = Serialize(output =>
		{
			WriteString(output, 1, "THOR");
			WriteString(output, 2, "RUNE");
			WriteString(output, 3, "RUNE");
		});
		var coin = Serialize(output =>
		{
			WriteMessage(output, 1, asset);
			WriteString(output, 2, amount.ToString(
				CultureInfo.InvariantCulture));
			output.WriteTag(3, WireFormat.WireType.Varint);
			output.WriteInt64(8);
		});
		var deposit = Serialize(output =>
		{
			WriteMessage(output, 1, coin);
			WriteString(output, 2, memo);
			WriteBytes(output, 3, _addressBytes);
		});
		var depositAny = CreateAny(_depositTypeUrl, deposit);
		var body = Serialize(output =>
		{
			WriteMessage(output, 1, depositAny);
			WriteString(output, 2, memo);
		});

		var publicKey = Serialize(output =>
			WriteBytes(output, 1, _publicKey));
		var publicKeyAny = CreateAny(_publicKeyTypeUrl, publicKey);
		var singleMode = Serialize(output =>
		{
			output.WriteTag(1, WireFormat.WireType.Varint);
			output.WriteEnum(1);
		});
		var modeInfo = Serialize(output =>
			WriteMessage(output, 1, singleMode));
		var signerInfo = Serialize(output =>
		{
			WriteMessage(output, 1, publicKeyAny);
			WriteMessage(output, 2, modeInfo);
			if (sequence != 0)
			{
				output.WriteTag(3, WireFormat.WireType.Varint);
				output.WriteUInt64(sequence);
			}
		});
		var feeCoin = Serialize(output =>
		{
			WriteString(output, 1, THORChainExtensions.RuneDenomination);
			WriteString(output, 2, "0");
		});
		var fee = Serialize(output =>
		{
			WriteMessage(output, 1, feeCoin);
			output.WriteTag(2, WireFormat.WireType.Varint);
			output.WriteUInt64(_gasLimit);
		});
		var authInfo = Serialize(output =>
		{
			WriteMessage(output, 1, signerInfo);
			WriteMessage(output, 2, fee);
		});
		var signDoc = Serialize(output =>
		{
			WriteBytes(output, 1, body);
			WriteBytes(output, 2, authInfo);
			WriteString(output, 3, chainId);
			if (accountNumber != 0)
			{
				output.WriteTag(4, WireFormat.WireType.Varint);
				output.WriteUInt64(accountNumber);
			}
		});

		var hash = SHA256.HashData(signDoc);
		var signature = new EthECKey(_privateKey, true).Sign(hash);
		var compactSignature = new byte[64];
		CopyScalar(signature.R, compactSignature, 0);
		CopyScalar(signature.S, compactSignature, 32);

		return Serialize(output =>
		{
			WriteBytes(output, 1, body);
			WriteBytes(output, 2, authInfo);
			WriteBytes(output, 3, compactSignature);
		});
	}

	private static byte[] CreateAny(string typeUrl, byte[] value)
		=> Serialize(output =>
		{
			WriteString(output, 1, typeUrl);
			WriteBytes(output, 2, value);
		});

	private static byte[] Serialize(Action<CodedOutputStream> write)
	{
		using var stream = new MemoryStream();
		using (var output = new CodedOutputStream(stream, true))
		{
			write(output);
			output.Flush();
		}
		return stream.ToArray();
	}

	private static void WriteString(CodedOutputStream output, int field,
		string value)
	{
		if (value.IsEmpty())
			return;
		output.WriteTag(field, WireFormat.WireType.LengthDelimited);
		output.WriteString(value);
	}

	private static void WriteBytes(CodedOutputStream output, int field,
		byte[] value)
	{
		if (value is not { Length: > 0 })
			return;
		output.WriteTag(field, WireFormat.WireType.LengthDelimited);
		output.WriteBytes(ByteString.CopyFrom(value));
	}

	private static void WriteMessage(CodedOutputStream output, int field,
		byte[] value)
		=> WriteBytes(output, field, value);

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

	private static byte[] CreateAddressBytes(byte[] publicKey)
	{
		var sha = SHA256.HashData(publicKey);
		var digest = new RipeMD160Digest();
		digest.BlockUpdate(sha, 0, sha.Length);
		var result = new byte[digest.GetDigestSize()];
		digest.DoFinal(result, 0);
		return result;
	}

	internal static (string Prefix, byte[] Data) DecodeAddress(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Any(char.IsUpper) && value.Any(char.IsLower))
			throw new FormatException("A Bech32 address cannot mix case.");
		value = value.ToLowerInvariant();
		var separator = value.LastIndexOf('1');
		if (separator is < 1 || separator + 7 > value.Length)
			throw new FormatException("Invalid Bech32 address.");
		var prefix = value[..separator];
		var words = new byte[value.Length - separator - 1];
		for (var i = 0; i < words.Length; i++)
		{
			var index = _bech32Alphabet.IndexOf(value[separator + 1 + i]);
			if (index < 0)
				throw new FormatException("Invalid Bech32 character.");
			words[i] = (byte)index;
		}
		if (Polymod([.. ExpandPrefix(prefix), .. words]) != 1)
			throw new FormatException("Invalid Bech32 checksum.");
		return (prefix, ConvertBits(words[..^6], 5, 8, false));
	}

	private static string EncodeAddress(string prefix, byte[] data)
	{
		var words = ConvertBits(data, 8, 5, true);
		var checksumInput = new byte[ExpandPrefix(prefix).Length +
			words.Length + 6];
		var expanded = ExpandPrefix(prefix);
		Buffer.BlockCopy(expanded, 0, checksumInput, 0, expanded.Length);
		Buffer.BlockCopy(words, 0, checksumInput, expanded.Length,
			words.Length);
		var polymod = Polymod(checksumInput) ^ 1u;
		var result = new StringBuilder(prefix).Append('1');
		foreach (var word in words)
			result.Append(_bech32Alphabet[word]);
		for (var i = 0; i < 6; i++)
			result.Append(_bech32Alphabet[(int)(polymod >>
				(5 * (5 - i)) & 31)]);
		return result.ToString();
	}

	private static byte[] ExpandPrefix(string prefix)
	{
		var result = new byte[prefix.Length * 2 + 1];
		for (var i = 0; i < prefix.Length; i++)
		{
			result[i] = (byte)(prefix[i] >> 5);
			result[prefix.Length + 1 + i] = (byte)(prefix[i] & 31);
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
		foreach (var value in values)
		{
			var top = checksum >> 25;
			checksum = (checksum & 0x1ffffffu) << 5 ^ value;
			for (var i = 0; i < generators.Length; i++)
				if (((top >> i) & 1) != 0)
					checksum ^= generators[i];
		}
		return checksum;
	}

	private static byte[] ConvertBits(ReadOnlySpan<byte> data, int fromBits,
		int toBits, bool isPaddingEnabled)
	{
		var accumulator = 0;
		var bits = 0;
		var mask = (1 << toBits) - 1;
		var maximum = (1 << (fromBits + toBits - 1)) - 1;
		var result = new List<byte>((data.Length * fromBits + toBits - 1) /
			toBits);
		foreach (var value in data)
		{
			if ((value >> fromBits) != 0)
				throw new FormatException("Invalid Bech32 data word.");
			accumulator = (accumulator << fromBits | value) & maximum;
			bits += fromBits;
			while (bits >= toBits)
			{
				bits -= toBits;
				result.Add((byte)(accumulator >> bits & mask));
			}
		}
		if (isPaddingEnabled)
		{
			if (bits > 0)
				result.Add((byte)(accumulator << (toBits - bits) & mask));
		}
		else if (bits >= fromBits ||
			(accumulator << (toBits - bits) & mask) != 0)
		{
			throw new FormatException("Invalid Bech32 padding.");
		}
		return [.. result];
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
