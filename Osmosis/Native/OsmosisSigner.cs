namespace StockSharp.Osmosis.Native;

sealed class OsmosisSigner : IDisposable
{
	private const string _exactInputTypeUrl =
		"/osmosis.poolmanager.v1beta1.MsgSwapExactAmountIn";
	private const string _exactOutputTypeUrl =
		"/osmosis.poolmanager.v1beta1.MsgSwapExactAmountOut";
	private const string _publicKeyTypeUrl =
		"/cosmos.crypto.secp256k1.PubKey";
	private const string _bech32Alphabet =
		"qpzry9x8gf2tvdw0s3jn54khce6mua7l";

	private readonly byte[] _privateKey;
	private readonly byte[] _publicKey;
	private bool _isDisposed;

	public OsmosisSigner(string walletAddress, SecureString privateKey)
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
					"Osmosis private key must contain 32 hexadecimal bytes.",
					nameof(privateKey));
			_privateKey = keyText.HexToByteArray();
			var key = new EthECKey(_privateKey, true);
			_publicKey = key.GetPubKey(true);
			var derived = EncodeAddress("osmo",
				CreateAddressBytes(_publicKey));
			if (!walletAddress.IsEmpty() &&
				!walletAddress.NormalizeOsmosisAddress().Equals(derived,
					StringComparison.Ordinal))
				throw new ArgumentException(
					"The Osmosis wallet address does not match the private key.",
					nameof(walletAddress));
			WalletAddress = derived;
		}
		else if (!walletAddress.IsEmpty())
		{
			WalletAddress = walletAddress.NormalizeOsmosisAddress();
		}
	}

	public string WalletAddress { get; }
	public bool IsWalletAvailable => !WalletAddress.IsEmpty();
	public bool IsSigningAvailable => _privateKey is { Length: 32 };

	public byte[] SignSwap(OsmosisQuote quote, string inputDenomination,
		string outputDenomination, BigInteger limitAmount, string chainId,
		ulong accountNumber, ulong sequence, ulong gasLimit,
		BigInteger feeAmount)
	{
		if (!IsSigningAvailable || _publicKey is not { Length: 33 })
			throw new InvalidOperationException(
				"An Osmosis private key is required to sign a swap.");
		ArgumentNullException.ThrowIfNull(quote);
		if (quote.Pools is not { Length: > 0 })
			throw new ArgumentException("A swap route is required.",
				nameof(quote));
		inputDenomination = inputDenomination.NormalizeDenomination();
		outputDenomination = outputDenomination.NormalizeDenomination();
		chainId = chainId.ThrowIfEmpty(nameof(chainId));
		if (limitAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(limitAmount));
		if (gasLimit == 0)
			throw new ArgumentOutOfRangeException(nameof(gasLimit));
		if (feeAmount < 0)
			throw new ArgumentOutOfRangeException(nameof(feeAmount));

		var message = quote.Kind switch
		{
			OsmosisSwapKinds.ExactInput => BuildExactInputMessage(quote,
				inputDenomination, limitAmount),
			OsmosisSwapKinds.ExactOutput => BuildExactOutputMessage(quote,
				outputDenomination, limitAmount),
			_ => throw new ArgumentOutOfRangeException(nameof(quote.Kind),
				quote.Kind, "Unsupported Osmosis swap kind."),
		};
		var typeUrl = quote.Kind == OsmosisSwapKinds.ExactInput
			? _exactInputTypeUrl
			: _exactOutputTypeUrl;
		var messageAny = CreateAny(typeUrl, message);
		var body = Serialize(output => WriteMessage(output, 1, messageAny));

		var publicKey = Serialize(output => WriteBytes(output, 1, _publicKey));
		var publicKeyAny = CreateAny(_publicKeyTypeUrl, publicKey);
		var singleMode = Serialize(output =>
		{
			output.WriteTag(1, WireFormat.WireType.Varint);
			output.WriteEnum(1);
		});
		var modeInfo = Serialize(output => WriteMessage(output, 1, singleMode));
		var signerInfo = Serialize(output =>
		{
			WriteMessage(output, 1, publicKeyAny);
			WriteMessage(output, 2, modeInfo);
			WriteUInt64(output, 3, sequence);
		});
		var feeCoin = BuildCoin(OsmosisExtensions.NativeDenomination,
			feeAmount);
		var fee = Serialize(output =>
		{
			WriteMessage(output, 1, feeCoin);
			WriteUInt64(output, 2, gasLimit);
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
			WriteUInt64(output, 4, accountNumber);
		});

		var signature = new EthECKey(_privateKey, true).Sign(
			SHA256.HashData(signDoc));
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

	private byte[] BuildExactInputMessage(OsmosisQuote quote,
		string inputDenomination, BigInteger minimumOutput)
	{
		if (quote.InputAmount <= 0)
			throw new InvalidDataException(
				"The exact-input quote has no positive input amount.");
		return Serialize(output =>
		{
			WriteString(output, 1, WalletAddress);
			foreach (var pool in quote.Pools)
			{
				var route = Serialize(routeOutput =>
				{
					WriteUInt64(routeOutput, 1, pool.Id);
					WriteString(routeOutput, 2,
						pool.OutputDenomination.NormalizeDenomination());
				});
				WriteMessage(output, 2, route);
			}
			WriteMessage(output, 3, BuildCoin(inputDenomination,
				quote.InputAmount));
			WriteString(output, 4, minimumOutput.ToString(
				CultureInfo.InvariantCulture));
		});
	}

	private byte[] BuildExactOutputMessage(OsmosisQuote quote,
		string outputDenomination, BigInteger maximumInput)
	{
		if (quote.OutputAmount <= 0)
			throw new InvalidDataException(
				"The exact-output quote has no positive output amount.");
		return Serialize(output =>
		{
			WriteString(output, 1, WalletAddress);
			foreach (var pool in quote.Pools)
			{
				var route = Serialize(routeOutput =>
				{
					WriteUInt64(routeOutput, 1, pool.Id);
					WriteString(routeOutput, 2,
						pool.InputDenomination.NormalizeDenomination());
				});
				WriteMessage(output, 2, route);
			}
			WriteString(output, 3, maximumInput.ToString(
				CultureInfo.InvariantCulture));
			WriteMessage(output, 4, BuildCoin(outputDenomination,
				quote.OutputAmount));
		});
	}

	private static byte[] BuildCoin(string denomination, BigInteger amount)
	{
		if (amount < 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		return Serialize(output =>
		{
			WriteString(output, 1, denomination.NormalizeDenomination());
			WriteString(output, 2, amount.ToString(
				CultureInfo.InvariantCulture));
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
		byte[] value) => WriteBytes(output, field, value);

	private static void WriteUInt64(CodedOutputStream output, int field,
		ulong value)
	{
		if (value == 0)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteUInt64(value);
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
		return (prefix, ConvertBits(words[..^6], 5, 8, false));
	}

	private static string EncodeAddress(string prefix, byte[] data)
	{
		var words = ConvertBits(data, 8, 5, true);
		var expanded = ExpandPrefix(prefix);
		var checksumInput = new byte[expanded.Length + words.Length + 6];
		Buffer.BlockCopy(expanded, 0, checksumInput, 0, expanded.Length);
		Buffer.BlockCopy(words, 0, checksumInput, expanded.Length,
			words.Length);
		var polymod = Polymod(checksumInput) ^ 1u;
		var result = new StringBuilder(prefix).Append('1');
		foreach (var word in words)
			result.Append(_bech32Alphabet[word]);
		for (var index = 0; index < 6; index++)
			result.Append(_bech32Alphabet[(int)(polymod >>
				(5 * (5 - index)) & 31)]);
		return result.ToString();
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
		foreach (var value in values)
		{
			var top = checksum >> 25;
			checksum = (checksum & 0x1ffffffu) << 5 ^ value;
			for (var index = 0; index < generators.Length; index++)
				if (((top >> index) & 1) != 0)
					checksum ^= generators[index];
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
