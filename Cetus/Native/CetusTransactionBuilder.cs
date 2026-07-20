namespace StockSharp.Cetus.Native;

static class CetusTransactionBuilder
{
	private const int _maximumInputCoins = 128;
	private const string _coinPackage =
		"0x0000000000000000000000000000000000000000000000000000000000000002";

	public static Transaction BuildSwap(string sender, CetusMarket market,
		CetusQuote quote, ulong amountLimit,
		IEnumerable<SuiObject> availableCoins,
		CetusSharedObject globalConfig, CetusSharedObject clock)
	{
		sender = sender.NormalizeSuiAddress();
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		ArgumentNullException.ThrowIfNull(globalConfig);
		ArgumentNullException.ThrowIfNull(clock);
		if (quote.PoolId != market.PoolId)
			throw new ArgumentException(
				"The Cetus quote belongs to a different pool.", nameof(quote));
		if (amountLimit == 0)
			throw new ArgumentOutOfRangeException(nameof(amountLimit));
		var requiredInput = quote.Kind == CetusSwapKinds.ExactInput
			? quote.InputAmount
			: amountLimit;
		var coins = SelectCoins(availableCoins, quote.InputCoinType,
			requiredInput);

		var programmable = new ProgrammableTransaction();
		var coinArguments = new List<Argument>(coins.Length);
		foreach (var coin in coins)
		{
			var input = AddInput(programmable, new()
			{
				Kind = Input.Types.InputKind.ImmutableOrOwned,
				ObjectId = coin.ObjectId.NormalizeSuiAddress(),
				Version = coin.Version,
				Digest = coin.Digest.NormalizeTransactionDigest(),
			});
			coinArguments.Add(InputArgument(input));
		}
		var primaryCoin = coinArguments[0];
		if (coinArguments.Count > 1)
		{
			var merge = new MergeCoins
			{
				Coin = primaryCoin.Clone(),
			};
			merge.CoinsToMerge.AddRange(coinArguments.Skip(1)
				.Select(static argument => argument.Clone()));
			programmable.Commands.Add(new Command { MergeCoins = merge });
		}

		var outputCoinType = quote.OutputCoinType.NormalizeCoinType();
		var zeroCommand = checked((uint)programmable.Commands.Count);
		var zero = new MoveCall
		{
			Package = _coinPackage,
			Module = "coin",
			Function = "zero",
		};
		zero.TypeArguments.Add(outputCoinType);
		programmable.Commands.Add(new Command { MoveCall = zero });

		var configInput = AddSharedInput(programmable, globalConfig);
		var poolInput = AddSharedInput(programmable, new()
		{
			ObjectId = market.PoolId,
			InitialVersion = market.PoolInitialVersion,
			IsMutable = true,
		});
		var exactInput = quote.Kind == CetusSwapKinds.ExactInput;
		var exactInputArgument = AddInput(programmable, new()
		{
			Kind = Input.Types.InputKind.Pure,
			Pure = ByteString.CopyFrom(exactInput ? [1] : [0]),
		});
		var amount = exactInput ? quote.InputAmount : quote.OutputAmount;
		var amountArgument = AddPureUInt64(programmable, amount);
		var limitArgument = AddPureUInt64(programmable, amountLimit);
		var sqrtPriceArgument = AddPureUInt128(programmable,
			quote.IsAToB ? CetusExtensions.MinimumSqrtPrice :
			CetusExtensions.MaximumSqrtPrice);
		var clockInput = AddSharedInput(programmable, clock);

		var swap = new MoveCall
		{
			Package = CetusExtensions.IntegrationPackage,
			Module = "pool_script_v2",
			Function = quote.IsAToB ? "swap_a2b" : "swap_b2a",
		};
		swap.TypeArguments.Add(market.CoinA.CoinType);
		swap.TypeArguments.Add(market.CoinB.CoinType);
		var zeroArgument = ResultArgument(zeroCommand);
		swap.Arguments.Add(InputArgument(configInput));
		swap.Arguments.Add(InputArgument(poolInput));
		swap.Arguments.Add(quote.IsAToB
			? primaryCoin.Clone()
			: zeroArgument.Clone());
		swap.Arguments.Add(quote.IsAToB
			? zeroArgument.Clone()
			: primaryCoin.Clone());
		swap.Arguments.Add(InputArgument(exactInputArgument));
		swap.Arguments.Add(InputArgument(amountArgument));
		swap.Arguments.Add(InputArgument(limitArgument));
		swap.Arguments.Add(InputArgument(sqrtPriceArgument));
		swap.Arguments.Add(InputArgument(clockInput));
		programmable.Commands.Add(new Command { MoveCall = swap });

		return new()
		{
			Sender = sender,
			Kind = new()
			{
				Kind = TransactionKind.Types.Kind.ProgrammableTransaction,
				ProgrammableTransaction = programmable,
			},
			Expiration = new()
			{
				Kind = TransactionExpiration.Types.TransactionExpirationKind.None,
			},
		};
	}

	private static SuiObject[] SelectCoins(
		IEnumerable<SuiObject> availableCoins, string coinType,
		ulong requiredAmount)
	{
		coinType = coinType.NormalizeCoinType();
		if (requiredAmount == 0)
			throw new ArgumentOutOfRangeException(nameof(requiredAmount));
		var expectedType = ("0x2::coin::Coin<" + coinType + ">")
			.NormalizeCoinType();
		var candidates = (availableCoins ?? []).Where(item =>
			item is not null && item.HasBalance && item.Balance > 0 &&
			!item.ObjectId.IsEmpty() && !item.Digest.IsEmpty() &&
			!item.ObjectType.IsEmpty() &&
			item.ObjectType.NormalizeCoinType() == expectedType)
			.OrderByDescending(static item => item.Balance).ToArray();
		var selected = new List<SuiObject>();
		var total = BigInteger.Zero;
		foreach (var coin in candidates)
		{
			selected.Add(coin);
			total += coin.Balance;
			if (total >= requiredAmount)
				break;
			if (selected.Count >= _maximumInputCoins)
				break;
		}
		if (total < requiredAmount)
			throw new InvalidOperationException(
				$"Insufficient Cetus input balance for '{coinType}'. Required " +
				$"{requiredAmount}, available {total} in selectable coin objects.");
		return [.. selected];
	}

	private static uint AddSharedInput(ProgrammableTransaction transaction,
		CetusSharedObject value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (value.InitialVersion == 0)
			throw new InvalidOperationException(
				$"Shared Sui object '{value.ObjectId}' has no initial version.");
		return AddInput(transaction, new()
		{
			Kind = Input.Types.InputKind.Shared,
			ObjectId = value.ObjectId.NormalizeSuiAddress(),
			Version = value.InitialVersion,
			Mutable = value.IsMutable,
			Mutability = value.IsMutable
				? Input.Types.Mutability.Mutable
				: Input.Types.Mutability.Immutable,
		});
	}

	private static uint AddPureUInt64(ProgrammableTransaction transaction,
		ulong value)
	{
		var bytes = new byte[sizeof(ulong)];
		BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
		return AddInput(transaction, new()
		{
			Kind = Input.Types.InputKind.Pure,
			Pure = ByteString.CopyFrom(bytes),
		});
	}

	private static uint AddPureUInt128(ProgrammableTransaction transaction,
		string value)
	{
		var number = BigInteger.Parse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture);
		if (number < 0 || number >= BigInteger.One << 128)
			throw new ArgumentOutOfRangeException(nameof(value));
		var bytes = new byte[16];
		var encoded = number.ToByteArray(true, false);
		Buffer.BlockCopy(encoded, 0, bytes, 0, encoded.Length);
		return AddInput(transaction, new()
		{
			Kind = Input.Types.InputKind.Pure,
			Pure = ByteString.CopyFrom(bytes),
		});
	}

	private static uint AddInput(ProgrammableTransaction transaction,
		Input input)
	{
		var index = checked((uint)transaction.Inputs.Count);
		transaction.Inputs.Add(input);
		return index;
	}

	private static Argument InputArgument(uint index)
		=> new()
		{
			Kind = Argument.Types.ArgumentKind.Input,
			Input = index,
		};

	private static Argument ResultArgument(uint index)
		=> new()
		{
			Kind = Argument.Types.ArgumentKind.Result,
			Result = index,
		};
}
