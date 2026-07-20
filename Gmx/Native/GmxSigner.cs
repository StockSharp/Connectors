namespace StockSharp.Gmx.Native;

sealed class GmxSigner
{
	private const string _zeroAddress =
		"0x0000000000000000000000000000000000000000";
	private const string _zeroHash =
		"0x0000000000000000000000000000000000000000000000000000000000000000";

	private static readonly byte[] _domainTypeHash = Keccak(
		"EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)");
	private static readonly byte[] _batchTypeHash = Keccak(
		"Batch(address account,CreateOrderParams[] createOrderParamsList," +
		"UpdateOrderParams[] updateOrderParamsList,bytes32[] cancelOrderKeys," +
		"bytes32 relayParams,bytes32 subaccountApproval)" +
		"CreateOrderAddresses(address receiver,address cancellationReceiver," +
		"address callbackContract,address uiFeeReceiver,address market," +
		"address initialCollateralToken,address[] swapPath)" +
		"CreateOrderNumbers(uint256 sizeDeltaUsd,uint256 initialCollateralDeltaAmount," +
		"uint256 triggerPrice,uint256 acceptablePrice,uint256 executionFee," +
		"uint256 callbackGasLimit,uint256 minOutputAmount,uint256 validFromTime)" +
		"CreateOrderParams(CreateOrderAddresses addresses,CreateOrderNumbers numbers," +
		"uint256 orderType,uint256 decreasePositionSwapType,bool isLong," +
		"bool shouldUnwrapNativeToken,bool autoCancel,bytes32 referralCode," +
		"bytes32[] dataList)" +
		"UpdateOrderParams(bytes32 key,uint256 sizeDeltaUsd,uint256 acceptablePrice," +
		"uint256 triggerPrice,uint256 minOutputAmount,uint256 validFromTime," +
		"bool autoCancel,uint256 executionFeeIncrease)");
	private static readonly byte[] _createOrderTypeHash = Keccak(
		"CreateOrderParams(CreateOrderAddresses addresses,CreateOrderNumbers numbers," +
		"uint256 orderType,uint256 decreasePositionSwapType,bool isLong," +
		"bool shouldUnwrapNativeToken,bool autoCancel,bytes32 referralCode," +
		"bytes32[] dataList)" +
		"CreateOrderAddresses(address receiver,address cancellationReceiver," +
		"address callbackContract,address uiFeeReceiver,address market," +
		"address initialCollateralToken,address[] swapPath)" +
		"CreateOrderNumbers(uint256 sizeDeltaUsd,uint256 initialCollateralDeltaAmount," +
		"uint256 triggerPrice,uint256 acceptablePrice,uint256 executionFee," +
		"uint256 callbackGasLimit,uint256 minOutputAmount,uint256 validFromTime)");
	private static readonly byte[] _createOrderAddressesTypeHash = Keccak(
		"CreateOrderAddresses(address receiver,address cancellationReceiver," +
		"address callbackContract,address uiFeeReceiver,address market," +
		"address initialCollateralToken,address[] swapPath)");
	private static readonly byte[] _createOrderNumbersTypeHash = Keccak(
		"CreateOrderNumbers(uint256 sizeDeltaUsd,uint256 initialCollateralDeltaAmount," +
		"uint256 triggerPrice,uint256 acceptablePrice,uint256 executionFee," +
		"uint256 callbackGasLimit,uint256 minOutputAmount,uint256 validFromTime)");
	private static readonly byte[] _updateOrderTypeHash = Keccak(
		"UpdateOrderParams(bytes32 key,uint256 sizeDeltaUsd,uint256 acceptablePrice," +
		"uint256 triggerPrice,uint256 minOutputAmount,uint256 validFromTime," +
		"bool autoCancel,uint256 executionFeeIncrease)");

	private static readonly (string Name, string Type)[] _batchFields =
	[
		("account", "address"),
		("createOrderParamsList", "CreateOrderParams[]"),
		("updateOrderParamsList", "UpdateOrderParams[]"),
		("cancelOrderKeys", "bytes32[]"),
		("relayParams", "bytes32"),
		("subaccountApproval", "bytes32"),
	];
	private static readonly (string Name, string Type)[] _createFields =
	[
		("addresses", "CreateOrderAddresses"),
		("numbers", "CreateOrderNumbers"),
		("orderType", "uint256"),
		("decreasePositionSwapType", "uint256"),
		("isLong", "bool"),
		("shouldUnwrapNativeToken", "bool"),
		("autoCancel", "bool"),
		("referralCode", "bytes32"),
		("dataList", "bytes32[]"),
	];
	private static readonly (string Name, string Type)[] _addressFields =
	[
		("receiver", "address"),
		("cancellationReceiver", "address"),
		("callbackContract", "address"),
		("uiFeeReceiver", "address"),
		("market", "address"),
		("initialCollateralToken", "address"),
		("swapPath", "address[]"),
	];
	private static readonly (string Name, string Type)[] _numberFields =
	[
		("sizeDeltaUsd", "uint256"),
		("initialCollateralDeltaAmount", "uint256"),
		("triggerPrice", "uint256"),
		("acceptablePrice", "uint256"),
		("executionFee", "uint256"),
		("callbackGasLimit", "uint256"),
		("minOutputAmount", "uint256"),
		("validFromTime", "uint256"),
	];
	private static readonly (string Name, string Type)[] _updateFields =
	[
		("key", "bytes32"),
		("sizeDeltaUsd", "uint256"),
		("acceptablePrice", "uint256"),
		("triggerPrice", "uint256"),
		("minOutputAmount", "uint256"),
		("validFromTime", "uint256"),
		("autoCancel", "bool"),
		("executionFeeIncrease", "uint256"),
	];

	private readonly EthECKey _key;

	public GmxSigner(string privateKey)
	{
		privateKey = privateKey.ThrowIfEmpty(nameof(privateKey)).Trim();
		_key = new(privateKey);
		Address = _key.GetPublicAddress().NormalizeGmxAddress("signer address");
	}

	public string Address { get; }

	public string Sign(GmxPrepareOrderResponse prepared, GmxNetworks network)
	{
		if (prepared?.Payload?.TypedData?.Domain is null ||
			prepared.Payload.TypedData.Types is null ||
			prepared.Payload.TypedData.Message is null)
			throw new InvalidDataException(
				"GMX prepare response contains no EIP-712 payload.");
		if (!string.Equals(prepared.PayloadType, "typed-data",
			StringComparison.Ordinal) ||
			!string.Equals(prepared.Mode, "express", StringComparison.Ordinal))
			throw new InvalidDataException(
				"GMX returned an unsupported transaction payload.");

		var typed = prepared.Payload.TypedData;
		ValidateDomain(typed.Domain, prepared.Payload.RelayRouterAddress, network);
		ValidateTypes(typed.Types);
		ValidateMessage(typed.Message);

		var domainHash = Keccak(Concat(
			_domainTypeHash,
			Keccak(typed.Domain.Name),
			Keccak(typed.Domain.Version),
			EncodeUnsigned(typed.Domain.ChainId, "chain ID"),
			EncodeAddress(typed.Domain.VerifyingContract)));
		var messageHash = HashBatch(typed.Message);
		var digest = Keccak(Concat([0x19, 0x01], domainHash, messageHash));
		var signature = _key.SignAndCalculateV(digest);
		var result = new byte[65];
		CopyScalar(signature.R, result, 0);
		CopyScalar(signature.S, result, 32);
		var recovery = signature.V is { Length: > 0 } ? signature.V[^1] : (byte)0;
		result[64] = recovery < 27 ? (byte)(recovery + 27) : recovery;
		return ToHex(result);
	}

	private void ValidateMessage(GmxTypedDataMessage message)
	{
		if (!string.Equals(message.Account, _zeroAddress,
			StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(message.SubaccountApproval, _zeroHash,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"GMX subaccount typed data is not supported by this connector.");
		foreach (var order in message.CreateOrderParameters ?? [])
		{
			var addresses = order?.Addresses ?? throw new InvalidDataException(
				"GMX typed order contains no addresses.");
			var receiver = addresses.Receiver.NormalizeGmxAddress("receiver");
			if (!receiver.Equals(Address, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"GMX typed order receiver does not match the signer.");
			var cancellationReceiver = addresses.CancellationReceiver
				.NormalizeGmxAddress("cancellation receiver");
			if (!cancellationReceiver.Equals(_zeroAddress,
				StringComparison.OrdinalIgnoreCase) &&
				!cancellationReceiver.Equals(Address,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"GMX cancellation receiver does not match the signer.");
		}
	}

	private static void ValidateDomain(GmxTypedDataDomain domain,
		string payloadRouter, GmxNetworks network)
	{
		if (!string.Equals(domain.Name, "GmxBaseGelatoRelayRouter",
			StringComparison.Ordinal) ||
			!string.Equals(domain.Version, "1", StringComparison.Ordinal) ||
			domain.ChainId != network.ChainId())
			throw new InvalidDataException("GMX EIP-712 domain is invalid.");
		var expected = network.RelayRouter();
		var verifying = domain.VerifyingContract.NormalizeGmxAddress(
			"verifying contract");
		var returned = payloadRouter.NormalizeGmxAddress("relay router");
		if (!verifying.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
			!returned.Equals(expected, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"GMX prepare response references an unknown relay router.");
	}

	private static void ValidateTypes(GmxTypedDataTypes types)
	{
		ValidateType("Batch", types.Batch, _batchFields);
		ValidateType("CreateOrderParams", types.CreateOrderParameters,
			_createFields);
		ValidateType("CreateOrderAddresses", types.CreateOrderAddresses,
			_addressFields);
		ValidateType("CreateOrderNumbers", types.CreateOrderNumbers,
			_numberFields);
		ValidateType("UpdateOrderParams", types.UpdateOrderParameters,
			_updateFields);
	}

	private static void ValidateType(string name, GmxTypedDataField[] actual,
		(string Name, string Type)[] expected)
	{
		if (actual?.Length != expected.Length)
			throw new InvalidDataException("GMX EIP-712 type '" + name +
				"' has an unexpected shape.");
		for (var i = 0; i < expected.Length; i++)
			if (!string.Equals(actual[i]?.Name, expected[i].Name,
				StringComparison.Ordinal) ||
				!string.Equals(actual[i]?.Type, expected[i].Type,
					StringComparison.Ordinal))
				throw new InvalidDataException("GMX EIP-712 type '" + name +
					"' has an unexpected field.");
	}

	private static byte[] HashBatch(GmxTypedDataMessage message)
		=> Keccak(Concat(
			_batchTypeHash,
			EncodeAddress(message.Account),
			HashArray((message.CreateOrderParameters ?? [])
				.Select(HashCreateOrder)),
			HashArray((message.UpdateOrderParameters ?? [])
				.Select(HashUpdateOrder)),
			HashWordArray(message.CancelOrderKeys),
			ParseWord(message.RelayParameters, "relay parameters"),
			ParseWord(message.SubaccountApproval, "subaccount approval")));

	private static byte[] HashCreateOrder(GmxCreateOrderPayload order)
	{
		if (order?.Addresses is null || order.Numbers is null)
			throw new InvalidDataException(
				"GMX typed create-order parameters are incomplete.");
		return Keccak(Concat(
			_createOrderTypeHash,
			HashAddresses(order.Addresses),
			HashNumbers(order.Numbers),
			EncodeUnsigned(order.OrderType, "order type"),
			EncodeUnsigned(order.DecreasePositionSwapType,
				"decrease-position swap type"),
			EncodeBoolean(order.IsLong),
			EncodeBoolean(order.IsNativeTokenUnwrapped),
			EncodeBoolean(order.IsAutoCancel),
			ParseWord(order.ReferralCode, "referral code"),
			HashWordArray(order.DataList)));
	}

	private static byte[] HashAddresses(GmxOrderAddresses addresses)
		=> Keccak(Concat(
			_createOrderAddressesTypeHash,
			EncodeAddress(addresses.Receiver),
			EncodeAddress(addresses.CancellationReceiver),
			EncodeAddress(addresses.CallbackContract),
			EncodeAddress(addresses.UiFeeReceiver),
			EncodeAddress(addresses.Market),
			EncodeAddress(addresses.InitialCollateralToken),
			HashArray((addresses.SwapPath ?? []).Select(EncodeAddress))));

	private static byte[] HashNumbers(GmxOrderNumbers numbers)
		=> Keccak(Concat(
			_createOrderNumbersTypeHash,
			EncodeUnsigned(numbers.SizeDeltaUsd, "size delta"),
			EncodeUnsigned(numbers.InitialCollateralDeltaAmount,
				"initial collateral delta"),
			EncodeUnsigned(numbers.TriggerPrice, "trigger price"),
			EncodeUnsigned(numbers.AcceptablePrice, "acceptable price"),
			EncodeUnsigned(numbers.ExecutionFee, "execution fee"),
			EncodeUnsigned(numbers.CallbackGasLimit, "callback gas limit"),
			EncodeUnsigned(numbers.MinimumOutputAmount, "minimum output amount"),
			EncodeUnsigned(numbers.ValidFromTime, "valid-from time")));

	private static byte[] HashUpdateOrder(GmxTypedUpdateOrder order)
	{
		if (order is null)
			throw new InvalidDataException(
				"GMX typed update-order parameters are missing.");
		return Keccak(Concat(
			_updateOrderTypeHash,
			ParseWord(order.Key, "order key"),
			EncodeUnsigned(order.SizeDeltaUsd, "size delta"),
			EncodeUnsigned(order.AcceptablePrice, "acceptable price"),
			EncodeUnsigned(order.TriggerPrice, "trigger price"),
			EncodeUnsigned(order.MinimumOutputAmount, "minimum output amount"),
			EncodeUnsigned(order.ValidFromTime, "valid-from time"),
			EncodeBoolean(order.IsAutoCancel),
			EncodeUnsigned(order.ExecutionFeeIncrease,
				"execution fee increase")));
	}

	private static byte[] HashWordArray(IEnumerable<string> values)
		=> HashArray((values ?? []).Select(value =>
			ParseWord(value, "bytes32 array item")));

	private static byte[] HashArray(IEnumerable<byte[]> values)
		=> Keccak(Concat([.. values ?? []]));

	private static byte[] EncodeBoolean(bool value)
		=> EncodeUnsigned(value ? BigInteger.One : BigInteger.Zero, "boolean");

	private static byte[] EncodeUnsigned(string value, string field)
		=> EncodeUnsigned(value.ParseGmxInteger(field), field);

	private static byte[] EncodeUnsigned(BigInteger value, string field)
	{
		if (value < 0 || value >= BigInteger.One << 256)
			throw new InvalidDataException(
				"GMX " + field + " does not fit in uint256.");
		var raw = value.ToByteArray(true, true);
		var result = new byte[32];
		Buffer.BlockCopy(raw, 0, result, result.Length - raw.Length, raw.Length);
		return result;
	}

	private static byte[] EncodeAddress(string value)
	{
		var raw = ParseHex(value.NormalizeGmxAddress("address"));
		var result = new byte[32];
		Buffer.BlockCopy(raw, 0, result, 12, raw.Length);
		return result;
	}

	private static byte[] ParseWord(string value, string field)
	{
		var result = ParseHex(value);
		if (result.Length != 32)
			throw new InvalidDataException("GMX " + field + " is not bytes32.");
		return result;
	}

	private static byte[] ParseHex(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length == 0 || value.Length % 2 != 0)
			throw new FormatException("GMX hexadecimal value is invalid.");
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
