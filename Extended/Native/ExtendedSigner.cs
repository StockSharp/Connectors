namespace StockSharp.Extended.Native;

sealed class ExtendedSigner : IDisposable
{
	private BigInteger _privateKey;
	private readonly BigInteger _publicKey;
	private readonly uint _vault;
	private readonly string _chainId;
	private bool _isDisposed;

	public ExtendedSigner(SecureString privateKey, string publicKey, uint vault,
		bool isTestnet)
	{
		if (privateKey.IsEmpty())
			throw new ArgumentNullException(nameof(privateKey));
		_privateKey = ExtendedStarkCrypto.ParseHex(privateKey.UnSecure());
		if (_privateKey <= 0 || _privateKey >= ExtendedStarkCrypto.CurveOrder)
			throw new ArgumentOutOfRangeException(nameof(privateKey),
				"Extended Stark private key is outside the curve scalar range.");
		_publicKey = ExtendedStarkCrypto.ParseHex(
			publicKey.ThrowIfEmpty(nameof(publicKey)));
		var derived = ExtendedStarkCrypto.GetPublicKey(_privateKey);
		if (derived != _publicKey)
			throw new InvalidOperationException(
				"The Extended Stark private key does not match the active account L2 key.");
		_vault = vault > 0 ? vault : throw new ArgumentOutOfRangeException(
			nameof(vault), vault, "Extended L2 vault must be positive.");
		_chainId = isTestnet ? "SN_SEPOLIA" : "SN_MAIN";
		ExtendedStarkCrypto.EnsureValidated();
	}

	public ExtendedCreateOrderRequest CreateOrder(ExtendedMarket market,
		ExtendedSides side, ExtendedOrderTypes orderType, decimal quantity,
		decimal price, ExtendedTimeInForces timeInForce, bool isPostOnly,
		bool isReduceOnly, DateTime expiry, decimal takerFee,
		ExtendedOrderCondition condition, string cancelId)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		market = market ?? throw new ArgumentNullException(nameof(market));
		condition ??= new();
		if (quantity <= 0)
			throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
				"Extended order quantity must be positive.");
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"Extended signed orders require a positive price.");
		if (takerFee is < 0 or > 1)
			throw new ArgumentOutOfRangeException(nameof(takerFee), takerFee,
				"Extended taker fee must be between zero and one.");
		if (condition.BuilderFee is < 0 or > 1)
			throw new ArgumentOutOfRangeException(nameof(condition.BuilderFee),
				condition.BuilderFee, "Extended builder fee must be between zero and one.");
		if ((condition.BuilderFee is null) != (condition.BuilderId is null))
			throw new InvalidOperationException(
				"Extended builder fee and builder ID must be specified together.");
		if (condition.BuilderId is <= 0)
			throw new InvalidOperationException(
				"Extended builder ID must be positive.");

		var l2 = market.L2Config ?? throw new InvalidDataException(
			"Extended market has no L2 settlement configuration.");
		if (l2.SyntheticResolution <= 0 || l2.CollateralResolution <= 0)
			throw new InvalidDataException(
				"Extended market has invalid settlement resolutions.");
		var isBuy = side == ExtendedSides.Buy;
		var syntheticAmount = ConvertAmount(quantity, l2.SyntheticResolution,
			isBuy);
		var collateralAmount = ConvertAmount(quantity * price,
			l2.CollateralResolution, isBuy);
		var totalFee = takerFee + (condition.BuilderFee ?? 0m);
		var feeAmount = ConvertFee(quantity * price * totalFee,
			l2.CollateralResolution);
		if (isBuy)
			collateralAmount = checked(-collateralAmount);
		else
			syntheticAmount = checked(-syntheticAmount);

		var nonce = CreateNonce();
		expiry = expiry.EnsureExtendedUtc();
		var settlementExpiration = checked((ulong)Math.Ceiling(
			(expiry.AddDays(14) - DateTime.UnixEpoch).TotalSeconds));
		var orderHash = ExtendedStarkCrypto.GetOrderMessageHash(
			_vault,
			ExtendedStarkCrypto.ParseHex(l2.SyntheticId),
			syntheticAmount,
			ExtendedStarkCrypto.ParseHex(l2.CollateralId),
			collateralAmount,
			ExtendedStarkCrypto.ParseHex(l2.CollateralId),
			checked((ulong)feeAmount),
			settlementExpiration,
			nonce,
			_publicKey,
			_chainId);
		var signature = ExtendedStarkCrypto.Sign(orderHash, _privateKey);
		var externalId = orderHash.ToString(CultureInfo.InvariantCulture);
		return new()
		{
			Id = externalId,
			Market = market.Name,
			Type = orderType,
			Side = side,
			Quantity = quantity.ToExtendedWire(),
			Price = price.ToExtendedWire(),
			IsReduceOnly = isReduceOnly,
			IsPostOnly = isPostOnly,
			TimeInForce = timeInForce,
			ExpiryEpochMilliseconds = expiry.ToExtendedUnixMilliseconds(),
			Fee = takerFee.ToExtendedWire(),
			Nonce = nonce.ToString(CultureInfo.InvariantCulture),
			SelfTradeProtectionLevel =
				ExtendedSelfTradeProtectionLevels.Account,
			CancelId = cancelId,
			Settlement = new()
			{
				Signature = new()
				{
					R = ExtendedStarkCrypto.ToHex(signature.R),
					S = ExtendedStarkCrypto.ToHex(signature.S),
				},
				StarkKey = ExtendedStarkCrypto.ToHex(_publicKey),
				CollateralPosition = _vault.ToString(CultureInfo.InvariantCulture),
			},
			Trigger = orderType == ExtendedOrderTypes.Conditional
				? new()
				{
					TriggerPrice = condition.TriggerPrice?.ToExtendedWire() ??
						throw new InvalidOperationException(
							"Extended conditional orders require a trigger price."),
					TriggerPriceType = condition.TriggerPriceType,
					Direction = condition.TriggerDirection,
					ExecutionPriceType = condition.ExecutionPriceType,
				}
				: null,
			DebuggingAmounts = new()
			{
				CollateralAmount = collateralAmount.ToString(
					CultureInfo.InvariantCulture),
				FeeAmount = feeAmount.ToString(CultureInfo.InvariantCulture),
				SyntheticAmount = syntheticAmount.ToString(
					CultureInfo.InvariantCulture),
			},
			BuilderFee = condition.BuilderFee?.ToExtendedWire(),
			BuilderId = condition.BuilderId,
		};
	}

	private static long ConvertAmount(decimal amount, long resolution,
		bool isBuy)
	{
		var scaled = checked(amount * resolution);
		return checked((long)(isBuy ? Math.Ceiling(scaled) : Math.Floor(scaled)));
	}

	private static long ConvertFee(decimal amount, long resolution)
		=> checked((long)Math.Ceiling(checked(amount * resolution)));

	private static uint CreateNonce()
	{
		Span<byte> bytes = stackalloc byte[sizeof(uint)];
		RandomNumberGenerator.Fill(bytes);
		return BitConverter.ToUInt32(bytes);
	}

	public void Dispose()
	{
		_privateKey = BigInteger.Zero;
		_isDisposed = true;
	}
}

readonly record struct ExtendedStarkSignature(BigInteger R, BigInteger S);

static class ExtendedStarkCrypto
{
	private readonly record struct CurvePoint(BigInteger X, BigInteger Y,
		bool IsInfinity);

	private readonly record struct JacobianPoint(BigInteger X, BigInteger Y,
		BigInteger Z);

	private static readonly BigInteger _fieldPrime = ParseHex(
		"800000000000011000000000000000000000000000000000000000000000001");
	public static readonly BigInteger CurveOrder = ParseHex(
		"0800000000000010ffffffffffffffffb781126dcae7b2321e66a241adc64d2f");
	private static readonly BigInteger _elementUpperBound = BigInteger.One << 251;
	private static readonly BigInteger _curveBeta = ParseHex(
		"06f21413efbe40de150e596d72f7a8c5609ad26c15c915c1f4cdfcb99cee9e89");
	private static readonly CurvePoint _generator = new(
		ParseHex("01ef15c18599971b7beced415a40f0c7deacfd9b0d1819e03d723d8bc943cfca"),
		ParseHex("005668060aa49730b7be4801df46ec62de53ecd11abe43a32873000c36e8dc1f"),
		false);
	private static readonly BigInteger _messagePrefix = ToShortString(
		"StarkNet Message");
	private static readonly BigInteger _domainSelector = ParseHex(
		"1ff2f602e42168014d405a94f75e8a93d640751d71d16311266e140d8b0a210");
	private static readonly BigInteger _orderSelector = ParseHex(
		"36da8d51815527cabfaa9c982f564c80fa7429616739306036f1f9b608dd112");
	private static readonly BigInteger[] _roundConstants =
		_roundConstantsHex.Split([' ', '\r', '\n', '\t'],
			StringSplitOptions.RemoveEmptyEntries).Select(ParseHex).ToArray();
	private static readonly bool _isValidated = ValidateKnownVectors();

	private const string _roundConstantsHex = """
6861759ea556a2339dd92f9562a30b9e58e2ad98109ae4780b7fd8eac77fe6f
3827681995d5af9ffc8397a3d00425a3da43f76abf28a64e4ab1a22f27508c4
3a3956d2fad44d0e7f760a2277dc7cb2cac75dc279b2d687a0dbe17704a8309
626c47a7d421fe1f13c4282214aa759291c78f926a2d1c6882031afe67ef4cd
78985f8e16505035bd6df5518cfd41f2d327fcc948d772cadfe17baca05d6a6
5427f10867514a3204c659875341243c6e26a68b456dc1d142dcf34341696ff
5af083f36e4c729454361733f0883c5847cd2c5d9d4cb8b0465e60edce699d7
7d71701bde3d06d54fa3f74f7b352a52d3975f92ff84b1ac77e709bfd388882
603da06882019009c26f8a6320a1c5eac1b64f699ffea44e39584467a6b1d3e
4332a6f6bde2f288e79ce13f47ad1cdeebd8870fd13a36b613b9721f6453a5d
53d0ebf61664c685310a04c4dec2e7e4b9a813aaeff60d6c9e8caeb5cba78e7
5346a68894845835ae5ebcb88028d2a6c82f99f928494ee1bfc2d15eaabfebc
4b085eb1df4258c3453cc97445954bf3433b6ab9dd5a99592864c00f54a3f9a
731cfd19d508285965f12a079b2a169fdfe0a8e610e6f2d5ca5d7b0961f6d96
217d08b5339852bcc6f7a774936b3e72ecd9e1f9a73d743f8079c1e3587eeaa
c935dd633b0fd63599b13c850dab3cb966ba510c81b20959e267008518c6e
52af8d378dd6772ee187ed23f79a7d98cf5a0a387103971467fe940e7b8b2be
294851c98b2682f1ec9918b9f12fcceaa6e28a7b79b2e506362cda595f8ab75
11b59990bacc280824d1021418d4f589da8c30063471494c204b169ab086064
4b4df56e3d7753f91960d59ae099b9beb2ce690e6bbdcd0b599d49ceb2acd6a
5eecfa15a757dc3ecae9fbd8ff06e466243534f30629fc5f1cf09eb5161ac4
680bfdd8b9680e04659227634a1ec5282e5a7cef81b15677f8448bda4279059
1d0bf8fab0a1a7a14e2930794f7a3065c17e10b1cedd791b8877d97acd85053
2c2c8c79f808ace54ba207053c0d412c0fc11a610f14c48876701a37e32f464
354ec9ed01d20ec52aae19a9b858d3474d8234c11ad7bce630ad56c54afa562
30df20fcf6427bac38bb5d1a42287f4e4136ac5892340e994e6ea28deec1e55
528cf329c64e7ee3040bafbdeff61e241d99b424091e31472eda296fc9c6778
40416f24f623534634789660df5435ebf0c3e0c69e6c5b5ff6e757930bd1960
380c8f936e2ed9fd488ae3bac7dce315ba21b11e88339cd5444435ccc9ea38
1cc4f5d5603d176f1a8e344392efd2d03ad0541832829d245e0e2291f255b75
5728917af5da91f9539310d99f5d142e011d6c8e015ea5423c502aa99c09752
efb450a9e86e1a46e295a348f0f23590925107d17c56d7c788fecc17219aa1
2020d74d36c421ae1a025616b342d0784b8fcd977de6c53a6c26693774dca99
7cfb309b75fd3bf2705558ae511dc82335050969f4bf84fa2b7b4f583989287
4651e48b2e9349a5365e009ece626809d7b7d02a617eb98c785a784812d75e9
d77627b270f65122d0269719da923ccae822d9aad0f0947a3b5c8f71c0dcc7
199ad3d641b54c4d571b3fe37773a8b82b003377f0dd8b7d3b7758c32908ea8
44f33640a8ecfd3973e2e9172a7333482b2d297be2da289319e72d137cdfe6e
7e4adf9894d964189d00a02dcf1e6be7f801234f5216eab6b6f366b6701abf7
3641fa5b3c90452f5ff808f8a9817eda7c6aecfb5471dfdca559fb4e711ee90
3de5729efd2fcbd897a49a78fa923fc306df32e6e2f0e02d0eee2c2cc3f3533
62691891a3fc1e27f622966ca0be20c06563500c8f06c9bdb77bd2882d6c994
6608d3bf11c18e4688739f72205763d1590cc4f9885ae1d86e96e0604baa0be
11c9c9b39cac71e3419726ce779116d07249f51cbdda4fd98c25cbbf593a316
61e23b58203269caef0850f74da27b9748e3312ea40c6844dd68c557c462ad7
4182cd9ab1d9488f870a572010bc2a3d9878440b25951e4ce010855cf83bdc8
520fe6c4a096793f9055e6823116d15f1df2fe89d306f9965f6a59f4f3ecb71
346b2b2d6e5810129e093093dcd3dfa99ed6d71f47723ea3fbe4d4e2fd4afa1
1359ca923e7f1448ec1dd2a3684bee4e8b682c8e8e973acea72877ce9f7e6cf
47c655f55cf307800dfefdad24de86fde9deadab145a1b392420f37b95d9675
4ab291f16555fa8a968cd7c9c285a9598efd925f2d58b7aa38ad87dca8441a8
39f409c7c782101223d1f6f7d86c21a22c44ef959510e392c9c7c5d17c629c5
44be36b782f882ad86eecb0cd6beb02e1a2f9fb5587a3babfacead0cafb6052
50a1dfde9b504ad2906db6eb5b507203cd1ceb394c52ce7107679a53a0d538b
5c753c14da89e287b181c0dd11ac6c3680bdd7f1017dae083e7aebbeab183ab
2cf6306ed32232106c8015a3b180f386eee93e15f7b4f4fa57746525fc0520c
2c2014634d52e27420873cf347429091dfc6380689bd4f54d7d8e502c1c3a09
3cfb9c5bd93e02b2fdacde2058e33e5975c446345f010d850fc09cdf86ed8a1
363fa71a383cf3897933f1411fc5f806e311e84f72cb50a9ea4e1281f6b0299
728199657067ee16947b3fc76271676b4901b2a3686cffebcb960da91b05df8
3fdfbd47d27f3d34f0723b728e8921dc9bde34a9872df5a652a078d7e4ee021
7f241379440cacd7dc0efbe7858eb7de53cc02ca7d24197945c453398eff449
5b2e8771ea9a0004e3bf056f3727797cbb457a27574d5f104354e52a5c25f0b
a8ddbce708de44a7e0b3b0333146e1e910245be6bf822ea057a081bda2e23e
2d521e0daca24e431aa47cd90a0f551c12270e533835613edce2e19aa9b0f61
6cdbc0f2aa54d2cf7d5ac3b93f855af03eef7b07aaee00341a6266c30e08ae6
3dd96a17111ec8f4c5da3ad6794c0961ceee452cbe92c7a0941112b36ed9bf3
5eafb1edeedc5c07ac07fdd06159344a2cfb92196a65d9ec0c5e732c36687dc
4ab038d7b09eda9324577b260feaebdbcec5a7b7c7f449b312cfcd065c207e6
4ca71981e4df6b505d2b0d94e235608463c58052570f68e495fc80c7fdef220
6dee9c6da4617e32aa419899c8ea8137e9b59d7e2759ffe573c15b77e413d2f
58f9e60b34ddab84dcbe2396065a4305b4a795a4770e4541e625d0460c6f186
47b7b4a802a10c1e6c9c735db6c34042d290906f274bea8fcecef17fc9af632
1849bcdb9ad7171096ecc936a186774084a074be0bfc0fbb9463a06a2bd430c
41870fbe04438348af5767bddaecd8aea3b49b4217547dec4d699b1466736cc
226c04e598076a9fa02aa64557daf28c0ec42e3d4da68d1965029d284738b07
1f0e971f0485a5b42eb92d6655c3ddb475cec4371f269a95335b2a7d6dac0fb
9f31cc2907dccbf994d35aa47ee3f4ebdf3703f795047a7b40dd3926431563
4b40cce78f3b641e31ce4df58ce5a42c22cfbc198c84451ffe8cca4c64bd7d2
191660489e4bd8a3e4563173de4a226f3ac736962fdfb70f72cb93ce50f8b9f
18c0919618db971f74eb01f293f2daea814b475103373dc7ed8dd4c7b467410
35b60253848530e845c8753121577d0ef37002e941c3dc1fb240bd57eadc803
1ae99db1575ae91c8b43a9f71a5f362581ad9b413d97fa6fd029134957451d5
3e6e1d0f3f8a0f728148ebcbd5d7d337d7cb8feb58a37d2d1dfb357e172647b
18bc36dffa8f96a659e1a171b55d2706ee3e9ad619e16f5c38dd1f4a209b8f3
2c7a3ef1afb6a302b54afc3a107ff9199a16efe9a1cc3ab83fa5b64893de4ed
53a7bd889bed07bf5e27dd8e92f6ae85e4fe4e84b0c6dde9856e94469de4bd7
4d383ff7ffc6318fda704aca35995f86bec5a02ce9a0bf9d3cc0cc2f03ccea9
4667b6762fb8ad53d07ef7e8a65b21ca96e0b3503037710d1292519c326f5cd
2cc8b43e75cf0b42a93c39ea98bcd46055dccc9589f02eb7fb536422e5921f
6b32ee98680871d38751447bfd76086ba4df0e7be59c55f4b2ce25582bf9c60
3e907927c7182faaa3b3c81358b82e734efac1f0609f0862d635cb1387102a3
3f3a5057b3a08975f0253728e512af78d2f437973f6a93793ea5e8424fbc6ea
14b491d73724779f8aa74b3fd8aa5821c21e1017224726a7a946bb6ca68d8f5
5c8278c7bbfc30ae7f60e514fe3b9367aca84c54ad1373861695ea4abb814ef
64851937f9836ee5a08a7dde65e44b467018a82ba3bf99bba0b4502755c8074
6a9ac84251294769eca450ffb52b441882be77cb85f422ff9ea5e73f1d971dc
37ec35b710b0d04c9a2b71f2f7bd098c6a81d991d27f0fc1884f5ca545064de
5334f75b052c0235119816883040da72c6d0a61538bdfff46d6a242bfeb7a1
5d0af4fcbd9e056c1020cca9d871ae68f80ee4af2ec6547cd49d6dca50aa431
30131bce2fba5694114a19c46d24e00b4699dc00f1d53ba5ab99537901b1e65
5646a95a7c1ae86b34c0750ed2e641c538f93f13161be3c4957660f2e788965
4b9f291d7b430c79fac36230a11f43e78581f5259692b52c90df47b7d4ec01a
5006d393d3480f41a98f19127072dc83e00becf6ceb4d73d890e74abae01a13
62c9d42199f3b260e7cb8a115143106acf4f702e6b346fd202dc3b26a679d80
51274d092db5099f180b1a8a13b7f2c7606836eabd8af54bf1d9ac2dc5717a5
61fc552b8eb75e17ad0fb7aaa4ca528f415e14f0d9cdbed861a8db0bfff0c5b
""";

	public static void EnsureValidated()
	{
		if (!_isValidated)
			throw new CryptographicException(
				"Extended Stark cryptography self-check failed.");
	}

	public static BigInteger GetPublicKey(BigInteger privateKey)
		=> ToAffine(Multiply(_generator, privateKey)).X;

	public static BigInteger GetOrderMessageHash(uint positionId,
		BigInteger baseAssetId, long baseAmount, BigInteger quoteAssetId,
		long quoteAmount, BigInteger feeAssetId, ulong feeAmount,
		ulong expiration, uint salt, BigInteger publicKey, string chainId)
	{
		var orderHash = PoseidonHashMany(
		[
			_orderSelector,
			positionId,
			baseAssetId,
			Normalize(baseAmount),
			quoteAssetId,
			Normalize(quoteAmount),
			feeAssetId,
			feeAmount,
			expiration,
			salt,
		]);
		var domainHash = PoseidonHashMany(
		[
			_domainSelector,
			ToShortString("Perpetuals"),
			ToShortString("v0"),
			ToShortString(chainId.ThrowIfEmpty(nameof(chainId))),
			BigInteger.One,
		]);
		return PoseidonHashMany(
		[
			_messagePrefix,
			domainHash,
			publicKey,
			orderHash,
		]);
	}

	public static ExtendedStarkSignature Sign(BigInteger message,
		BigInteger privateKey)
	{
		if (message < 0 || message >= _elementUpperBound)
			throw new CryptographicException(
				"Extended Stark message hash is outside the signing range.");
		BigInteger seed = BigInteger.Zero;
		while (true)
		{
			var k = GenerateK(message, privateKey, seed);
			var point = ToAffine(Multiply(_generator, k));
			var r = point.X;
			if (r <= 0 || r >= _elementUpperBound)
			{
				seed++;
				continue;
			}
			var s = NormalizeMod((message + r * privateKey) *
				ModInverse(k, CurveOrder), CurveOrder);
			if (s <= 0 || s >= _elementUpperBound)
			{
				seed++;
				continue;
			}
			return new(r, s);
		}
	}

	public static BigInteger ParseHex(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.IsEmpty() || value.Any(static ch => !Uri.IsHexDigit(ch)))
			throw new FormatException("Invalid Stark hexadecimal value.");
		if ((value.Length & 1) != 0)
			value = "0" + value;
		return new BigInteger(Convert.FromHexString(value), true, true);
	}

	public static string ToHex(BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
	}

	private static BigInteger PoseidonHashMany(
		IReadOnlyList<BigInteger> inputs)
	{
		var values = new List<BigInteger>(inputs.Count + 2);
		foreach (var input in inputs)
			values.Add(Normalize(input));
		values.Add(BigInteger.One);
		if ((values.Count & 1) != 0)
			values.Add(BigInteger.Zero);
		var state = new BigInteger[3];
		for (var i = 0; i < values.Count; i += 2)
		{
			state[0] = Normalize(state[0] + values[i]);
			state[1] = Normalize(state[1] + values[i + 1]);
			Permute(state);
		}
		return state[0];
	}

	private static void Permute(BigInteger[] state)
	{
		var index = 0;
		for (var round = 0; round < 4; round++)
			FullRound(state, ref index);
		for (var round = 0; round < 83; round++)
		{
			state[2] = Cube(state[2] + _roundConstants[index++]);
			Mix(state);
		}
		for (var round = 0; round < 4; round++)
			FullRound(state, ref index);
		if (index != _roundConstants.Length)
			throw new CryptographicException(
				"Invalid Extended Poseidon round constants.");
	}

	private static void FullRound(BigInteger[] state, ref int index)
	{
		for (var i = 0; i < state.Length; i++)
			state[i] = Cube(state[i] + _roundConstants[index++]);
		Mix(state);
	}

	private static BigInteger Cube(BigInteger value)
	{
		value = Normalize(value);
		return value * value % _fieldPrime * value % _fieldPrime;
	}

	private static void Mix(BigInteger[] state)
	{
		var sum = Normalize(state[0] + state[1] + state[2]);
		var x = Normalize(sum + 2 * state[0]);
		var y = Normalize(sum - 2 * state[1]);
		var z = Normalize(sum - 3 * state[2]);
		state[0] = x;
		state[1] = y;
		state[2] = z;
	}

	private static BigInteger GenerateK(BigInteger message,
		BigInteger privateKey, BigInteger seed)
	{
		var x = ToFixedBytes(privateKey, 32);
		var hash = ToFixedBytes(message, 32);
		var additional = seed.IsZero
			? Array.Empty<byte>()
			: seed.ToByteArray(true, true);
		var key = new byte[32];
		var value = Enumerable.Repeat((byte)1, 32).ToArray();
		try
		{
			for (byte round = 0; round <= 1; round++)
			{
				key = Hmac(key, value, [round], x, hash, additional);
				value = Hmac(key, value);
			}
			while (true)
			{
				value = Hmac(key, value);
				var candidate = new BigInteger(value, true, true) >> 4;
				if (candidate > 0 && candidate < CurveOrder)
					return candidate;
				key = Hmac(key, value, [byte.MinValue]);
				value = Hmac(key, value);
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(x);
			CryptographicOperations.ZeroMemory(hash);
			CryptographicOperations.ZeroMemory(key);
			CryptographicOperations.ZeroMemory(value);
			if (additional.Length > 0)
				CryptographicOperations.ZeroMemory(additional);
		}
	}

	private static byte[] Hmac(byte[] key, params byte[][] parts)
	{
		using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256,
			key);
		foreach (var part in parts)
			hmac.AppendData(part);
		return hmac.GetHashAndReset();
	}

	private static byte[] ToFixedBytes(BigInteger value, int length)
	{
		var result = new byte[length];
		if (!value.TryWriteBytes(result, out var written, true, true))
			throw new ArgumentOutOfRangeException(nameof(value));
		if (written < length)
		{
			result.AsSpan(0, written).CopyTo(result.AsSpan(length - written));
			result.AsSpan(0, length - written).Clear();
		}
		return result;
	}

	private static JacobianPoint Multiply(CurvePoint point, BigInteger scalar)
	{
		if (point.IsInfinity || scalar.IsZero)
			return new(BigInteger.Zero, BigInteger.One, BigInteger.Zero);
		var result = new JacobianPoint(BigInteger.Zero, BigInteger.One,
			BigInteger.Zero);
		var bitLength = GetBitLength(scalar);
		for (var bit = bitLength - 1; bit >= 0; bit--)
		{
			result = Double(result);
			if (!((scalar >> bit) & BigInteger.One).IsZero)
				result = Add(result, point);
		}
		return result;
	}

	private static JacobianPoint Double(JacobianPoint point)
	{
		if (point.Z.IsZero || point.Y.IsZero)
			return new(BigInteger.Zero, BigInteger.One, BigInteger.Zero);
		var a = Normalize(point.X * point.X);
		var b = Normalize(point.Y * point.Y);
		var c = Normalize(b * b);
		var d = Normalize(2 * (Normalize((point.X + b) * (point.X + b)) -
			a - c));
		var z2 = Normalize(point.Z * point.Z);
		var e = Normalize(3 * a + Normalize(z2 * z2));
		var f = Normalize(e * e);
		var x = Normalize(f - 2 * d);
		var y = Normalize(e * (d - x) - 8 * c);
		var z = Normalize(2 * point.Y * point.Z);
		return new(x, y, z);
	}

	private static JacobianPoint Add(JacobianPoint point, CurvePoint addend)
	{
		if (point.Z.IsZero)
			return new(addend.X, addend.Y, BigInteger.One);
		var z2 = Normalize(point.Z * point.Z);
		var u2 = Normalize(addend.X * z2);
		var s2 = Normalize(addend.Y * point.Z * z2);
		var h = Normalize(u2 - point.X);
		if (h.IsZero)
			return s2 == point.Y
				? Double(point)
				: new(BigInteger.Zero, BigInteger.One, BigInteger.Zero);
		var hh = Normalize(h * h);
		var i = Normalize(4 * hh);
		var j = Normalize(h * i);
		var r = Normalize(2 * (s2 - point.Y));
		var v = Normalize(point.X * i);
		var x = Normalize(r * r - j - 2 * v);
		var y = Normalize(r * (v - x) - 2 * point.Y * j);
		var z = Normalize((point.Z + h) * (point.Z + h) - z2 - hh);
		return new(x, y, z);
	}

	private static CurvePoint ToAffine(JacobianPoint point)
	{
		if (point.Z.IsZero)
			return new(BigInteger.Zero, BigInteger.Zero, true);
		var zInverse = ModInverse(point.Z, _fieldPrime);
		var z2 = Normalize(zInverse * zInverse);
		return new(
			Normalize(point.X * z2),
			Normalize(point.Y * z2 * zInverse),
			false);
	}

	private static BigInteger ModInverse(BigInteger value, BigInteger modulus)
	{
		value = NormalizeMod(value, modulus);
		if (value.IsZero)
			throw new DivideByZeroException("Cannot invert zero modulo a field.");
		var oldR = modulus;
		var r = value;
		var oldT = BigInteger.Zero;
		var t = BigInteger.One;
		while (!r.IsZero)
		{
			var quotient = oldR / r;
			(oldR, r) = (r, oldR - quotient * r);
			(oldT, t) = (t, oldT - quotient * t);
		}
		if (oldR != BigInteger.One)
			throw new ArithmeticException("Value has no modular inverse.");
		return NormalizeMod(oldT, modulus);
	}

	private static int GetBitLength(BigInteger value)
	{
		var bytes = value.ToByteArray(true, true);
		if (bytes.Length == 0)
			return 0;
		var bits = (bytes.Length - 1) * 8;
		var leading = bytes[0];
		while (leading != 0)
		{
			bits++;
			leading >>= 1;
		}
		return bits;
	}

	private static BigInteger ToShortString(string value)
	{
		var bytes = Encoding.ASCII.GetBytes(
			value.ThrowIfEmpty(nameof(value)));
		if (bytes.Length > 31 || bytes.Any(static item => item > 127))
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"Starknet short strings must contain at most 31 ASCII bytes.");
		var result = BigInteger.Zero;
		foreach (var item in bytes)
			result = (result << 8) + item;
		return result;
	}

	private static BigInteger Normalize(BigInteger value)
		=> NormalizeMod(value, _fieldPrime);

	private static BigInteger NormalizeMod(BigInteger value,
		BigInteger modulus)
	{
		value %= modulus;
		return value.Sign < 0 ? value + modulus : value;
	}

	private static bool ValidateKnownVectors()
	{
		if (_roundConstants.Length != 107)
			return false;
		var hash = GetOrderMessageHash(
			100,
			2,
			100,
			1,
			-156,
			1,
			74,
			100,
			123,
			ParseHex(
				"5d05989e9302dcebc74e241001e3e3ac3f4402ccf2f8e6f74b034b07ad6a904"),
			"SN_SEPOLIA");
		if (hash != ParseHex(
			"4de4c009e0d0c5a70a7da0e2039fb2b99f376d53496f89d9f437e736add6b48"))
			return false;
		var signature = Sign(
			ParseHex(
				"06fea80189363a786037ed3e7ba546dad0ef7de49fccae0e31eb658b7dd4ea76"),
			ParseHex(
				"0139fe4d6f02e666e86a6f58e65060f115cd3c185bd9e98bd829636931458f79"));
		return signature.R == ParseHex(
				"061ec782f76a66f6984efc3a1b6d152a124c701c00abdd2bf76641b4135c770f") &&
			signature.S == ParseHex(
				"04e44e759cea02c23568bb4d8a09929bbca8768ab68270d50c18d214166ccd9a");
	}
}
