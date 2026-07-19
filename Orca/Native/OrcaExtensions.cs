namespace StockSharp.Orca.Native;

static class OrcaExtensions
{
	public const string ProgramAddress =
		"whirLbMiicVdio4qvUfM5KAg6Ct8VwpYzGff3uctyCc";
	public const string MainnetConfigAddress =
		"2LecshUwdy9xi7meFgHtFJQNSKk4KdTrcpvaB56dP2NQ";
	public const string SystemProgramAddress =
		"11111111111111111111111111111111";
	public const string AssociatedTokenProgramAddress =
		"ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL";
	public const string TokenProgramAddress =
		"TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
	public const string Token2022ProgramAddress =
		"TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";
	public const string MetadataProgramAddress =
		"metaqbxxUerdq28cj1RbAWkYQm3ybzjb6a8bt518x1s";
	public const string MemoProgramAddress =
		"MemoSq4gqABAXKb96qnH8TysNcWxMyWCqXgDLGmfcHr";
	public const string WrappedSolMint =
		"So11111111111111111111111111111111111111112";
	public const string UsdcMint =
		"EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";

	public const int TickArraySize = 88;
	public const int MinimumTickIndex = -443636;
	public const int MaximumTickIndex = 443636;
	public static readonly BigInteger MinimumSqrtPrice = 4295048016;
	public static readonly BigInteger MaximumSqrtPrice =
		BigInteger.Parse("79226673515401279992447579055",
			CultureInfo.InvariantCulture);

	public static readonly byte[] WhirlpoolDiscriminator =
		[63, 149, 209, 12, 225, 128, 99, 9];
	public static readonly byte[] FixedTickArrayDiscriminator =
		[69, 97, 189, 190, 110, 7, 66, 187];
	public static readonly byte[] DynamicTickArrayDiscriminator =
		[17, 216, 246, 142, 225, 199, 218, 56];
	public static readonly byte[] SwapV2Discriminator =
		[43, 4, 237, 11, 26, 201, 30, 98];
	public static readonly byte[] TradedEventDiscriminator =
		[225, 202, 73, 175, 147, 43, 160, 150];

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	private readonly record struct IndexedTick(int Index, OrcaTick Tick);
	private readonly record struct SwapAmounts(BigInteger TokenA,
		BigInteger TokenB);
	private readonly record struct SwapStep(BigInteger AmountIn,
		BigInteger AmountOut, BigInteger NextSqrtPrice,
		BigInteger FeeAmount);

	public static string NormalizePublicKey(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!PublicKey.IsValid(value))
			throw new ArgumentException(
				$"Invalid Solana public key '{value}'.", nameof(value));
		var key = new PublicKey(value);
		_ = key.KeyBytes;
		return key.Key;
	}

	public static PublicKey ToPublicKey(this string value)
		=> new(value.NormalizePublicKey());

	public static string FindProgramAddress(string programAddress,
		params byte[][] seeds)
	{
		if (!PublicKey.TryFindProgramAddress(seeds,
			programAddress.ToPublicKey(), out var address, out _))
			throw new InvalidOperationException(
				$"Unable to derive a PDA for program '{programAddress}'.");
		return address.Key;
	}

	public static string TickArrayAddress(string poolAddress,
		int startTickIndex)
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("tick_array"),
			poolAddress.ToPublicKey().KeyBytes,
			Encoding.UTF8.GetBytes(startTickIndex.ToString(
				CultureInfo.InvariantCulture)));

	public static string OracleAddress(string poolAddress)
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("oracle"),
			poolAddress.ToPublicKey().KeyBytes);

	public static string AssociatedTokenAddress(string owner, string mint,
		string tokenProgram)
		=> FindProgramAddress(AssociatedTokenProgramAddress,
			owner.ToPublicKey().KeyBytes,
			tokenProgram.ToPublicKey().KeyBytes,
			mint.ToPublicKey().KeyBytes);

	public static string MetadataAddress(string mint)
	{
		var program = MetadataProgramAddress.ToPublicKey();
		return FindProgramAddress(MetadataProgramAddress,
			Encoding.UTF8.GetBytes("metadata"), program.KeyBytes,
			mint.ToPublicKey().KeyBytes);
	}

	public static string GetRpcEndpoint(this OrcaClusters cluster)
		=> cluster switch
		{
			OrcaClusters.Mainnet => "https://api.mainnet-beta.solana.com",
			OrcaClusters.Devnet => "https://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static string GetSocketEndpoint(this OrcaClusters cluster)
		=> cluster switch
		{
			OrcaClusters.Mainnet => "wss://api.mainnet-beta.solana.com",
			OrcaClusters.Devnet => "wss://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static SecurityId ToStockSharp(this OrcaMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Orca,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static BigInteger ToBaseUnits(this decimal value, int decimals)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (decimals is < 0 or > 255)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var text = value.ToString("0.############################",
			CultureInfo.InvariantCulture);
		var separator = text.IndexOf('.');
		var whole = separator < 0 ? text : text[..separator];
		var fraction = separator < 0 ? string.Empty : text[(separator + 1)..];
		if (fraction.Length > decimals)
		{
			if (fraction[decimals..].Any(static ch => ch != '0'))
				throw new InvalidOperationException(
					$"Value '{value}' has more than {decimals} decimals.");
			fraction = fraction[..decimals];
		}
		fraction = fraction.PadRight(decimals, '0');
		var digits = (whole + fraction).TrimStart('0');
		return digits.IsEmpty()
			? BigInteger.Zero
			: BigInteger.Parse(digits, NumberStyles.Integer,
				CultureInfo.InvariantCulture);
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		var digits = value.ToString(CultureInfo.InvariantCulture);
		if (decimals > 0)
		{
			digits = digits.PadLeft(decimals + 1, '0');
			digits = digits.Insert(digits.Length - decimals, ".");
		}
		if (!decimal.TryParse(digits, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new OverflowException(
				"Token amount exceeds the supported decimal range.");
		return result;
	}

	public static OrcaMarket DecodeWhirlpool(string poolAddress,
		OrcaRpcAccount account)
	{
		ArgumentNullException.ThrowIfNull(account);
		if (!account.Owner.Equals(ProgramAddress, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Account '{poolAddress}' is not owned by Orca Whirlpools.");
		var reader = new OrcaDataReader(DecodeAccountData(account));
		reader.ReadDiscriminator(WhirlpoolDiscriminator, "whirlpool");
		var config = reader.ReadPublicKey();
		_ = reader.ReadByte();
		var tickSpacing = reader.ReadUInt16();
		var feeTierIndexSeed = reader.ReadUInt16();
		var feeRate = reader.ReadUInt16();
		_ = reader.ReadUInt16();
		var liquidity = reader.ReadUInt128();
		var sqrtPrice = reader.ReadUInt128();
		var currentTick = reader.ReadInt32();
		_ = reader.ReadUInt64();
		_ = reader.ReadUInt64();
		var mintA = reader.ReadPublicKey();
		var vaultA = reader.ReadPublicKey();
		reader.Skip(16);
		var mintB = reader.ReadPublicKey();
		var vaultB = reader.ReadPublicKey();
		reader.Skip(16);
		_ = reader.ReadUInt64();
		reader.Skip(3 * 128);
		if (reader.Position != reader.Length)
			throw new InvalidDataException(
				$"Orca whirlpool '{poolAddress}' has an unexpected data length.");
		if (tickSpacing == 0 || liquidity <= 0 ||
			sqrtPrice < MinimumSqrtPrice || sqrtPrice > MaximumSqrtPrice ||
			currentTick is < MinimumTickIndex or > MaximumTickIndex)
			throw new InvalidDataException(
				$"Orca whirlpool '{poolAddress}' contains invalid state.");
		return new()
		{
			PoolAddress = poolAddress.NormalizePublicKey(),
			WhirlpoolsConfig = config,
			TickSpacing = tickSpacing,
			FeeTierIndexSeed = feeTierIndexSeed,
			FeeRate = feeRate,
			Liquidity = liquidity,
			SqrtPrice = sqrtPrice,
			CurrentTickIndex = currentTick,
			TokenVaultA = vaultA,
			TokenVaultB = vaultB,
			TokenA = new() { Mint = mintA },
			TokenB = new() { Mint = mintB },
		};
	}

	public static OrcaToken DecodeMint(string mint, OrcaRpcAccount account,
		string symbol, string name, OrcaApiToken apiToken)
	{
		ArgumentNullException.ThrowIfNull(account);
		var data = DecodeAccountData(account);
		if (data.Length < 82)
			throw new InvalidDataException(
				$"Solana mint '{mint}' data is truncated.");
		if (!account.Owner.Equals(TokenProgramAddress,
				StringComparison.Ordinal) &&
			!account.Owner.Equals(Token2022ProgramAddress,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Account '{mint}' is not owned by an SPL token program.");
		var supply = BinaryPrimitives.ReadUInt64LittleEndian(
			data.AsSpan(36, sizeof(ulong)));
		var decimals = data[44];
		if (data[45] == 0)
			throw new InvalidDataException(
				$"Solana mint '{mint}' is not initialized.");
		if (apiToken is not null)
		{
			if (!apiToken.Address.Equals(mint, StringComparison.Ordinal) ||
				apiToken.Decimals != decimals ||
				!apiToken.ProgramId.Equals(account.Owner,
					StringComparison.Ordinal))
				throw new InvalidDataException(
					$"Orca API metadata for mint '{mint}' does not match " +
					"the on-chain account.");
			symbol ??= apiToken.Symbol;
			name ??= apiToken.Name;
		}
		return new()
		{
			Mint = mint.NormalizePublicKey(),
			Symbol = NormalizeSymbol(symbol, mint),
			Name = name.IsEmpty() ? NormalizeSymbol(symbol, mint) : name.Trim(),
			Decimals = decimals,
			Supply = supply,
			TokenProgram = account.Owner.NormalizePublicKey(),
			ExtensionTags = apiToken?.Tags ?? [],
			IsExtensionMetadataKnown = account.Owner.Equals(TokenProgramAddress,
				StringComparison.Ordinal) || apiToken is not null,
		};
	}

	public static (string Name, string Symbol) DecodeMetadata(
		OrcaRpcAccount account, string expectedMint)
	{
		if (account is null || !account.Owner.Equals(MetadataProgramAddress,
			StringComparison.Ordinal))
			return (null, null);
		var reader = new OrcaDataReader(DecodeAccountData(account));
		_ = reader.ReadByte();
		_ = reader.ReadPublicKey();
		var mint = reader.ReadPublicKey();
		if (!mint.Equals(expectedMint, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Metadata account belongs to mint '{mint}', not " +
				$"'{expectedMint}'.");
		var name = reader.ReadString(256).TrimEnd('\0').Trim();
		var symbol = reader.ReadString(64).TrimEnd('\0').Trim();
		return (name, symbol);
	}

	public static OrcaTickArray DecodeTickArray(string address,
		OrcaRpcAccount account, string expectedPool, int expectedStart)
	{
		if (account is null)
			return CreateEmptyTickArray(address, expectedStart);
		if (!account.Owner.Equals(ProgramAddress, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Tick array '{address}' is not owned by Orca Whirlpools.");
		var data = DecodeAccountData(account);
		var reader = new OrcaDataReader(data);
		if (data.AsSpan(0, 8).SequenceEqual(FixedTickArrayDiscriminator))
		{
			reader.Skip(8);
			var start = reader.ReadInt32();
			var ticks = new OrcaTick[TickArraySize];
			for (var index = 0; index < ticks.Length; index++)
			{
				ticks[index] = new()
				{
					IsInitialized = reader.ReadBoolean(),
					LiquidityNet = reader.ReadInt128(),
				};
				reader.Skip(96);
			}
			ValidateTickArray(address, expectedPool, expectedStart, start,
				reader.ReadPublicKey(), reader);
			return new()
			{
				Address = address.NormalizePublicKey(),
				StartTickIndex = start,
				Ticks = ticks,
			};
		}
		if (data.AsSpan(0, 8).SequenceEqual(DynamicTickArrayDiscriminator))
		{
			reader.Skip(8);
			var start = reader.ReadInt32();
			var pool = reader.ReadPublicKey();
			_ = reader.ReadUInt128();
			var ticks = new OrcaTick[TickArraySize];
			for (var index = 0; index < ticks.Length; index++)
			{
				var variant = reader.ReadByte();
				if (variant == 0)
					ticks[index] = new();
				else if (variant == 1)
				{
					ticks[index] = new()
					{
						IsInitialized = true,
						LiquidityNet = reader.ReadInt128(),
					};
					reader.Skip(96);
				}
				else
					throw new InvalidDataException(
						$"Dynamic Orca tick array '{address}' contains " +
						$"unknown tick variant '{variant}'.");
			}
			ValidateTickArray(address, expectedPool, expectedStart, start, pool,
				reader);
			return new()
			{
				Address = address.NormalizePublicKey(),
				StartTickIndex = start,
				Ticks = ticks,
			};
		}
		throw new InvalidDataException(
			$"Account '{address}' is not a supported Orca tick array.");
	}

	public static OrcaTickArray CreateEmptyTickArray(string address,
		int startTickIndex)
		=> new()
		{
			Address = address.NormalizePublicKey(),
			StartTickIndex = startTickIndex,
			Ticks = Enumerable.Range(0, TickArraySize).Select(static _ =>
				new OrcaTick()).ToArray(),
		};

	public static int GetTickArrayStartIndex(int tickIndex,
		ushort tickSpacing)
	{
		if (tickSpacing == 0)
			throw new ArgumentOutOfRangeException(nameof(tickSpacing));
		var spacing = (int)tickSpacing;
		return FloorDiv(FloorDiv(tickIndex, spacing), TickArraySize) *
			spacing * TickArraySize;
	}

	public static OrcaEvent[] DecodeEvents(string signature,
		IEnumerable<string> logs, DateTime time)
	{
		var result = new List<OrcaEvent>();
		foreach (var line in logs ?? [])
		{
			const string prefix = "Program data: ";
			var start = line?.IndexOf(prefix, StringComparison.Ordinal) ?? -1;
			if (start < 0)
				continue;
			var encoded = line[(start + prefix.Length)..].Trim();
			var separator = encoded.IndexOf(' ');
			if (separator >= 0)
				encoded = encoded[..separator];
			byte[] data;
			try
			{
				data = Convert.FromBase64String(encoded);
			}
			catch (FormatException)
			{
				continue;
			}
			if (data.Length != 121 ||
				!data.AsSpan(0, 8).SequenceEqual(TradedEventDiscriminator))
				continue;
			try
			{
				var reader = new OrcaDataReader(data);
				reader.Skip(8);
				var pool = reader.ReadPublicKey();
				var isAToB = reader.ReadBoolean();
				_ = reader.ReadUInt128();
				var postSqrtPrice = reader.ReadUInt128();
				var inputAmount = reader.ReadUInt64();
				var outputAmount = reader.ReadUInt64();
				var inputTransferFee = reader.ReadUInt64();
				var outputTransferFee = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				result.Add(new()
				{
					EventIndex = result.Count,
					Signature = signature,
					PoolAddress = pool,
					Time = time.ToUniversalTime(),
					IsAToB = isAToB,
					PostSqrtPrice = postSqrtPrice,
					InputAmount = inputAmount,
					OutputAmount = outputAmount,
					InputTransferFee = inputTransferFee,
					OutputTransferFee = outputTransferFee,
				});
			}
			catch (Exception error) when (error is InvalidDataException or
				ArgumentOutOfRangeException)
			{
			}
		}
		return [.. result];
	}

	public static OrcaQuote GetQuote(this OrcaMarket market, Sides side,
		BigInteger baseAmount, decimal slippageTolerance)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (baseAmount <= 0 || baseAmount > ulong.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(baseAmount));
		if (market.IsAdaptiveFee)
			throw new NotSupportedException(
				"Adaptive-fee Orca pools require live oracle fee state and are " +
				"not used for local executable quotes.");
		if (market.TokenA.IsTransferFeeEnabled ||
			market.TokenB.IsTransferFeeEnabled)
			throw new NotSupportedException(
				"Token-2022 transfer-fee pools require epoch-specific transfer " +
				"fee quoting.");
		if (market.TickArrays is not { Length: 5 })
			throw new InvalidOperationException(
				"Five Orca tick arrays are required for a swap quote.");
		var isSell = side == Sides.Sell;
		var amounts = ComputeSwap(market, baseAmount, isSell, isSell);
		var quoteAmount = amounts.TokenB;
		if (amounts.TokenA != baseAmount || quoteAmount <= 0 ||
			quoteAmount > ulong.MaxValue)
			throw new InvalidOperationException(
				"Orca could not quote the complete requested base amount.");
		var slippageBasisPoints = checked((int)(slippageTolerance * 100m));
		var quoteLimit = isSell
			? quoteAmount * (10_000 - slippageBasisPoints) / 10_000
			: CeilDiv(quoteAmount * (10_000 + slippageBasisPoints), 10_000);
		if (quoteLimit <= 0 || quoteLimit > ulong.MaxValue)
			throw new OverflowException(
				"Orca quote limit does not fit into an unsigned 64-bit value.");
		return new()
		{
			BaseAmount = baseAmount,
			QuoteAmount = quoteAmount,
			QuoteLimit = quoteLimit,
			IsAmountSpecifiedInput = isSell,
			IsAToB = isSell,
			TickArrayAddresses = [.. market.TickArrays.Select(static array =>
				array.Address)],
		};
	}

	public static byte[] DecodeAccountData(OrcaRpcAccount account)
	{
		ArgumentNullException.ThrowIfNull(account);
		if (account.Data is not { Length: >= 2 } data ||
			!data[1].Equals("base64", StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"Solana RPC account data is not base64 encoded.");
		try
		{
			return Convert.FromBase64String(data[0]);
		}
		catch (FormatException error)
		{
			throw new InvalidDataException(
				"Solana RPC returned invalid base64 account data.", error);
		}
	}

	public static int SqrtPriceToTickIndex(BigInteger sqrtPrice)
	{
		if (sqrtPrice < MinimumSqrtPrice || sqrtPrice > MaximumSqrtPrice)
			throw new ArgumentOutOfRangeException(nameof(sqrtPrice));
		var low = MinimumTickIndex;
		var high = MaximumTickIndex;
		while (low < high)
		{
			var middle = low + (high - low + 1) / 2;
			if (TickIndexToSqrtPrice(middle) <= sqrtPrice)
				low = middle;
			else
				high = middle - 1;
		}
		return low;
	}

	private static SwapAmounts ComputeSwap(OrcaMarket market,
		BigInteger tokenAmount, bool isAToB, bool isSpecifiedInput)
	{
		var amountRemaining = tokenAmount;
		var amountCalculated = BigInteger.Zero;
		var currentSqrtPrice = market.SqrtPrice;
		var currentTickIndex = market.CurrentTickIndex;
		var currentLiquidity = market.Liquidity;
		var sqrtPriceLimit = isAToB ? MinimumSqrtPrice : MaximumSqrtPrice;
		while (amountRemaining > 0 && currentSqrtPrice != sqrtPriceLimit)
		{
			var next = FindNextTick(market, currentTickIndex, isAToB);
			var nextTickSqrtPrice = TickIndexToSqrtPrice(next.Index);
			var targetSqrtPrice = isAToB
				? BigInteger.Max(nextTickSqrtPrice, sqrtPriceLimit)
				: BigInteger.Min(nextTickSqrtPrice, sqrtPriceLimit);
			var step = ComputeSwapStep(amountRemaining, market.FeeRate,
				currentLiquidity, currentSqrtPrice, targetSqrtPrice, isAToB,
				isSpecifiedInput);
			if (isSpecifiedInput)
			{
				amountRemaining -= step.AmountIn + step.FeeAmount;
				amountCalculated += step.AmountOut;
			}
			else
			{
				amountRemaining -= step.AmountOut;
				amountCalculated += step.AmountIn + step.FeeAmount;
			}
			if (amountRemaining < 0)
				throw new InvalidOperationException(
					"Orca quote arithmetic produced a negative remainder.");
			if (step.NextSqrtPrice == nextTickSqrtPrice)
			{
				if (next.Tick is not null)
					currentLiquidity = GetNextLiquidity(currentLiquidity,
						next.Tick.LiquidityNet, isAToB);
				currentTickIndex = isAToB ? next.Index - 1 : next.Index;
			}
			else if (step.NextSqrtPrice != currentSqrtPrice)
				currentTickIndex = SqrtPriceToTickIndex(step.NextSqrtPrice);
			if (step.NextSqrtPrice == currentSqrtPrice)
				throw new InvalidOperationException(
					"Orca quote made no progress through the pool.");
			currentSqrtPrice = step.NextSqrtPrice;
		}
		if (amountRemaining > 0)
			throw new InvalidOperationException(
				"The requested Orca quote exceeds the five tick-array coverage.");
		var swappedAmount = tokenAmount - amountRemaining;
		return isAToB == isSpecifiedInput
			? new(swappedAmount, amountCalculated)
			: new(amountCalculated, swappedAmount);
	}

	private static SwapStep ComputeSwapStep(BigInteger amountRemaining,
		uint feeRate, BigInteger liquidity, BigInteger currentSqrtPrice,
		BigInteger targetSqrtPrice, bool isAToB, bool isSpecifiedInput)
	{
		if (liquidity <= 0)
			throw new InvalidOperationException(
				"Orca pool has no active liquidity at the current tick.");
		var initialFixedDelta = GetAmountFixedDelta(currentSqrtPrice,
			targetSqrtPrice, liquidity, isAToB, isSpecifiedInput);
		var amountCalculated = isSpecifiedInput
			? ApplySwapFee(amountRemaining, feeRate)
			: amountRemaining;
		var nextSqrtPrice = initialFixedDelta <= amountCalculated
			? targetSqrtPrice
			: GetNextSqrtPrice(currentSqrtPrice, liquidity, amountCalculated,
				isAToB, isSpecifiedInput);
		var isMaximumSwap = nextSqrtPrice == targetSqrtPrice;
		var amountUnfixedDelta = GetAmountUnfixedDelta(currentSqrtPrice,
			nextSqrtPrice, liquidity, isAToB, isSpecifiedInput);
		var amountFixedDelta = isMaximumSwap
			? initialFixedDelta
			: GetAmountFixedDelta(currentSqrtPrice, nextSqrtPrice, liquidity,
				isAToB, isSpecifiedInput);
		var amountIn = isSpecifiedInput
			? amountFixedDelta
			: amountUnfixedDelta;
		var amountOut = isSpecifiedInput
			? amountUnfixedDelta
			: BigInteger.Min(amountFixedDelta, amountRemaining);
		var feeAmount = isSpecifiedInput && !isMaximumSwap
			? amountRemaining - amountIn
			: ReverseApplySwapFee(amountIn, feeRate) - amountIn;
		return new(amountIn, amountOut, nextSqrtPrice, feeAmount);
	}

	private static BigInteger GetAmountFixedDelta(BigInteger current,
		BigInteger target, BigInteger liquidity, bool isAToB,
		bool isSpecifiedInput)
		=> isAToB == isSpecifiedInput
			? GetAmountDeltaA(current, target, liquidity, isSpecifiedInput)
			: GetAmountDeltaB(current, target, liquidity, isSpecifiedInput);

	private static BigInteger GetAmountUnfixedDelta(BigInteger current,
		BigInteger target, BigInteger liquidity, bool isAToB,
		bool isSpecifiedInput)
		=> isSpecifiedInput == isAToB
			? GetAmountDeltaB(current, target, liquidity, !isSpecifiedInput)
			: GetAmountDeltaA(current, target, liquidity, !isSpecifiedInput);

	private static BigInteger GetAmountDeltaA(BigInteger first,
		BigInteger second, BigInteger liquidity, bool isRoundUp)
	{
		var lower = BigInteger.Min(first, second);
		var upper = BigInteger.Max(first, second);
		var numerator = liquidity * (upper - lower) << 64;
		var denominator = lower * upper;
		return isRoundUp
			? CeilDiv(numerator, denominator)
			: numerator / denominator;
	}

	private static BigInteger GetAmountDeltaB(BigInteger first,
		BigInteger second, BigInteger liquidity, bool isRoundUp)
	{
		var lower = BigInteger.Min(first, second);
		var upper = BigInteger.Max(first, second);
		var product = liquidity * (upper - lower);
		return isRoundUp ? CeilDiv(product, BigInteger.One << 64) :
			product >> 64;
	}

	private static BigInteger GetNextSqrtPrice(BigInteger current,
		BigInteger liquidity, BigInteger amount, bool isAToB,
		bool isSpecifiedInput)
		=> isSpecifiedInput == isAToB
			? GetNextSqrtPriceFromA(current, liquidity, amount,
				isSpecifiedInput)
			: GetNextSqrtPriceFromB(current, liquidity, amount,
				isSpecifiedInput);

	private static BigInteger GetNextSqrtPriceFromA(BigInteger current,
		BigInteger liquidity, BigInteger amount, bool isSpecifiedInput)
	{
		if (amount == 0)
			return current;
		var product = current * amount;
		var shiftedLiquidity = liquidity << 64;
		var denominator = isSpecifiedInput
			? shiftedLiquidity + product
			: shiftedLiquidity - product;
		if (denominator <= 0)
			throw new InvalidOperationException(
				"Orca quote would move beyond the supported price range.");
		var result = CeilDiv(liquidity * current << 64, denominator);
		ValidateSqrtPrice(result);
		return result;
	}

	private static BigInteger GetNextSqrtPriceFromB(BigInteger current,
		BigInteger liquidity, BigInteger amount, bool isSpecifiedInput)
	{
		if (amount == 0)
			return current;
		var shiftedAmount = amount << 64;
		var delta = isSpecifiedInput
			? shiftedAmount / liquidity
			: CeilDiv(shiftedAmount, liquidity);
		var result = isSpecifiedInput ? current + delta : current - delta;
		ValidateSqrtPrice(result);
		return result;
	}

	private static IndexedTick FindNextTick(OrcaMarket market,
		int currentTickIndex, bool isAToB)
	{
		var arrays = market.TickArrays.OrderBy(static array =>
			array.StartTickIndex).ToArray();
		var start = Math.Max(MinimumTickIndex, arrays[0].StartTickIndex);
		var end = Math.Min(MaximumTickIndex,
			arrays[^1].StartTickIndex + TickArraySize * market.TickSpacing - 1);
		IndexedTick? result = null;
		foreach (var array in arrays)
			for (var offset = 0; offset < array.Ticks.Length; offset++)
			{
				var tick = array.Ticks[offset];
				if (!tick.IsInitialized)
					continue;
				var index = array.StartTickIndex + offset * market.TickSpacing;
				if (isAToB && index <= FloorInitializableTick(currentTickIndex,
						market.TickSpacing) &&
					(result is null || index > result.Value.Index) ||
					!isAToB && index > currentTickIndex &&
					(result is null || index < result.Value.Index))
					result = new(index, tick);
			}
		if (result is not null)
			return result.Value;
		if (isAToB && currentTickIndex < start ||
			!isAToB && currentTickIndex >= end)
			throw new InvalidOperationException(
				"Orca quote exhausted the available tick arrays.");
		return new(isAToB ? start : end, null);
	}

	private static BigInteger GetNextLiquidity(BigInteger liquidity,
		BigInteger liquidityNet, bool isAToB)
	{
		var result = isAToB ? liquidity - liquidityNet :
			liquidity + liquidityNet;
		if (result < 0)
			throw new InvalidDataException(
				"Orca tick liquidity transition underflowed.");
		return result;
	}

	private static BigInteger ApplySwapFee(BigInteger amount, uint feeRate)
	{
		const uint denominator = 1_000_000;
		if (feeRate >= denominator)
			throw new InvalidDataException("Orca pool fee rate is invalid.");
		return amount * (denominator - feeRate) / denominator;
	}

	private static BigInteger ReverseApplySwapFee(BigInteger amount,
		uint feeRate)
	{
		const uint denominator = 1_000_000;
		if (feeRate >= denominator)
			throw new InvalidDataException("Orca pool fee rate is invalid.");
		return CeilDiv(amount * denominator, denominator - feeRate);
	}

	private static BigInteger TickIndexToSqrtPrice(int tickIndex)
	{
		if (tickIndex is < MinimumTickIndex or > MaximumTickIndex)
			throw new ArgumentOutOfRangeException(nameof(tickIndex));
		return tickIndex >= 0
			? PositiveTickToSqrtPrice(tickIndex)
			: NegativeTickToSqrtPrice(tickIndex);
	}

	private static BigInteger PositiveTickToSqrtPrice(int tick)
	{
		var ratio = BigInteger.Parse(tick % 2 != 0
			? "79232123823359799118286999567"
			: "79228162514264337593543950336",
			CultureInfo.InvariantCulture);
		var multipliers = new[]
		{
			"79236085330515764027303304731",
			"79244008939048815603706035061",
			"79259858533276714757314932305",
			"79291567232598584799939703904",
			"79355022692464371645785046466",
			"79482085999252804386437311141",
			"79736823300114093921829183326",
			"80248749790819932309965073892",
			"81282483887344747381513967011",
			"83390072131320151908154831281",
			"87770609709833776024991924138",
			"97234110755111693312479820773",
			"119332217159966728226237229890",
			"179736315981702064433883588727",
			"407748233172238350107850275304",
			"2098478828474011932436660412517",
			"55581415166113811149459800483533",
			"38992368544603139932233054999993551",
		};
		for (var bit = 1; bit <= multipliers.Length; bit++)
			if ((tick & (1 << bit)) != 0)
				ratio = ratio * BigInteger.Parse(multipliers[bit - 1],
					CultureInfo.InvariantCulture) >> 96;
		return ratio >> 32;
	}

	private static BigInteger NegativeTickToSqrtPrice(int tick)
	{
		var absolute = Math.Abs(tick);
		var ratio = BigInteger.Parse(absolute % 2 != 0
			? "18445821805675392311"
			: "18446744073709551616", CultureInfo.InvariantCulture);
		var multipliers = new[]
		{
			"18444899583751176498", "18443055278223354162",
			"18439367220385604838", "18431993317065449817",
			"18417254355718160513", "18387811781193591352",
			"18329067761203520168", "18212142134806087854",
			"17980523815641551639", "17526086738831147013",
			"16651378430235024244", "15030750278693429944",
			"12247334978882834399", "8131365268884726200",
			"3584323654723342297", "696457651847595233",
			"26294789957452057", "37481735321082",
		};
		for (var bit = 1; bit <= multipliers.Length; bit++)
			if ((absolute & (1 << bit)) != 0)
				ratio = ratio * BigInteger.Parse(multipliers[bit - 1],
					CultureInfo.InvariantCulture) >> 64;
		return ratio;
	}

	private static int FloorInitializableTick(int tickIndex,
		ushort tickSpacing)
		=> FloorDiv(tickIndex, tickSpacing) * tickSpacing;

	private static int FloorDiv(int value, int divisor)
	{
		var quotient = value / divisor;
		var remainder = value % divisor;
		return remainder < 0 ? quotient - 1 : quotient;
	}

	private static BigInteger CeilDiv(BigInteger numerator,
		BigInteger denominator)
	{
		if (numerator < 0 || denominator <= 0)
			throw new ArgumentOutOfRangeException(nameof(denominator));
		return (numerator + denominator - 1) / denominator;
	}

	private static void ValidateSqrtPrice(BigInteger sqrtPrice)
	{
		if (sqrtPrice < MinimumSqrtPrice || sqrtPrice > MaximumSqrtPrice)
			throw new InvalidOperationException(
				"Orca quote would move beyond the supported price range.");
	}

	private static void ValidateTickArray(string address, string expectedPool,
		int expectedStart, int actualStart, string actualPool,
		OrcaDataReader reader)
	{
		if (reader.Position != reader.Length)
			throw new InvalidDataException(
				$"Orca tick array '{address}' has trailing data.");
		if (actualStart != expectedStart ||
			!actualPool.Equals(expectedPool, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Orca tick array '{address}' does not match its expected pool " +
				"or start tick.");
	}

	private static string NormalizeSymbol(string value, string mint)
	{
		if (mint.Equals(WrappedSolMint, StringComparison.Ordinal))
			return "SOL";
		if (mint.Equals(UsdcMint, StringComparison.Ordinal))
			return "USDC";
		value = value?.Trim();
		if (!value.IsEmpty())
		{
			var sanitized = new string(value.Where(static ch =>
				char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-').ToArray());
			if (!sanitized.IsEmpty())
				return sanitized.ToUpperInvariant();
		}
		return mint[..6].ToUpperInvariant();
	}
}
