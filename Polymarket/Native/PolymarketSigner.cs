namespace StockSharp.Polymarket.Native;

sealed class PolymarketSigner
{
	private const long _chainId = 137;
	private const string _domainName = "Polymarket CTF Exchange";
	private const string _zeroBytes32 =
		"0x0000000000000000000000000000000000000000000000000000000000000000";
	private const string _exchangeV2 =
		"0xE111180000d2663C0091e4f400237545B87B996B";
	private const string _negativeRiskExchangeV2 =
		"0xe2222d279d744050d28e00520010520000310F59";
	private const string _exchangeV3 =
		"0xe3333700cA9d93003F00f0F71f8515005F6c00Aa";
	private const string _orderType =
		"Order(uint256 salt,address maker,address signer,uint256 tokenId," +
		"uint256 makerAmount,uint256 takerAmount,uint8 side," +
		"uint8 signatureType,uint256 timestamp,bytes32 metadata,bytes32 builder)";
	private const string _typedDataSignType =
		"TypedDataSign(Order contents,string name,string version,uint256 chainId," +
		"address verifyingContract,bytes32 salt)" + _orderType;

	private static readonly byte[] _domainTypeHash = Keccak(
		"EIP712Domain(string name,string version,uint256 chainId," +
		"address verifyingContract)");
	private static readonly byte[] _orderTypeHash = Keccak(_orderType);
	private static readonly byte[] _typedDataSignTypeHash =
		Keccak(_typedDataSignType);
	private static readonly byte[] _domainNameHash = Keccak(_domainName);
	private static readonly byte[] _depositWalletNameHash =
		Keccak("DepositWallet");
	private static readonly byte[] _depositWalletVersionHash = Keccak("1");

	private readonly EthECKey _key;
	private readonly string _funderAddress;
	private readonly PolymarketSignatureTypes _signatureType;
	private readonly string _builder;

	public PolymarketSigner(SecureString privateKey, string funderAddress,
		PolymarketSignatureTypes signatureType, string builder)
	{
		if (!Enum.IsDefined(signatureType))
			throw new ArgumentOutOfRangeException(nameof(signatureType),
				signatureType, "Unsupported Polymarket signature type.");
		_signatureType = signatureType;
		_builder = builder.IsEmpty()
			? _zeroBytes32
			: builder.NormalizeBytes32(nameof(builder));
		if (!privateKey.IsEmpty())
		{
			try
			{
				_key = new(privateKey.UnSecure().Trim());
			}
			catch (Exception error)
			{
				throw new ArgumentException(
					"Invalid Polymarket EVM private key.",
					nameof(privateKey), error);
			}
		}
		var signer = _key?.GetPublicAddress().NormalizeAddress("privateKey");
		if (_signatureType != PolymarketSignatureTypes.Eoa &&
			funderAddress.IsEmpty())
			throw new ArgumentException(
				"A funder address is required for Polymarket proxy, Safe and " +
				"deposit wallets.", nameof(funderAddress));
		_funderAddress = funderAddress.IsEmpty()
			? signer
			: funderAddress.NormalizeAddress(nameof(funderAddress));
		if (_key is not null && _signatureType == PolymarketSignatureTypes.Eoa &&
			!_funderAddress.IsEmpty() && !_funderAddress.Equals(signer,
				StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				"An EOA Polymarket funder address must match the private key.",
				nameof(funderAddress));
	}

	public bool IsAvailable => _key is not null;

	public string SignerAddress => _key?.GetPublicAddress()
		.NormalizeAddress("privateKey");

	public string FunderAddress => _funderAddress;

	public PolymarketOrderRequest CreateOrder(int version,
		PolymarketMarket market, Sides side, decimal price, decimal volume,
		PolymarketOrderTypes orderType, DateTime? expiryDate, bool isPostOnly,
		string owner)
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"A Polymarket EVM private key is required for trading.");
		ArgumentNullException.ThrowIfNull(market);
		if (version is not (2 or 3))
			throw new NotSupportedException(
				$"Polymarket order API version {version} is unsupported.");
		if (owner.IsEmpty())
			throw new ArgumentNullException(nameof(owner));
		if (price <= 0 || price >= 1 || market.PriceStep <= 0 ||
			price % market.PriceStep != 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				$"Polymarket price must be a multiple of {market.PriceStep} " +
				"strictly between zero and one.");
		if (volume <= 0 || volume != RoundDown(volume, 2))
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				"Polymarket order volume must be positive with no more than " +
				"two decimals.");
		if (volume < market.MinimumVolume)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"Polymarket order volume must be at least {market.MinimumVolume}.");
		if (isPostOnly && orderType is PolymarketOrderTypes.FillOrKill or
			PolymarketOrderTypes.FillAndKill)
			throw new InvalidOperationException(
				"Polymarket immediate orders cannot be post-only.");

		var amountDecimals = GetAmountDecimals(market.PriceStep);
		var maker = _funderAddress.NormalizeAddress(nameof(FunderAddress));
		var signer = _signatureType == PolymarketSignatureTypes.Poly1271
			? maker
			: SignerAddress;
		var nativeSide = side.ToPolymarket();
		var shares = RoundDown(volume, 2);
		var value = NormalizeAmount(shares * price, amountDecimals);
		var makerValue = nativeSide == PolymarketSides.Buy ? value : shares;
		var takerValue = nativeSide == PolymarketSides.Buy ? shares : value;
		var signed = new PolymarketSignedOrder
		{
			Salt = CreateSalt(),
			Maker = maker,
			Signer = signer,
			TokenId = market.TokenId,
			MakerAmount = makerValue.ToWire(nameof(makerValue)),
			TakerAmount = takerValue.ToWire(nameof(takerValue)),
			Side = nativeSide,
			SignatureType = _signatureType,
			Timestamp = DateTime.UtcNow.ToPolymarketMilliseconds().ToString(
				CultureInfo.InvariantCulture),
			Expiration = (expiryDate?.ToPolymarketSeconds() ?? 0).ToString(
				CultureInfo.InvariantCulture),
			Metadata = _zeroBytes32,
			Builder = _builder,
		};
		signed = new()
		{
			Salt = signed.Salt,
			Maker = signed.Maker,
			Signer = signed.Signer,
			TokenId = signed.TokenId,
			MakerAmount = signed.MakerAmount,
			TakerAmount = signed.TakerAmount,
			Side = signed.Side,
			SignatureType = signed.SignatureType,
			Timestamp = signed.Timestamp,
			Expiration = signed.Expiration,
			Metadata = signed.Metadata,
			Builder = signed.Builder,
			Signature = SignOrder(signed, version, market.IsNegativeRisk),
		};
		return new()
		{
			Order = signed,
			Owner = owner,
			OrderType = orderType,
			IsExecutionDeferred = false,
			IsPostOnly = isPostOnly,
		};
	}

	private string SignOrder(PolymarketSignedOrder order, int version,
		bool isNegativeRisk)
	{
		var contract = version == 3
			? _exchangeV3
			: isNegativeRisk ? _negativeRiskExchangeV2 : _exchangeV2;
		var domain = DomainSeparator(version, contract);
		var contents = Keccak(Concat(
			_orderTypeHash,
			ToUInt256(order.Salt),
			ToAddressWord(order.Maker),
			ToAddressWord(order.Signer),
			ToUInt256(ParseInteger(order.TokenId, "token ID")),
			ToUInt256(ParseInteger(order.MakerAmount, "maker amount")),
			ToUInt256(ParseInteger(order.TakerAmount, "taker amount")),
			ToUInt256(order.Side == PolymarketSides.Buy ? 0 : 1),
			ToUInt256((int)order.SignatureType),
			ToUInt256(ParseInteger(order.Timestamp, "timestamp")),
			ParseBytes32(order.Metadata, "metadata"),
			ParseBytes32(order.Builder, "builder")));
		if (_signatureType != PolymarketSignatureTypes.Poly1271)
			return SignDigest(Keccak(Concat([0x19, 0x01], domain, contents)))
				.ToHex(true);

		var innerHash = Keccak(Concat(
			_typedDataSignTypeHash,
			contents,
			_depositWalletNameHash,
			_depositWalletVersionHash,
			ToUInt256(_chainId),
			ToAddressWord(order.Signer),
			new byte[32]));
		var innerSignature = SignDigest(Keccak(Concat(
			[0x19, 0x01], domain, innerHash)));
		var typeBytes = Encoding.UTF8.GetBytes(_orderType);
		if (typeBytes.Length > ushort.MaxValue)
			throw new InvalidOperationException(
				"Polymarket ERC-7739 content type is too long.");
		var length = new byte[2];
		BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)typeBytes.Length);
		return Concat(innerSignature, domain, contents, typeBytes, length)
			.ToHex(true);
	}

	private static byte[] DomainSeparator(int version, string contract)
		=> Keccak(Concat(
			_domainTypeHash,
			_domainNameHash,
			Keccak(version.ToString(CultureInfo.InvariantCulture)),
			ToUInt256(_chainId),
			ToAddressWord(contract)));

	private byte[] SignDigest(byte[] digest)
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
				$"Polymarket signer returned invalid recovery ID '{recovery}'.");
		result[64] = recovery;
		return result;
	}

	private static int GetAmountDecimals(decimal tickSize)
		=> tickSize switch
		{
			0.1m => 3,
			0.01m => 4,
			0.005m => 5,
			0.0025m => 6,
			0.001m => 5,
			0.0001m => 6,
			_ => throw new NotSupportedException(
				$"Polymarket tick size {tickSize} is unsupported."),
		};

	private static decimal NormalizeAmount(decimal value, int decimals)
	{
		if (value == RoundDown(value, decimals))
			return value;
		value = RoundUp(value, decimals + 4);
		return value == RoundDown(value, decimals)
			? value
			: RoundDown(value, decimals);
	}

	private static decimal RoundDown(decimal value, int decimals)
	{
		var factor = Pow10(decimals);
		return decimal.Floor(value * factor) / factor;
	}

	private static decimal RoundUp(decimal value, int decimals)
	{
		var factor = Pow10(decimals);
		return decimal.Ceiling(value * factor) / factor;
	}

	private static decimal Pow10(int exponent)
	{
		var result = 1m;
		for (var index = 0; index < exponent; index++)
			result *= 10m;
		return result;
	}

	private static ulong CreateSalt()
	{
		Span<byte> bytes = stackalloc byte[8];
		RandomNumberGenerator.Fill(bytes);
		var value = BinaryPrimitives.ReadUInt64BigEndian(bytes) &
			((1UL << 53) - 1);
		return value == 0 ? 1UL : value;
	}

	private static BigInteger ParseInteger(string value, string field)
	{
		if (!BigInteger.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result) || result < 0)
			throw new InvalidDataException(
				$"Polymarket {field} '{value}' is invalid.");
		return result;
	}

	private static byte[] ParseBytes32(string value, string field)
		=> value.NormalizeBytes32(field)[2..].HexToByteArray();

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
				"Polymarket signer returned an invalid scalar.");
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
