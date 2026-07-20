namespace StockSharp.Reya.Native;

sealed class ReyaSigner
{
	private static readonly byte[] _domainTypeHash = Keccak(
		"EIP712Domain(string name,string version,address verifyingContract)");
	private static readonly byte[] _domainNameHash = Keccak("Reya");
	private static readonly byte[] _domainVersionHash = Keccak("1");
	private static readonly byte[] _conditionalOrderTypeHash = Keccak(
		"ConditionalOrder(uint256 verifyingChainId,uint256 deadline,ConditionalOrderDetails order)" +
		"ConditionalOrderDetails(uint128 accountId,uint128 marketId,uint128 exchangeId,uint128[] counterpartyAccountIds,uint8 orderType,bytes inputs,address signer,uint256 nonce)");
	private static readonly byte[] _conditionalOrderDetailsTypeHash = Keccak(
		"ConditionalOrderDetails(uint128 accountId,uint128 marketId,uint128 exchangeId,uint128[] counterpartyAccountIds,uint8 orderType,bytes inputs,address signer,uint256 nonce)");
	private static readonly byte[] _spotCancelTypeHash = Keccak(
		"OrderCancel(uint64 verifyingChainId,uint64 deadline,OrderCancelDetails cancel)" +
		"OrderCancelDetails(uint64 accountId,uint64 marketId,uint64 orderId,uint64 clOrdId,uint64 nonce)");
	private static readonly byte[] _spotCancelDetailsTypeHash = Keccak(
		"OrderCancelDetails(uint64 accountId,uint64 marketId,uint64 orderId,uint64 clOrdId,uint64 nonce)");
	private static readonly byte[] _massCancelTypeHash = Keccak(
		"MassCancel(uint64 verifyingChainId,uint64 deadline,MassCancelDetails massCancel)" +
		"MassCancelDetails(uint64 accountId,uint64 marketId,uint64 nonce)");
	private static readonly byte[] _massCancelDetailsTypeHash = Keccak(
		"MassCancelDetails(uint64 accountId,uint64 marketId,uint64 nonce)");

	private readonly EthECKey _key;
	private readonly byte[] _gatewayAddress;
	private readonly BigInteger _chainId;
	private readonly Lock _nonceSync = new();
	private long _lastOrderTimestamp;
	private long _lastSpotNonce;

	public ReyaSigner(string privateKey, long chainId, string gatewayAddress)
	{
		privateKey = privateKey.ThrowIfEmpty(nameof(privateKey)).Trim();
		if (chainId <= 0)
			throw new ArgumentOutOfRangeException(nameof(chainId), chainId,
				"Reya chain ID must be positive.");
		_key = new(privateKey);
		_chainId = chainId;
		_gatewayAddress = ParseAddress(gatewayAddress);
		Address = _key.GetPublicAddress().ToLowerInvariant();
	}

	public string Address { get; }

	public string EncodeLimitInputs(bool isBuy, decimal price, decimal quantity)
	{
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"Reya order price must be positive.");
		if (quantity <= 0)
			throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
				"Reya order quantity must be positive.");
		var signedQuantity = quantity.ToReyaDecimal().ToScaledInteger(18,
			"order quantity") * (isBuy ? BigInteger.One : -BigInteger.One);
		var scaledPrice = price.ToReyaDecimal().ToScaledInteger(18,
			"order price");
		return ToHex(Concat(
			EncodeSigned(signedQuantity, 256, "order quantity"),
			EncodeUnsigned(scaledPrice, 256, "order price")));
	}

	public string EncodeTriggerInputs(bool isBuy, decimal triggerPrice)
	{
		if (triggerPrice <= 0)
			throw new ArgumentOutOfRangeException(nameof(triggerPrice), triggerPrice,
				"Reya trigger price must be positive.");
		var scaledTrigger = triggerPrice.ToReyaDecimal().ToScaledInteger(18,
			"trigger price");
		var limitPrice = (isBuy ? "100000000000000000000" : "0")
			.ToScaledInteger(18, "trigger limit price");
		return ToHex(Concat(
			EncodeUnsigned(isBuy ? BigInteger.One : BigInteger.Zero, 8,
				"trigger side"),
			EncodeUnsigned(scaledTrigger, 256, "trigger price"),
			EncodeUnsigned(limitPrice, 256, "trigger limit price")));
	}

	public BigInteger CreateOrderNonce(BigInteger accountId, long marketId)
	{
		ValidateUnsigned(accountId, 128, "account ID");
		ValidateUnsigned(marketId, 32, "market ID");
		long timestamp;
		using (_nonceSync.EnterScope())
		{
			var current = DateTime.UtcNow.ToReyaMilliseconds();
			timestamp = current > _lastOrderTimestamp
				? current
				: checked(_lastOrderTimestamp + 1);
			_lastOrderTimestamp = timestamp;
		}
		ValidateUnsigned(timestamp, 64, "order timestamp");
		return accountId << 98 | (BigInteger)timestamp << 32 | marketId;
	}

	public long CreateSpotNonce()
	{
		using (_nonceSync.EnterScope())
		{
			var current = checked((DateTime.UtcNow - DateTime.UnixEpoch).Ticks / 10);
			_lastSpotNonce = current > _lastSpotNonce
				? current
				: checked(_lastSpotNonce + 1);
			return _lastSpotNonce;
		}
	}

	public string SignOrder(BigInteger accountId, long marketId, long exchangeId,
		BigInteger[] counterpartyAccountIds, ReyaGatewayOrderTypes orderType,
		string inputs, BigInteger deadline, BigInteger nonce)
	{
		ValidateUnsigned(accountId, 128, "account ID");
		ValidateUnsigned(marketId, 128, "market ID");
		ValidateUnsigned(exchangeId, 128, "exchange ID");
		ValidateUnsigned((int)orderType, 8, "order type");
		ValidateUnsigned(deadline, 256, "deadline");
		ValidateUnsigned(nonce, 256, "nonce");
		var counterparties = counterpartyAccountIds ?? [];
		foreach (var counterparty in counterparties)
			ValidateUnsigned(counterparty, 128, "counterparty account ID");

		var inputBytes = ParseHex(inputs);
		var counterpartiesHash = Keccak(Concat(counterparties.Select(
			counterparty => EncodeUnsigned(counterparty, 128,
				"counterparty account ID")).ToArray()));
		var detailsHash = Keccak(Concat(
			_conditionalOrderDetailsTypeHash,
			EncodeUnsigned(accountId, 128, "account ID"),
			EncodeUnsigned(marketId, 128, "market ID"),
			EncodeUnsigned(exchangeId, 128, "exchange ID"),
			counterpartiesHash,
			EncodeUnsigned((int)orderType, 8, "order type"),
			Keccak(inputBytes),
			EncodeAddress(ParseAddress(Address)),
			EncodeUnsigned(nonce, 256, "nonce")));
		var structureHash = Keccak(Concat(
			_conditionalOrderTypeHash,
			EncodeUnsigned(_chainId, 256, "chain ID"),
			EncodeUnsigned(deadline, 256, "deadline"),
			detailsHash));
		return SignTypedData(structureHash);
	}

	public string SignPerpetualCancellation(string orderId)
	{
		var message = JsonConvert.SerializeObject(
			new ReyaPerpetualCancelSigningMessage
			{
				OrderId = orderId.ThrowIfEmpty(nameof(orderId)).Trim(),
				Status = "cancelled",
				ActionType = "changeStatus",
			}, Formatting.None, new JsonSerializerSettings
			{
				Culture = CultureInfo.InvariantCulture,
				NullValueHandling = NullValueHandling.Ignore,
			});
		return new EthereumMessageSigner().EncodeUTF8AndSign(message, _key);
	}

	public string SignSpotCancellation(BigInteger accountId, long marketId,
		BigInteger orderId, long clientOrderId, long nonce, long deadline)
	{
		ValidateUnsigned(accountId, 64, "spot account ID");
		ValidateUnsigned(marketId, 64, "spot market ID");
		ValidateUnsigned(orderId, 64, "spot order ID");
		ValidateUnsigned(clientOrderId, 64, "client order ID");
		ValidateUnsigned(nonce, 64, "spot cancellation nonce");
		ValidateUnsigned(deadline, 64, "spot cancellation deadline");
		ValidateUnsigned(_chainId, 64, "chain ID");

		var detailsHash = Keccak(Concat(
			_spotCancelDetailsTypeHash,
			EncodeUnsigned(accountId, 64, "spot account ID"),
			EncodeUnsigned(marketId, 64, "spot market ID"),
			EncodeUnsigned(orderId, 64, "spot order ID"),
			EncodeUnsigned(clientOrderId, 64, "client order ID"),
			EncodeUnsigned(nonce, 64, "spot cancellation nonce")));
		var structureHash = Keccak(Concat(
			_spotCancelTypeHash,
			EncodeUnsigned(_chainId, 64, "chain ID"),
			EncodeUnsigned(deadline, 64, "spot cancellation deadline"),
			detailsHash));
		return SignTypedData(structureHash);
	}

	public string SignMassCancellation(BigInteger accountId, long marketId,
		long nonce, long deadline)
	{
		ValidateUnsigned(accountId, 64, "spot account ID");
		ValidateUnsigned(marketId, 64, "spot market ID");
		ValidateUnsigned(nonce, 64, "mass cancellation nonce");
		ValidateUnsigned(deadline, 64, "mass cancellation deadline");
		ValidateUnsigned(_chainId, 64, "chain ID");

		var detailsHash = Keccak(Concat(
			_massCancelDetailsTypeHash,
			EncodeUnsigned(accountId, 64, "spot account ID"),
			EncodeUnsigned(marketId, 64, "spot market ID"),
			EncodeUnsigned(nonce, 64, "mass cancellation nonce")));
		var structureHash = Keccak(Concat(
			_massCancelTypeHash,
			EncodeUnsigned(_chainId, 64, "chain ID"),
			EncodeUnsigned(deadline, 64, "mass cancellation deadline"),
			detailsHash));
		return SignTypedData(structureHash);
	}

	private string SignTypedData(byte[] structureHash)
	{
		var domainHash = Keccak(Concat(
			_domainTypeHash,
			_domainNameHash,
			_domainVersionHash,
			EncodeAddress(_gatewayAddress)));
		var digest = Keccak(Concat([0x19, 0x01], domainHash, structureHash));
		var signature = _key.SignAndCalculateV(digest);
		var result = new byte[65];
		CopyScalar(signature.R, result, 0);
		CopyScalar(signature.S, result, 32);
		var recovery = signature.V is { Length: > 0 } ? signature.V[^1] : (byte)0;
		result[64] = recovery < 27 ? (byte)(recovery + 27) : recovery;
		return ToHex(result);
	}

	private static byte[] EncodeAddress(byte[] value)
	{
		if (value?.Length != 20)
			throw new InvalidDataException("Reya EVM address is invalid.");
		var result = new byte[32];
		Buffer.BlockCopy(value, 0, result, 12, value.Length);
		return result;
	}

	private static byte[] EncodeUnsigned(BigInteger value, int bits, string field)
	{
		ValidateUnsigned(value, bits, field);
		var raw = value.ToByteArray(true, true);
		var result = new byte[32];
		Buffer.BlockCopy(raw, 0, result, result.Length - raw.Length, raw.Length);
		return result;
	}

	private static byte[] EncodeSigned(BigInteger value, int bits, string field)
	{
		var minimum = -(BigInteger.One << (bits - 1));
		var maximum = (BigInteger.One << (bits - 1)) - 1;
		if (value < minimum || value > maximum)
			throw new InvalidOperationException(
				"Reya " + field + " does not fit in " + bits + " signed bits.");
		return value >= 0
			? EncodeUnsigned(value, bits, field)
			: EncodeUnsigned((BigInteger.One << 256) + value, 256, field);
	}

	private static void ValidateUnsigned(BigInteger value, int bits, string field)
	{
		if (value < 0 || value >= BigInteger.One << bits)
			throw new InvalidOperationException(
				"Reya " + field + " does not fit in " + bits + " unsigned bits.");
	}

	private static byte[] ParseAddress(string value)
	{
		var result = ParseHex(value);
		if (result.Length != 20)
			throw new InvalidDataException("Reya EVM address is invalid.");
		return result;
	}

	private static byte[] ParseHex(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length == 0 || value.Length % 2 != 0)
			throw new FormatException("Reya hexadecimal value is invalid.");
		var result = new byte[value.Length / 2];
		for (var i = 0; i < result.Length; i++)
			result[i] = byte.Parse(value.AsSpan(i * 2, 2),
				NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		return result;
	}

	private static void CopyScalar(byte[] scalar, byte[] destination, int offset)
	{
		if (scalar?.Length is not > 0 || scalar.Length > 32)
			throw new CryptographicException("Invalid ECDSA signature scalar.");
		Buffer.BlockCopy(scalar, 0, destination, offset + 32 - scalar.Length,
			scalar.Length);
	}

	private static byte[] Keccak(string value)
		=> Keccak((value ?? string.Empty).UTF8());

	private static byte[] Keccak(byte[] value)
		=> Sha3Keccack.Current.CalculateHash(value ?? []);

	private static byte[] Concat(params byte[][] values)
	{
		var length = values?.Sum(static value => value?.Length ?? 0) ?? 0;
		var result = new byte[length];
		var offset = 0;
		foreach (var value in values ?? [])
		{
			if (value is null)
				continue;
			Buffer.BlockCopy(value, 0, result, offset, value.Length);
			offset += value.Length;
		}
		return result;
	}

	private static string ToHex(byte[] value)
		=> "0x" + Convert.ToHexString(value).ToLowerInvariant();
}
