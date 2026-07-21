namespace StockSharp.Synthetix.Native;

sealed class SynthetixSigner
{
	private const string _zeroAddress =
		"0x0000000000000000000000000000000000000000";

	private static readonly byte[] _domainTypeHash = Keccak(
		"EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)");
	private static readonly byte[] _authTypeHash = Keccak(
		"AuthMessage(uint256 subAccountId,uint256 timestamp,string action)");
	private static readonly byte[] _subAccountActionTypeHash = Keccak(
		"SubAccountAction(uint256 subAccountId,string action,uint256 expiresAfter)");
	private static readonly byte[] _orderTypeHash = Keccak(
		"Order(string symbol,string side,string orderType,string price," +
		"string triggerPrice,string quantity,bool reduceOnly," +
		"bool isTriggerMarket,string clientOrderId,bool closePosition)");
	private static readonly byte[] _placeOrdersTypeHash = Keccak(
		"PlaceOrders(uint256 subAccountId,Order[] orders,string grouping," +
		"uint256 nonce,uint256 expiresAfter)" +
		"Order(string symbol,string side,string orderType,string price," +
		"string triggerPrice,string quantity,bool reduceOnly," +
		"bool isTriggerMarket,string clientOrderId,bool closePosition)");
	private static readonly byte[] _cancelOrdersTypeHash = Keccak(
		"CancelOrders(uint256 subAccountId,uint256[] orderIds,uint256 nonce," +
		"uint256 expiresAfter)");
	private static readonly byte[] _cancelAllOrdersTypeHash = Keccak(
		"CancelAllOrders(uint256 subAccountId,string[] symbols,uint256 nonce," +
		"uint256 expiresAfter)");
	private static readonly byte[] _modifyOrderTypeHash = Keccak(
		"ModifyOrder(uint256 subAccountId,uint256 orderId,string price," +
		"string quantity,string triggerPrice,uint256 nonce," +
		"uint256 expiresAfter)");
	private static readonly byte[] _domainSeparator = Keccak(Concat(
		_domainTypeHash, Keccak("Synthetix"), Keccak("1"),
		EncodeUnsigned(BigInteger.One), EncodeAddress(_zeroAddress)));

	private readonly EthECKey _key;

	public SynthetixSigner(SecureString privateKey)
	{
		if (privateKey.IsEmpty())
			return;
		try
		{
			_key = new(privateKey.UnSecure().Trim());
		}
		catch (Exception error)
		{
			throw new ArgumentException(
				"Invalid Synthetix EVM private key.", nameof(privateKey), error);
		}
	}

	public bool IsAvailable => _key is not null;

	public string Address => _key?.GetPublicAddress().ToLowerInvariant();

	public SynthetixSocketAuthentication CreateAuthentication(
		string subAccountId, long timestampSeconds)
	{
		EnsureAvailable();
		var account = ParseUnsigned(subAccountId, "subaccount ID");
		if (timestampSeconds <= 0)
			throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
		var structureHash = Keccak(Concat(_authTypeHash,
			EncodeUnsigned(account), EncodeUnsigned(timestampSeconds),
			Keccak("websocket_auth")));
		var signature = Sign(structureHash);
		var typed = new SynthetixAuthTypedData
		{
			Types = new()
			{
				Domain =
				[
					new() { Name = "name", Type = "string" },
					new() { Name = "version", Type = "string" },
					new() { Name = "chainId", Type = "uint256" },
					new() { Name = "verifyingContract", Type = "address" },
				],
				AuthMessage =
				[
					new() { Name = "subAccountId", Type = "uint256" },
					new() { Name = "timestamp", Type = "uint256" },
					new() { Name = "action", Type = "string" },
				],
			},
			PrimaryType = "AuthMessage",
			Domain = new()
			{
				Name = "Synthetix",
				Version = "1",
				ChainId = 1,
				VerifyingContract = _zeroAddress,
			},
			Message = new()
			{
				SubAccountId = ToHex(account),
				Timestamp = ToHex(timestampSeconds),
				Action = "websocket_auth",
			},
		};
		return new()
		{
			Message = JsonConvert.SerializeObject(typed, Formatting.None,
				new JsonSerializerSettings
				{
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}),
			Signature = signature.Raw,
		};
	}

	public SynthetixSignature SignSubAccountAction(string subAccountId,
		string action, long expiresAfter)
		=> Sign(Keccak(Concat(_subAccountActionTypeHash,
			EncodeUnsigned(ParseUnsigned(subAccountId, "subaccount ID")),
			Keccak(action.ThrowIfEmpty(nameof(action))),
			EncodeUnsigned(expiresAfter))));

	public SynthetixSignature SignPlaceOrders(string subAccountId,
		SynthetixPlaceOrder[] orders, string grouping, long nonce,
		long expiresAfter)
	{
		if (orders is not { Length: > 0 })
			throw new ArgumentException(
				"At least one Synthetix order is required.", nameof(orders));
		var hashes = orders.Select(HashOrder).ToArray();
		return Sign(Keccak(Concat(_placeOrdersTypeHash,
			EncodeUnsigned(ParseUnsigned(subAccountId, "subaccount ID")),
			Keccak(Concat(hashes)),
			Keccak(grouping.ThrowIfEmpty(nameof(grouping))),
			EncodeUnsigned(nonce), EncodeUnsigned(expiresAfter))));
	}

	public SynthetixSignature SignCancelOrders(string subAccountId,
		string[] orderIds, long nonce, long expiresAfter)
	{
		if (orderIds is not { Length: > 0 })
			throw new ArgumentException(
				"At least one Synthetix order ID is required.", nameof(orderIds));
		return Sign(Keccak(Concat(_cancelOrdersTypeHash,
			EncodeUnsigned(ParseUnsigned(subAccountId, "subaccount ID")),
			Keccak(Concat([.. orderIds.Select(orderId => EncodeUnsigned(
				ParseUnsigned(orderId, "order ID")))])),
			EncodeUnsigned(nonce), EncodeUnsigned(expiresAfter))));
	}

	public SynthetixSignature SignCancelAllOrders(string subAccountId,
		string[] symbols, long nonce, long expiresAfter)
	{
		if (symbols is not { Length: > 0 })
			throw new ArgumentException(
				"At least one Synthetix symbol is required.", nameof(symbols));
		return Sign(Keccak(Concat(_cancelAllOrdersTypeHash,
			EncodeUnsigned(ParseUnsigned(subAccountId, "subaccount ID")),
			Keccak(Concat([.. symbols.Select(symbol =>
				Keccak(symbol.ThrowIfEmpty(nameof(symbol))))])),
			EncodeUnsigned(nonce), EncodeUnsigned(expiresAfter))));
	}

	public SynthetixSignature SignModifyOrder(string subAccountId,
		string orderId, string price, string quantity, string triggerPrice,
		long nonce, long expiresAfter)
		=> Sign(Keccak(Concat(_modifyOrderTypeHash,
			EncodeUnsigned(ParseUnsigned(subAccountId, "subaccount ID")),
			EncodeUnsigned(ParseUnsigned(orderId, "order ID")),
			Keccak(price ?? string.Empty), Keccak(quantity ?? string.Empty),
			Keccak(triggerPrice ?? string.Empty), EncodeUnsigned(nonce),
			EncodeUnsigned(expiresAfter))));

	private SynthetixSignature Sign(byte[] structureHash)
	{
		EnsureAvailable();
		var digest = Keccak(Concat([0x19, 0x01], _domainSeparator,
			structureHash));
		var signature = _key.SignAndCalculateV(digest);
		var recovery = signature.V is { Length: > 0 }
			? signature.V[^1]
			: (byte)27;
		if (recovery is 0 or 1)
			recovery += 27;
		if (recovery is not 27 and not 28)
			throw new CryptographicException(
				$"Synthetix signer returned invalid recovery ID '{recovery}'.");
		return new()
		{
			V = recovery,
			R = ToWordHex(signature.R, "R"),
			S = ToWordHex(signature.S, "S"),
		};
	}

	private static byte[] HashOrder(SynthetixPlaceOrder order)
	{
		ArgumentNullException.ThrowIfNull(order);
		return Keccak(Concat(_orderTypeHash,
			Keccak(order.Symbol ?? string.Empty),
			Keccak(order.Side ?? string.Empty),
			Keccak(order.OrderType ?? string.Empty),
			Keccak(order.Price ?? string.Empty),
			Keccak(order.TriggerPrice ?? string.Empty),
			Keccak(order.Quantity ?? string.Empty),
			EncodeUnsigned(order.IsReduceOnly ? 1 : 0),
			EncodeUnsigned(order.IsTriggerMarket ? 1 : 0),
			Keccak(order.ClientOrderId ?? string.Empty),
			EncodeUnsigned(order.IsClosePosition ? 1 : 0)));
	}

	private void EnsureAvailable()
	{
		if (!IsAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for Synthetix authentication.");
	}

	private static BigInteger ParseUnsigned(string value, string field)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		BigInteger result;
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			var raw = value[2..];
			if (raw.Length == 0 || raw.Any(static character =>
				!Uri.IsHexDigit(character)))
				throw new FormatException(
					$"Synthetix {field} is not an unsigned integer.");
			result = BigInteger.Parse("0" + raw,
				NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		}
		else if (!BigInteger.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out result))
			throw new FormatException(
				$"Synthetix {field} is not an unsigned integer.");
		if (result < 0 || result >= BigInteger.One << 256)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				$"Synthetix {field} does not fit in uint256.");
		return result;
	}

	private static byte[] EncodeUnsigned(long value)
		=> EncodeUnsigned(new BigInteger(value));

	private static byte[] EncodeUnsigned(BigInteger value)
	{
		if (value < 0 || value >= BigInteger.One << 256)
			throw new ArgumentOutOfRangeException(nameof(value));
		var raw = value.ToByteArray(true, true);
		var result = new byte[32];
		Buffer.BlockCopy(raw, 0, result, result.Length - raw.Length, raw.Length);
		return result;
	}

	private static byte[] EncodeAddress(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 40 || value.Any(static character =>
			!Uri.IsHexDigit(character)))
			throw new FormatException("Invalid Synthetix EIP-712 address.");
		var raw = Convert.FromHexString(value);
		var result = new byte[32];
		Buffer.BlockCopy(raw, 0, result, 12, raw.Length);
		return result;
	}

	private static string ToWordHex(byte[] value, string field)
	{
		if (value is not { Length: > 0 } || value.Length > 32)
			throw new CryptographicException(
				$"Synthetix signer returned invalid {field} scalar.");
		var result = new byte[32];
		Buffer.BlockCopy(value, 0, result, result.Length - value.Length,
			value.Length);
		return "0x" + Convert.ToHexString(result).ToLowerInvariant();
	}

	private static string ToHex(BigInteger value)
		=> "0x" + value.ToString("x", CultureInfo.InvariantCulture);

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
}
