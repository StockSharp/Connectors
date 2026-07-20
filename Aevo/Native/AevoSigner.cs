namespace StockSharp.Aevo.Native;

sealed class AevoSigner
{
	private static readonly byte[] _domainTypeHash = Keccak(
		"EIP712Domain(string name,string version,uint256 chainId)");
	private static readonly byte[] _orderTypeHash = Keccak(
		"Order(address maker,bool isBuy,uint256 limitPrice,uint256 amount," +
		"uint256 salt,uint256 instrument,uint256 timestamp)");
	private static readonly byte[] _versionHash = Keccak("1");

	private readonly EthECKey _key;
	private readonly byte[] _domainSeparator;

	public AevoSigner(SecureString privateKey, AevoEnvironments environment)
	{
		if (privateKey.IsEmpty())
			return;
		try
		{
			_key = new(privateKey.UnSecure().Trim());
		}
		catch (Exception error)
		{
			throw new ArgumentException("Invalid Aevo EVM signing key.",
				nameof(privateKey), error);
		}
		_domainSeparator = Keccak(Concat(_domainTypeHash,
			Keccak(environment.DomainName()), _versionHash,
			ToUInt256(environment.ChainId())));
	}

	public bool IsAvailable => _key is not null;

	public string SigningAddress => _key?.GetPublicAddress().ToLowerInvariant();

	public AevoOrderRequest CreateOrder(string maker, AevoInstrument instrument,
		Sides side, decimal price, decimal volume, OrderTypes orderType,
		TimeInForce? timeInForce, bool isPostOnly, bool isReduceOnly, bool isMmp)
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"An Aevo signing key is required for trading.");
		ArgumentNullException.ThrowIfNull(instrument);
		maker = maker.NormalizeAddress(nameof(maker));
		if (!long.TryParse(instrument.InstrumentId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var instrumentId) || instrumentId <= 0)
			throw new InvalidDataException("Aevo instrument ID is invalid.");
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		if (orderType == OrderTypes.Limit && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price));
		if (orderType == OrderTypes.Market && isPostOnly)
			throw new InvalidOperationException(
				"An Aevo market order cannot be post-only.");

		var wireAmount = BigInteger.Parse(volume.ToWire(nameof(volume)),
			CultureInfo.InvariantCulture);
		var wirePrice = orderType == OrderTypes.Market
			? side == Sides.Buy ? (BigInteger.One << 256) - 1 : BigInteger.Zero
			: BigInteger.Parse(price.ToWire(nameof(price)),
				CultureInfo.InvariantCulture);
		Span<byte> saltBytes = stackalloc byte[16];
		RandomNumberGenerator.Fill(saltBytes);
		var salt = new BigInteger(saltBytes, true, true);
		var timestamp = checked((long)(DateTime.UtcNow - DateTime.UnixEpoch)
			.TotalSeconds);
		var structureHash = Keccak(Concat(_orderTypeHash,
			ToAddressWord(maker), ToUInt256(side == Sides.Buy ? 1 : 0),
			ToUInt256(wirePrice), ToUInt256(wireAmount), ToUInt256(salt),
			ToUInt256(instrumentId), ToUInt256(timestamp)));
		var digest = Keccak(Concat([0x19, 0x01], _domainSeparator,
			structureHash));
		return new()
		{
			Instrument = instrumentId,
			Maker = maker,
			IsBuy = side == Sides.Buy,
			Amount = wireAmount.ToString(CultureInfo.InvariantCulture),
			LimitPrice = wirePrice.ToString(CultureInfo.InvariantCulture),
			Salt = salt.ToString(CultureInfo.InvariantCulture),
			Timestamp = timestamp,
			Signature = SignDigest(digest),
			IsPostOnly = isPostOnly,
			IsReduceOnly = isReduceOnly,
			IsMmp = isMmp,
			TimeInForce = timeInForce == TimeInForce.CancelBalance
				? "IOC"
				: "GTC",
		};
	}

	private string SignDigest(byte[] digest)
	{
		var signature = _key.SignAndCalculateV(digest);
		var result = new byte[65];
		CopyScalar(signature.R, result.AsSpan(0, 32));
		CopyScalar(signature.S, result.AsSpan(32, 32));
		var recovery = signature.V is { Length: > 0 }
			? signature.V[^1]
			: (byte)27;
		if (recovery is 0 or 1)
			recovery += 27;
		if (recovery is not 27 and not 28)
			throw new InvalidOperationException(
				$"Aevo signer returned invalid recovery ID '{recovery}'.");
		result[64] = recovery;
		return result.ToHex(true);
	}

	private static byte[] ToAddressWord(string address)
	{
		var result = new byte[32];
		var bytes = address.NormalizeAddress(nameof(address))[2..]
			.HexToByteArray();
		Buffer.BlockCopy(bytes, 0, result, 12, bytes.Length);
		return result;
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

	private static void CopyScalar(byte[] source, Span<byte> target)
	{
		if (source is null || source.Length == 0 || source.Length > target.Length)
			throw new InvalidOperationException(
				"Aevo signer returned an invalid scalar.");
		target.Clear();
		source.CopyTo(target[^source.Length..]);
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
