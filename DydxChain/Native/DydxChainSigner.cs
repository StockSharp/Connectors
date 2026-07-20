namespace StockSharp.DydxChain.Native;

sealed class DydxChainSigner : IDisposable
{
	private const string _placeOrderTypeUrl =
		"/dydxprotocol.clob.MsgPlaceOrder";
	private const string _cancelOrderTypeUrl =
		"/dydxprotocol.clob.MsgCancelOrder";
	private const string _publicKeyTypeUrl =
		"/cosmos.crypto.secp256k1.PubKey";
	private const string _transactionExtensionTypeUrl =
		"/dydxprotocol.accountplus.TxExtension";
	private const string _baseAccountTypeUrl =
		"/cosmos.auth.v1beta1.BaseAccount";
	private const string _bech32Alphabet =
		"qpzry9x8gf2tvdw0s3jn54khce6mua7l";

	private readonly byte[] _privateKey;
	private readonly byte[] _publicKey;
	private bool _isDisposed;

	public DydxChainSigner(string walletAddress, SecureString privateKey)
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
					"dYdX private key must contain 32 hexadecimal bytes.",
					nameof(privateKey));
			_privateKey = keyText.HexToByteArray();
			var key = new EthECKey(_privateKey, true);
			_publicKey = key.GetPubKey(true);
			var derived = EncodeAddress("dydx",
				CreateAddressBytes(_publicKey));
			if (!walletAddress.IsEmpty() &&
				!walletAddress.NormalizeAddress().Equals(derived,
					StringComparison.Ordinal))
				throw new ArgumentException(
					"The dYdX wallet address does not match the private key.",
					nameof(walletAddress));
			WalletAddress = derived;
		}
		else if (!walletAddress.IsEmpty())
		{
			WalletAddress = walletAddress.NormalizeAddress();
		}
	}

	public string WalletAddress { get; }
	public bool IsWalletAvailable => !WalletAddress.IsEmpty();
	public bool IsSigningAvailable => _privateKey is { Length: 32 };

	public byte[] SignPlaceOrder(DydxChainPlaceOrder order, string chainId,
		ulong accountNumber, ulong sequence, ulong gasLimit)
	{
		ArgumentNullException.ThrowIfNull(order);
		ValidateOwner(order.Address);
		ValidateExpiration(order.OrderFlags, order.GoodTilBlock,
			order.GoodTilBlockTime);
		if (order.Quantums == 0 || order.Subticks == 0)
			throw new ArgumentException(
				"dYdX order quantums and subticks must be positive.",
				nameof(order));
		if (order.Side == DydxChainProtoSides.Unspecified)
			throw new ArgumentException("dYdX order side is required.",
				nameof(order));

		var orderId = BuildOrderId(order.Address, order.SubaccountNumber,
			order.ClientId, order.OrderFlags, order.ClobPairId);
		var protoOrder = Serialize(output =>
		{
			WriteMessage(output, 1, orderId);
			WriteEnum(output, 2, (int)order.Side);
			WriteUInt64(output, 3, order.Quantums);
			WriteUInt64(output, 4, order.Subticks);
			if (order.GoodTilBlock != 0)
				WriteUInt32(output, 5, order.GoodTilBlock);
			else
				WriteFixed32(output, 6, order.GoodTilBlockTime);
			WriteEnum(output, 7, (int)order.TimeInForce);
			WriteBool(output, 8, order.IsReduceOnly);
			WriteUInt32(output, 9, order.ClientMetadata);
			WriteEnum(output, 10, (int)order.ConditionType);
			WriteUInt64(output, 11, order.ConditionalTriggerSubticks);
			if (order.TwapParameters is not null)
				WriteMessage(output, 12, BuildTwapParameters(
					order.TwapParameters));
		});
		var message = Serialize(output => WriteMessage(output, 1, protoOrder));
		return SignMessage(_placeOrderTypeUrl, message, chainId,
			accountNumber, sequence, gasLimit);
	}

	public byte[] SignCancelOrder(DydxChainCancelOrder order, string chainId,
		ulong accountNumber, ulong sequence, ulong gasLimit)
	{
		ArgumentNullException.ThrowIfNull(order);
		ValidateOwner(order.Address);
		ValidateExpiration(order.OrderFlags, order.GoodTilBlock,
			order.GoodTilBlockTime);
		var orderId = BuildOrderId(order.Address, order.SubaccountNumber,
			order.ClientId, order.OrderFlags, order.ClobPairId);
		var message = Serialize(output =>
		{
			WriteMessage(output, 1, orderId);
			if (order.GoodTilBlock != 0)
				WriteUInt32(output, 2, order.GoodTilBlock);
			else
				WriteFixed32(output, 3, order.GoodTilBlockTime);
		});
		return SignMessage(_cancelOrderTypeUrl, message, chainId,
			accountNumber, sequence, gasLimit);
	}

	public static byte[] CreateAccountQuery(string address)
		=> Serialize(output => WriteString(output, 1,
			address.NormalizeAddress()));

	public static DydxChainAccountInfo ParseAccountQueryResponse(byte[] value)
	{
		if (value is not { Length: > 0 })
			throw new InvalidDataException(
				"dYdX account query returned an empty protobuf response.");
		var accountAny = ReadMessageField(value, 1, "account response");
		string typeUrl = null;
		byte[] account = null;
		using (var input = new CodedInputStream(accountAny))
		{
			while (!input.IsAtEnd)
			{
				var tag = input.ReadTag();
				if (tag == 0)
					break;
				switch (WireFormat.GetTagFieldNumber(tag))
				{
					case 1:
						typeUrl = input.ReadString();
						break;
					case 2:
						account = input.ReadBytes().ToByteArray();
						break;
					default:
						input.SkipLastField();
						break;
				}
			}
		}
		if (!string.Equals(typeUrl, _baseAccountTypeUrl,
			StringComparison.Ordinal) ||
			account is not { Length: > 0 })
			throw new InvalidDataException(
				$"dYdX returned unsupported account type '{typeUrl}'.");

		ulong accountNumber = 0;
		ulong sequence = 0;
		using (var input = new CodedInputStream(account))
		{
			while (!input.IsAtEnd)
			{
				var tag = input.ReadTag();
				if (tag == 0)
					break;
				switch (WireFormat.GetTagFieldNumber(tag))
				{
					case 3:
						accountNumber = input.ReadUInt64();
						break;
					case 4:
						sequence = input.ReadUInt64();
						break;
					default:
						input.SkipLastField();
						break;
				}
			}
		}
		return new()
		{
			AccountNumber = accountNumber,
			Sequence = sequence,
		};
	}

	private byte[] SignMessage(string typeUrl, byte[] message, string chainId,
		ulong accountNumber, ulong sequence, ulong gasLimit)
	{
		if (!IsSigningAvailable || _publicKey is not { Length: 33 })
			throw new InvalidOperationException(
				"A dYdX private key is required to sign transactions.");
		chainId = chainId.ThrowIfEmpty(nameof(chainId));
		if (gasLimit == 0)
			throw new ArgumentOutOfRangeException(nameof(gasLimit));

		var messageAny = CreateAny(typeUrl, message);
		var extensionAny = CreateAny(_transactionExtensionTypeUrl, []);
		var body = Serialize(output =>
		{
			WriteMessage(output, 1, messageAny);
			WriteMessage(output, 2047, extensionAny);
		});

		var publicKey = Serialize(output =>
			WriteBytes(output, 1, _publicKey));
		var publicKeyAny = CreateAny(_publicKeyTypeUrl, publicKey);
		var singleMode = Serialize(output => WriteEnum(output, 1, 1));
		var modeInfo = Serialize(output => WriteMessage(output, 1, singleMode));
		var signerInfo = Serialize(output =>
		{
			WriteMessage(output, 1, publicKeyAny);
			WriteMessage(output, 2, modeInfo);
			WriteUInt64(output, 3, sequence);
		});
		var fee = Serialize(output => WriteUInt64(output, 2, gasLimit));
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

	private static byte[] BuildOrderId(string address, uint subaccountNumber,
		uint clientId, DydxChainOrderFlags flags, uint clobPairId)
	{
		var subaccount = Serialize(output =>
		{
			WriteString(output, 1, address.NormalizeAddress());
			WriteUInt32(output, 2, subaccountNumber);
		});
		return Serialize(output =>
		{
			WriteMessage(output, 1, subaccount);
			WriteFixed32(output, 2, clientId);
			WriteUInt32(output, 3, (uint)flags);
			WriteUInt32(output, 4, clobPairId);
		});
	}

	private static byte[] BuildTwapParameters(
		DydxChainTwapParameters parameters)
	{
		if (parameters.Duration is < 300 or > 86400 ||
			parameters.Interval is < 30 or > 3600 ||
			parameters.Duration % parameters.Interval != 0 ||
			parameters.PriceTolerance >= 1_000_000)
			throw new ArgumentException(
				"Invalid dYdX TWAP duration, interval, or price tolerance.",
				nameof(parameters));
		return Serialize(output =>
		{
			WriteUInt32(output, 1, parameters.Duration);
			WriteUInt32(output, 2, parameters.Interval);
			WriteUInt32(output, 3, parameters.PriceTolerance);
		});
	}

	private void ValidateOwner(string address)
	{
		if (!address.NormalizeAddress().Equals(WalletAddress,
			StringComparison.Ordinal))
			throw new ArgumentException(
				"The dYdX transaction owner does not match the signing key.",
				nameof(address));
	}

	private static void ValidateExpiration(DydxChainOrderFlags flags,
		uint goodTilBlock, uint goodTilBlockTime)
	{
		var isShortTerm = flags == DydxChainOrderFlags.ShortTerm;
		if (isShortTerm && (goodTilBlock == 0 || goodTilBlockTime != 0) ||
			!isShortTerm && (goodTilBlock != 0 || goodTilBlockTime == 0))
			throw new ArgumentException(
				"dYdX short-term orders require a block expiration; stateful " +
				"orders require a UTC timestamp expiration.");
	}

	private static byte[] ReadMessageField(byte[] value, int field,
		string name)
	{
		using var input = new CodedInputStream(value);
		while (!input.IsAtEnd)
		{
			var tag = input.ReadTag();
			if (tag == 0)
				break;
			if (WireFormat.GetTagFieldNumber(tag) == field)
				return input.ReadBytes().ToByteArray();
			input.SkipLastField();
		}
		throw new InvalidDataException(
			$"dYdX {name} has no protobuf field {field}.");
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

	private static void WriteUInt32(CodedOutputStream output, int field,
		uint value)
	{
		if (value == 0)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteUInt32(value);
	}

	private static void WriteUInt64(CodedOutputStream output, int field,
		ulong value)
	{
		if (value == 0)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteUInt64(value);
	}

	private static void WriteFixed32(CodedOutputStream output, int field,
		uint value)
	{
		if (value == 0)
			return;
		output.WriteTag(field, WireFormat.WireType.Fixed32);
		output.WriteFixed32(value);
	}

	private static void WriteEnum(CodedOutputStream output, int field,
		int value)
	{
		if (value == 0)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteEnum(value);
	}

	private static void WriteBool(CodedOutputStream output, int field,
		bool value)
	{
		if (!value)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteBool(value);
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
