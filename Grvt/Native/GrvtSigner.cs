namespace StockSharp.Grvt.Native;

sealed class GrvtSigner
{
	private static readonly byte[] _domainTypeHash = Keccak(
		"EIP712Domain(string name,string version,uint256 chainId)");
	private static readonly byte[] _orderTypeHash = Keccak(
		"Order(uint64 subAccountID,bool isMarket,uint8 timeInForce," +
		"bool postOnly,bool reduceOnly,OrderLeg[] legs,uint32 nonce," +
		"int64 expiration)OrderLeg(uint256 assetID,uint64 contractSize," +
		"uint64 limitPrice,bool isBuyingContract)");
	private static readonly byte[] _orderLegTypeHash = Keccak(
		"OrderLeg(uint256 assetID,uint64 contractSize,uint64 limitPrice," +
		"bool isBuyingContract)");
	private static readonly byte[] _domainNameHash = Keccak("GRVT Exchange");
	private static readonly byte[] _domainVersionHash = Keccak("0");

	private readonly EthECKey _key;
	private readonly int _chainId;
	private readonly byte[] _domainSeparator;

	public GrvtSigner(string privateKey, int chainId)
	{
		if (privateKey.IsEmpty())
			throw new ArgumentNullException(nameof(privateKey));
		if (chainId is not (325 or 326))
			throw new ArgumentOutOfRangeException(nameof(chainId), chainId,
				"GRVT supports production chain 325 and testnet chain 326.");
		try
		{
			_key = new(privateKey.Trim());
		}
		catch (Exception error)
		{
			throw new ArgumentException("Invalid EVM private key.",
				nameof(privateKey), error);
		}
		_chainId = chainId;
		_domainSeparator = Keccak(Concat(_domainTypeHash, _domainNameHash,
			_domainVersionHash, ToUInt256(chainId)));
	}

	public string Address => _key.GetPublicAddress().ToLowerInvariant();

	public GrvtSignature SignOrder(GrvtOrder order, GrvtInstrument instrument,
		DateTime expiration)
	{
		ArgumentNullException.ThrowIfNull(order);
		ArgumentNullException.ThrowIfNull(instrument);
		if (order.Legs is not { Length: 1 })
			throw new NotSupportedException(
				"The StockSharp GRVT adapter signs single-leg orders only.");
		if (!ulong.TryParse(order.SubAccountId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var subAccountId))
			throw new InvalidDataException(
				"GRVT subaccount ID is not an unsigned 64-bit integer.");
		if (!System.Enum.IsDefined(order.TimeInForce))
			throw new InvalidDataException("GRVT time-in-force is invalid.");
		if (expiration.Kind != DateTimeKind.Utc)
			expiration = expiration.ToUniversalTime();
		var expirationNanoseconds = expiration.ToUnixNanoseconds();
		if (expirationNanoseconds <= DateTime.UtcNow.ToUnixNanoseconds())
			throw new InvalidDataException(
				"GRVT signature expiration must be in the future.");

		var nonceBytes = new byte[sizeof(uint)];
		RandomNumberGenerator.Fill(nonceBytes);
		var nonce = BinaryPrimitives.ReadUInt32LittleEndian(nonceBytes);
		var signature = new GrvtSignature
		{
			Signer = Address,
			Expiration = expirationNanoseconds.ToString(CultureInfo.InvariantCulture),
			Nonce = nonce,
			ChainId = _chainId.ToString(CultureInfo.InvariantCulture),
		};

		var leg = order.Legs[0];
		if (!leg.Instrument.EqualsIgnoreCase(instrument.Instrument))
			throw new InvalidDataException(
				"GRVT signing instrument does not match the order leg.");
		var assetId = ParseAssetId(instrument.InstrumentHash);
		var contractSize = ScaleDecimal(leg.Size.ParseRequiredDecimal("size"),
			instrument.BaseDecimals, "order size");
		var limitPrice = order.IsMarket == true
			? 0UL
			: ScaleDecimal(leg.LimitPrice.ParseRequiredDecimal("limit price"),
				9, "limit price");
		var legHash = Keccak(Concat(_orderLegTypeHash, ToUInt256(assetId),
			ToUInt256(contractSize), ToUInt256(limitPrice),
			ToUInt256(leg.IsBuyingAsset ? 1 : 0)));
		var legsHash = Keccak(legHash);
		var orderHash = Keccak(Concat(_orderTypeHash, ToUInt256(subAccountId),
			ToUInt256(order.IsMarket == true ? 1 : 0),
			ToUInt256((int)order.TimeInForce),
			ToUInt256(order.IsPostOnly == true ? 1 : 0),
			ToUInt256(order.IsReduceOnly == true ? 1 : 0), legsHash,
			ToUInt256(nonce), ToUInt256(expirationNanoseconds)));
		var digest = Keccak(Concat([0x19, 0x01], _domainSeparator, orderHash));
		var signed = _key.SignAndCalculateV(digest);
		signature.R = ToScalarHex(signed.R);
		signature.S = ToScalarHex(signed.S);
		var v = signed.V is { Length: > 0 } ? signed.V[^1] : (byte)27;
		if (v is 0 or 1)
			v += 27;
		if (v is not 27 and not 28)
			throw new InvalidOperationException(
				$"EIP-712 signer returned invalid recovery id '{v}'.");
		signature.V = v;
		return signature;
	}

	private static BigInteger ParseAssetId(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.IsEmpty() || value.Length > 64 ||
			!value.All(Uri.IsHexDigit))
			throw new InvalidDataException("GRVT instrument hash is invalid.");
		return new BigInteger(value.HexToByteArray(), true, true);
	}

	private static ulong ScaleDecimal(decimal value, int decimals, string field)
	{
		if (value < 0 || decimals is < 0 or > 18)
			throw new InvalidDataException(
				$"GRVT {field} or its precision is invalid.");
		var factor = 1m;
		for (var i = 0; i < decimals; i++)
			factor *= 10m;
		var scaled = value * factor;
		if (scaled != decimal.Truncate(scaled) || scaled > ulong.MaxValue)
			throw new InvalidDataException(
				$"GRVT {field} cannot be represented exactly on chain.");
		return decimal.ToUInt64(scaled);
	}

	private static string ToScalarHex(byte[] source)
	{
		if (source is null || source.Length == 0 || source.Length > 32)
			throw new InvalidOperationException(
				"ECDSA signer returned an invalid scalar.");
		var result = new byte[32];
		Buffer.BlockCopy(source, 0, result, result.Length - source.Length,
			source.Length);
		return result.ToHex(true);
	}

	private static byte[] ToUInt256(BigInteger value)
	{
		if (value < 0 || value >= BigInteger.One << 256)
			throw new ArgumentOutOfRangeException(nameof(value));
		var bytes = value.ToByteArray(true, true);
		var result = new byte[32];
		Buffer.BlockCopy(bytes, 0, result, result.Length - bytes.Length,
			bytes.Length);
		return result;
	}

	private static byte[] Keccak(string value)
		=> Keccak((value ?? string.Empty).UTF8());

	private static byte[] Keccak(byte[] value)
		=> Sha3Keccack.Current.CalculateHash(value ?? []);

	private static byte[] Concat(params byte[][] chunks)
	{
		var result = new byte[(chunks ?? []).Sum(static chunk =>
			chunk?.Length ?? 0)];
		var offset = 0;
		foreach (var chunk in chunks ?? [])
		{
			if (chunk is null || chunk.Length == 0)
				continue;
			Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
			offset += chunk.Length;
		}
		return result;
	}
}
