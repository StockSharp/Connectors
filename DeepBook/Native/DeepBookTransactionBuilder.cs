namespace StockSharp.DeepBook.Native;

static class DeepBookTransactionBuilder
{
	private const int _maximumInputCoins = 128;
	private const string _coinPackage =
		"0x0000000000000000000000000000000000000000000000000000000000000002";

	public static Transaction BuildSwap(string sender, string packageId,
		DeepBookMarket market, DeepBookQuote quote, ulong inputAmount,
		ulong minimumOutput, IEnumerable<SuiObject> availableCoins,
		DeepBookSharedObject pool, DeepBookSharedObject clock)
	{
		sender = sender.NormalizeSuiAddress();
		packageId = packageId.NormalizeSuiAddress();
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		ArgumentNullException.ThrowIfNull(pool);
		ArgumentNullException.ThrowIfNull(clock);
		if (inputAmount == 0)
			throw new ArgumentOutOfRangeException(nameof(inputAmount));
		if (minimumOutput == 0)
			throw new ArgumentOutOfRangeException(nameof(minimumOutput));
		if (pool.ObjectId.NormalizeSuiAddress() != market.PoolId)
			throw new ArgumentException(
				"The shared object does not match the DeepBook pool.",
				nameof(pool));

		var inputCoinType = quote.Side == Sides.Sell
			? market.BaseToken.CoinType
			: market.QuoteToken.CoinType;
		var coins = SelectCoins(availableCoins, inputCoinType, inputAmount);
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

		var inputAmountArgument = AddPureUInt64(programmable, inputAmount);
		var splitCommand = checked((uint)programmable.Commands.Count);
		var split = new SplitCoins
		{
			Coin = primaryCoin.Clone(),
		};
		split.Amounts.Add(InputArgument(inputAmountArgument));
		programmable.Commands.Add(new Command { SplitCoins = split });

		var zeroCommand = checked((uint)programmable.Commands.Count);
		var zero = new MoveCall
		{
			Package = _coinPackage,
			Module = "coin",
			Function = "zero",
		};
		zero.TypeArguments.Add(DeepBookExtensions.DeepCoinType);
		programmable.Commands.Add(new Command { MoveCall = zero });

		var poolInput = AddSharedInput(programmable, pool);
		var minimumOutputArgument = AddPureUInt64(programmable, minimumOutput);
		var clockInput = AddSharedInput(programmable, clock);
		var swapCommand = checked((uint)programmable.Commands.Count);
		var swap = new MoveCall
		{
			Package = packageId,
			Module = "pool",
			Function = quote.Side == Sides.Sell
				? "swap_exact_base_for_quote"
				: "swap_exact_quote_for_base",
		};
		swap.TypeArguments.Add(market.BaseToken.CoinType);
		swap.TypeArguments.Add(market.QuoteToken.CoinType);
		swap.Arguments.Add(InputArgument(poolInput));
		swap.Arguments.Add(NestedResultArgument(splitCommand, 0));
		swap.Arguments.Add(ResultArgument(zeroCommand));
		swap.Arguments.Add(InputArgument(minimumOutputArgument));
		swap.Arguments.Add(InputArgument(clockInput));
		programmable.Commands.Add(new Command { MoveCall = swap });

		var senderInput = AddInput(programmable, new()
		{
			Kind = Input.Types.InputKind.Pure,
			Pure = ByteString.CopyFrom(sender.DecodeSuiAddress()),
		});
		var transfer = new TransferObjects
		{
			Address = InputArgument(senderInput),
		};
		for (uint index = 0; index < 3; index++)
			transfer.Objects.Add(NestedResultArgument(swapCommand, index));
		programmable.Commands.Add(new Command { TransferObjects = transfer });

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
				$"Insufficient DeepBook input balance for '{coinType}'. " +
				$"Required {requiredAmount}, available {total} in selectable " +
				"coin objects.");
		return [.. selected];
	}

	private static uint AddSharedInput(ProgrammableTransaction transaction,
		DeepBookSharedObject value)
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

	private static Argument NestedResultArgument(uint command, uint index)
		=> new()
		{
			Kind = Argument.Types.ArgumentKind.Result,
			Result = command,
			Subresult = index,
		};
}
