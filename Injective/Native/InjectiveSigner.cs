namespace StockSharp.Injective.Native;

sealed class InjectiveSigner : IDisposable
{
	private const string _spotLimitType =
		"/injective.exchange.v2.MsgCreateSpotLimitOrder";
	private const string _spotMarketType =
		"/injective.exchange.v2.MsgCreateSpotMarketOrder";
	private const string _derivativeLimitType =
		"/injective.exchange.v2.MsgCreateDerivativeLimitOrder";
	private const string _derivativeMarketType =
		"/injective.exchange.v2.MsgCreateDerivativeMarketOrder";
	private const string _cancelSpotType =
		"/injective.exchange.v2.MsgCancelSpotOrder";
	private const string _cancelDerivativeType =
		"/injective.exchange.v2.MsgCancelDerivativeOrder";
	private const string _publicKeyType =
		"/injective.crypto.v1beta1.ethsecp256k1.PubKey";
	private const string _bech32Alphabet =
		"qpzry9x8gf2tvdw0s3jn54khce6mua7l";

	private readonly byte[] _privateKey;
	private readonly byte[] _publicKey;
	private readonly byte[] _addressBytes;
	private bool _isDisposed;

	public InjectiveSigner(string walletAddress, SecureString privateKey)
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
					"Injective private key must contain 32 hexadecimal bytes.",
					nameof(privateKey));
			_privateKey = keyText.HexToByteArray();
			var key = new EthECKey(_privateKey, true);
			_publicKey = key.GetPubKey(true);
			var uncompressed = key.GetPubKey(false);
			if (uncompressed is not { Length: 65 } || uncompressed[0] != 4)
				throw new CryptographicException(
					"Expected an uncompressed secp256k1 public key.");
			var hash = Sha3Keccack.Current.CalculateHash(uncompressed[1..]);
			_addressBytes = hash[^20..];
			var derived = EncodeAddress("inj", _addressBytes);
			if (!walletAddress.IsEmpty() &&
				!NormalizeAddress(walletAddress).Equals(derived,
					StringComparison.Ordinal))
				throw new ArgumentException(
					"The Injective wallet address does not match the private key.",
					nameof(walletAddress));
			WalletAddress = derived;
		}
		else if (!walletAddress.IsEmpty())
		{
			WalletAddress = NormalizeAddress(walletAddress);
			_addressBytes = DecodeAddress(WalletAddress).Data;
		}
	}

	public string WalletAddress { get; }
	public bool IsWalletAvailable => !WalletAddress.IsEmpty();
	public bool IsSigningAvailable => _privateKey is { Length: 32 };

	public string CreateSubaccountId(byte index)
	{
		if (_addressBytes is not { Length: 20 })
			throw new InvalidOperationException(
				"An Injective wallet address is required for a subaccount.");
		var result = new byte[32];
		Buffer.BlockCopy(_addressBytes, 0, result, 0, _addressBytes.Length);
		result[^1] = index;
		return "0x" + Convert.ToHexString(result).ToLowerInvariant();
	}

	public byte[] SignPlaceOrder(InjectivePlaceOrder order, string chainId,
		ulong accountNumber, ulong sequence, ulong gasLimit, string feeAmount,
		string feeDenom)
	{
		ArgumentNullException.ThrowIfNull(order);
		var market = order.Market ?? throw new ArgumentException(
			"Injective order market is required.", nameof(order));
		if (order.Price <= 0 || order.Quantity <= 0)
			throw new ArgumentException(
				"Injective order price and quantity must be positive.",
				nameof(order));
		if (order.IsPostOnly && order.IsMarket)
			throw new ArgumentException(
				"An Injective market order cannot be post-only.", nameof(order));
		if (market.Kind == InjectiveMarketKinds.Spot && order.IsReduceOnly)
			throw new ArgumentException(
				"Injective spot orders cannot be reduce-only.", nameof(order));
		if (market.Kind == InjectiveMarketKinds.Derivative &&
			!order.IsReduceOnly && order.Margin <= 0)
			throw new ArgumentException(
				"Injective derivative order margin must be positive.",
				nameof(order));

		var orderInfo = Serialize(output =>
		{
			WriteString(output, 1, CreateSubaccountId(CurrentSubaccount));
			WriteString(output, 2, WalletAddress);
			WriteString(output, 3, ToExtendedDecimal(order.Price));
			WriteString(output, 4, ToExtendedDecimal(order.Quantity));
			WriteString(output, 5, order.ClientId);
		});
		var orderType = GetOrderType(order);
		var trigger = order.TriggerPrice is decimal triggerPrice
			? ToExtendedDecimal(triggerPrice)
			: null;
		byte[] protocolOrder;
		string typeUrl;
		if (market.Kind == InjectiveMarketKinds.Spot)
		{
			protocolOrder = Serialize(output =>
			{
				WriteString(output, 1, market.MarketId);
				WriteMessage(output, 2, orderInfo);
				WriteEnum(output, 3, orderType);
				WriteString(output, 4, trigger);
				WriteInt64(output, 5, order.ExpirationBlock);
			});
			typeUrl = order.IsMarket ? _spotMarketType : _spotLimitType;
		}
		else
		{
			protocolOrder = Serialize(output =>
			{
				WriteString(output, 1, market.MarketId);
				WriteMessage(output, 2, orderInfo);
				WriteEnum(output, 3, orderType);
				WriteString(output, 4, ToExtendedDecimal(
					order.IsReduceOnly ? 0m : order.Margin));
				WriteString(output, 5, trigger);
				WriteInt64(output, 6, order.ExpirationBlock);
			});
			typeUrl = order.IsMarket
				? _derivativeMarketType : _derivativeLimitType;
		}
		var message = Serialize(output =>
		{
			WriteString(output, 1, WalletAddress);
			WriteMessage(output, 2, protocolOrder);
		});
		return SignMessage(typeUrl, message, chainId, accountNumber, sequence,
			gasLimit, feeAmount, feeDenom);
	}

	public byte[] SignCancelOrder(InjectiveCancelOrder order, string chainId,
		ulong accountNumber, ulong sequence, ulong gasLimit, string feeAmount,
		string feeDenom)
	{
		ArgumentNullException.ThrowIfNull(order);
		var market = order.Market ?? throw new ArgumentException(
			"Injective order market is required.", nameof(order));
		if (order.OrderHash.IsEmpty() && order.ClientId.IsEmpty())
			throw new ArgumentException(
				"Injective order hash or client ID is required.", nameof(order));
		var message = Serialize(output =>
		{
			WriteString(output, 1, WalletAddress);
			WriteString(output, 2, market.MarketId);
			WriteString(output, 3, CreateSubaccountId(CurrentSubaccount));
			WriteString(output, 4, order.OrderHash);
			if (market.Kind == InjectiveMarketKinds.Derivative)
				WriteInt32(output, 5, order.OrderMask);
			WriteString(output,
				market.Kind == InjectiveMarketKinds.Spot ? 5 : 6,
				order.ClientId);
		});
		return SignMessage(market.Kind == InjectiveMarketKinds.Spot
			? _cancelSpotType : _cancelDerivativeType, message, chainId,
			accountNumber, sequence, gasLimit, feeAmount, feeDenom);
	}

	public byte CurrentSubaccount { get; set; }

	private byte[] SignMessage(string typeUrl, byte[] message, string chainId,
		ulong accountNumber, ulong sequence, ulong gasLimit, string feeAmount,
		string feeDenom)
	{
		if (!IsSigningAvailable || _publicKey is not { Length: 33 })
			throw new InvalidOperationException(
				"An Injective private key is required to sign transactions.");
		chainId = chainId.ThrowIfEmpty(nameof(chainId));
		feeAmount = feeAmount.ThrowIfEmpty(nameof(feeAmount));
		feeDenom = feeDenom.ThrowIfEmpty(nameof(feeDenom));
		if (gasLimit == 0 || !BigInteger.TryParse(feeAmount,
			NumberStyles.None, CultureInfo.InvariantCulture, out var fee) || fee < 0)
			throw new ArgumentException("Invalid Injective transaction fee.",
				nameof(feeAmount));

		var body = Serialize(output =>
			WriteMessage(output, 1, CreateAny(typeUrl, message)));
		var publicKey = Serialize(output =>
			WriteBytes(output, 1, _publicKey));
		var singleMode = Serialize(output => WriteEnum(output, 1, 1));
		var modeInfo = Serialize(output => WriteMessage(output, 1, singleMode));
		var signerInfo = Serialize(output =>
		{
			WriteMessage(output, 1, CreateAny(_publicKeyType, publicKey));
			WriteMessage(output, 2, modeInfo);
			WriteUInt64(output, 3, sequence);
		});
		var coin = Serialize(output =>
		{
			WriteString(output, 1, feeDenom);
			WriteString(output, 2, feeAmount);
		});
		var feeMessage = Serialize(output =>
		{
			if (fee > 0)
				WriteMessage(output, 1, coin);
			WriteUInt64(output, 2, gasLimit);
		});
		var authInfo = Serialize(output =>
		{
			WriteMessage(output, 1, signerInfo);
			WriteMessage(output, 2, feeMessage);
		});
		var signDoc = Serialize(output =>
		{
			WriteBytes(output, 1, body);
			WriteBytes(output, 2, authInfo);
			WriteString(output, 3, chainId);
			WriteUInt64(output, 4, accountNumber);
		});
		var digest = Sha3Keccack.Current.CalculateHash(signDoc);
		var signature = new EthECKey(_privateKey, true).Sign(digest);
		var compact = new byte[64];
		CopyScalar(signature.R, compact.AsSpan(0, 32));
		CopyScalar(signature.S, compact.AsSpan(32, 32));
		return Serialize(output =>
		{
			WriteBytes(output, 1, body);
			WriteBytes(output, 2, authInfo);
			WriteBytes(output, 3, compact);
		});
	}

	private static int GetOrderType(InjectivePlaceOrder order)
	{
		if (order.TriggerPrice is not null)
			return order.IsTakeProfit
				? order.Side == Sides.Buy ? 5 : 6
				: order.Side == Sides.Buy ? 3 : 4;
		if (order.IsPostOnly)
			return order.Side == Sides.Buy ? 7 : 8;
		return order.Side == Sides.Buy ? 1 : 2;
	}

	private static string ToExtendedDecimal(decimal value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		var bits = decimal.GetBits(value);
		var mantissa = new BigInteger((uint)bits[0]) |
			new BigInteger((uint)bits[1]) << 32 |
			new BigInteger((uint)bits[2]) << 64;
		var scale = (bits[3] >> 16) & 0x7f;
		if (scale <= 18)
			mantissa *= BigInteger.Pow(10, 18 - scale);
		else
		{
			var divisor = BigInteger.Pow(10, scale - 18);
			var remainder = mantissa % divisor;
			if (remainder != 0)
				throw new ArgumentException(
					"Injective values cannot have more than 18 decimal places.",
					nameof(value));
			mantissa /= divisor;
		}
		return mantissa.ToString(CultureInfo.InvariantCulture);
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

	private static void WriteEnum(CodedOutputStream output, int field,
		int value)
	{
		if (value == 0)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteEnum(value);
	}

	private static void WriteInt32(CodedOutputStream output, int field,
		int value)
	{
		if (value == 0)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteInt32(value);
	}

	private static void WriteInt64(CodedOutputStream output, int field,
		long value)
	{
		if (value == 0)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteInt64(value);
	}

	private static void WriteUInt64(CodedOutputStream output, int field,
		ulong value)
	{
		if (value == 0)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteUInt64(value);
	}

	private static void CopyScalar(byte[] source, Span<byte> target)
	{
		if (source is null || source.Length == 0 || source.Length > target.Length)
			throw new CryptographicException(
				"The Injective signer returned an invalid scalar.");
		target.Clear();
		source.CopyTo(target[^source.Length..]);
	}

	private static string NormalizeAddress(string value)
	{
		var decoded = DecodeAddress(value);
		if (!decoded.Prefix.Equals("inj", StringComparison.Ordinal) ||
			decoded.Data.Length != 20)
			throw new FormatException("Invalid Injective wallet address.");
		return EncodeAddress(decoded.Prefix, decoded.Data);
	}

	private static (string Prefix, byte[] Data) DecodeAddress(string value)
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
		Buffer.BlockCopy(words, 0, checksumInput, expanded.Length, words.Length);
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
			0x3b6a57b2u, 0x26508e6du, 0x1ea119fau,
			0x3d4233ddu, 0x2a1462b3u,
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
			throw new FormatException("Invalid Bech32 padding.");
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
