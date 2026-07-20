namespace StockSharp.Nado.Native;

using Nethereum.Signer;
using Nethereum.Util;

sealed class NadoSigner
{
	private static readonly byte[] _domainTypeHash = Keccak(
		"EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)");
	private static readonly byte[] _domainNameHash = Keccak("Nado");
	private static readonly byte[] _domainVersionHash = Keccak("0.0.1");
	private static readonly byte[] _orderTypeHash = Keccak(
		"Order(bytes32 sender,int128 priceX18,int128 amount,uint64 expiration,uint64 nonce,uint128 appendix)");
	private static readonly byte[] _cancelTypeHash = Keccak(
		"Cancellation(bytes32 sender,uint32[] productIds,bytes32[] digests,uint64 nonce)");
	private static readonly byte[] _cancelProductsTypeHash = Keccak(
		"CancellationProducts(bytes32 sender,uint32[] productIds,uint64 nonce)");

	private readonly EthECKey _key;
	private readonly byte[] _subaccount;

	public NadoSigner(string privateKey, string subaccountName)
	{
		privateKey = privateKey.ThrowIfEmpty(nameof(privateKey)).Trim();
		_key = new(privateKey);
		Address = _key.GetPublicAddress().ToLowerInvariant();
		_subaccount = CreateSubaccount(Address,
			subaccountName.ThrowIfEmpty(nameof(subaccountName)));
		Subaccount = ToHex(_subaccount);
	}

	public string Address { get; }
	public string Subaccount { get; }

	public static string CreateSubaccountHex(string address, string name)
		=> ToHex(CreateSubaccount(address, name));

	public string SignOrder(NadoSignedOrder order, int productId, long chainId)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		if (productId <= 0)
			throw new ArgumentOutOfRangeException(nameof(productId), productId,
				"Nado product ID must be positive.");
		ValidateSender(order.Sender);

		var structureHash = Keccak(Concat(
			_orderTypeHash,
			_subaccount,
			EncodeSigned(ParseBigInteger(order.Price, "order price"), 128),
			EncodeSigned(ParseBigInteger(order.Amount, "order amount"), 128),
			EncodeUnsigned(ParseBigInteger(order.Expiration, "order expiration"), 64),
			EncodeUnsigned(ParseBigInteger(order.Nonce, "order nonce"), 64),
			EncodeUnsigned(ParseBigInteger(order.Appendix, "order appendix"), 128)));
		return SignTypedData(structureHash, chainId,
			"0x" + productId.ToString("x40", CultureInfo.InvariantCulture));
	}

	public string SignCancellation(NadoCancelTransaction transaction,
		long chainId, string endpointAddress)
	{
		if (transaction is null)
			throw new ArgumentNullException(nameof(transaction));
		ValidateSender(transaction.Sender);
		if (transaction.ProductIds?.Length is not > 0 ||
			transaction.Digests?.Length != transaction.ProductIds.Length)
			throw new InvalidOperationException(
				"Nado cancellation products and digests must have equal non-zero lengths.");

		var productWords = transaction.ProductIds
			.Select(static id => EncodeUnsigned(id, 32)).ToArray();
		var digestWords = transaction.Digests.Select(ParseBytes32).ToArray();
		var structureHash = Keccak(Concat(
			_cancelTypeHash,
			_subaccount,
			Keccak(Concat(productWords)),
			Keccak(Concat(digestWords)),
			EncodeUnsigned(ParseBigInteger(transaction.Nonce,
				"cancellation nonce"), 64)));
		return SignTypedData(structureHash, chainId, endpointAddress);
	}

	public string SignProductCancellation(
		NadoCancelProductsTransaction transaction, long chainId,
		string endpointAddress)
	{
		if (transaction is null)
			throw new ArgumentNullException(nameof(transaction));
		ValidateSender(transaction.Sender);
		if (transaction.ProductIds?.Length is not > 0)
			throw new InvalidOperationException(
				"Nado product cancellation requires at least one product.");

		var productWords = transaction.ProductIds
			.Select(static id => EncodeUnsigned(id, 32)).ToArray();
		var structureHash = Keccak(Concat(
			_cancelProductsTypeHash,
			_subaccount,
			Keccak(Concat(productWords)),
			EncodeUnsigned(ParseBigInteger(transaction.Nonce,
				"cancellation nonce"), 64)));
		return SignTypedData(structureHash, chainId, endpointAddress);
	}

	public static string CreateOrderNonce()
	{
		var receiveTime = checked((ulong)(DateTime.UtcNow.AddSeconds(90) -
			DateTime.UnixEpoch).TotalMilliseconds);
		var random = (ulong)RandomNumberGenerator.GetInt32(1000);
		return checked((receiveTime << 20) + random).ToString(
			CultureInfo.InvariantCulture);
	}

	public static string PackAppendix(NadoOrderExecutionTypes executionType,
		bool isReduceOnly, bool isIsolated, decimal? isolatedMargin,
		int? builderId, int? builderFeeRate)
	{
		if (!Enum.IsDefined(executionType))
			throw new ArgumentOutOfRangeException(nameof(executionType),
				executionType, null);
		if (isIsolated && isolatedMargin is not > 0)
			throw new InvalidOperationException(
				"Nado isolated orders require a positive margin.");
		if (!isIsolated && isolatedMargin is not null)
			throw new InvalidOperationException(
				"Nado isolated margin requires isolated order mode.");
		if (builderId is < 0 or > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(builderId), builderId,
				"Nado builder ID must fit in 16 bits.");
		if (builderFeeRate is < 0 or > 1023)
			throw new ArgumentOutOfRangeException(nameof(builderFeeRate),
				builderFeeRate, "Nado builder fee rate must fit in 10 bits.");
		if (builderId is null && builderFeeRate is not null)
			throw new InvalidOperationException(
				"Nado builder fee rate requires a builder ID.");

		var value = isIsolated
			? new BigInteger(decimal.Truncate(isolatedMargin.Value * 1_000_000m))
			: BigInteger.Zero;
		if (value < 0 || value >= BigInteger.One << 64)
			throw new InvalidOperationException(
				"Nado isolated margin does not fit in the order appendix.");

		var execution = executionType switch
		{
			NadoOrderExecutionTypes.Default => 0,
			NadoOrderExecutionTypes.ImmediateOrCancel => 1,
			NadoOrderExecutionTypes.FillOrKill => 2,
			NadoOrderExecutionTypes.PostOnly => 3,
			_ => throw new ArgumentOutOfRangeException(nameof(executionType),
				executionType, null),
		};
		var packed = value;
		packed = packed << 16 | (builderId ?? 0);
		packed = packed << 10 | (builderFeeRate ?? 0);
		packed <<= 24;
		packed <<= 2;
		packed = packed << 1 | (isReduceOnly ? 1 : 0);
		packed = packed << 2 | execution;
		packed = packed << 1 | (isIsolated ? 1 : 0);
		packed = packed << 8 | 1;
		return packed.ToString(CultureInfo.InvariantCulture);
	}

	public static NadoOrderCondition UnpackAppendix(string appendix)
	{
		var packed = ParseBigInteger(appendix, "order appendix");
		if (packed < 0 || packed >= BigInteger.One << 128)
			throw new InvalidDataException("Nado order appendix is out of range.");
		var version = (int)(packed & 0xff);
		packed >>= 8;
		var isIsolated = !packed.IsEven;
		packed >>= 1;
		var execution = (int)(packed & 3);
		packed >>= 2;
		var isReduceOnly = !packed.IsEven;
		packed >>= 1;
		packed >>= 2;
		packed >>= 24;
		var builderFeeRate = (int)(packed & 1023);
		packed >>= 10;
		var builderId = (int)(packed & ushort.MaxValue);
		packed >>= 16;
		var margin = (decimal)(packed & ((BigInteger.One << 64) - 1)) /
			1_000_000m;
		if (version != 1)
			throw new InvalidDataException(
				"Unsupported Nado order appendix version " + version + ".");
		return new()
		{
			ExecutionType = (NadoOrderExecutionTypes)execution,
			IsReduceOnly = isReduceOnly,
			IsIsolated = isIsolated,
			IsolatedMargin = isIsolated ? margin : null,
			BuilderId = builderId == 0 ? null : builderId,
			BuilderFeeRate = builderId == 0 ? null : builderFeeRate,
		};
	}

	private string SignTypedData(byte[] structureHash, long chainId,
		string verifyingContract)
	{
		if (chainId <= 0)
			throw new ArgumentOutOfRangeException(nameof(chainId), chainId,
				"Nado chain ID must be positive.");
		var address = ParseAddress(verifyingContract);
		var addressWord = new byte[32];
		Buffer.BlockCopy(address, 0, addressWord, 12, address.Length);
		var domainSeparator = Keccak(Concat(
			_domainTypeHash,
			_domainNameHash,
			_domainVersionHash,
			EncodeUnsigned(chainId, 256),
			addressWord));
		var digest = Keccak(Concat([0x19, 0x01], domainSeparator,
			structureHash));
		var signature = _key.SignAndCalculateV(digest);
		var result = new byte[65];
		CopyScalar(signature.R, result, 0);
		CopyScalar(signature.S, result, 32);
		var recovery = signature.V is { Length: > 0 } ? signature.V[^1] : (byte)0;
		result[64] = recovery < 27 ? (byte)(recovery + 27) : recovery;
		return ToHex(result);
	}

	private void ValidateSender(string sender)
	{
		var bytes = ParseBytes32(sender);
		if (!bytes.SequenceEqual(_subaccount))
			throw new InvalidOperationException(
				"Nado transaction sender does not match the configured subaccount.");
	}

	private static byte[] CreateSubaccount(string address, string name)
	{
		var addressBytes = ParseAddress(address);
		byte[] nameBytes;
		if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			nameBytes = ParseHex(name);
		else
			nameBytes = Encoding.UTF8.GetBytes(name);
		if (nameBytes.Length > 12)
			throw new ArgumentOutOfRangeException(nameof(name), name,
				"Nado subaccount name must fit in 12 bytes.");
		var result = new byte[32];
		Buffer.BlockCopy(addressBytes, 0, result, 0, addressBytes.Length);
		Buffer.BlockCopy(nameBytes, 0, result, 20, nameBytes.Length);
		return result;
	}

	private static byte[] EncodeUnsigned(BigInteger value, int bits)
	{
		if (value < 0 || value >= BigInteger.One << bits)
			throw new InvalidOperationException(
				"Nado unsigned EIP-712 value does not fit in " + bits + " bits.");
		var raw = value.ToByteArray(true, true);
		var word = new byte[32];
		Buffer.BlockCopy(raw, 0, word, word.Length - raw.Length, raw.Length);
		return word;
	}

	private static byte[] EncodeSigned(BigInteger value, int bits)
	{
		var minimum = -(BigInteger.One << (bits - 1));
		var maximum = (BigInteger.One << (bits - 1)) - 1;
		if (value < minimum || value > maximum)
			throw new InvalidOperationException(
				"Nado signed EIP-712 value does not fit in " + bits + " bits.");
		if (value >= 0)
			return EncodeUnsigned(value, bits);
		var encoded = (BigInteger.One << 256) + value;
		return EncodeUnsigned(encoded, 256);
	}

	private static BigInteger ParseBigInteger(string value, string field)
	{
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException("Nado " + field + " is invalid.");
		return result;
	}

	private static byte[] ParseBytes32(string value)
	{
		var bytes = ParseHex(value);
		if (bytes.Length != 32)
			throw new InvalidDataException("Nado bytes32 value is invalid.");
		return bytes;
	}

	private static byte[] ParseAddress(string value)
	{
		var bytes = ParseHex(value);
		if (bytes.Length != 20)
			throw new InvalidDataException("Nado EVM address is invalid.");
		return bytes;
	}

	private static byte[] ParseHex(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length == 0 || value.Length % 2 != 0)
			throw new FormatException("Nado hexadecimal value is invalid.");
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
