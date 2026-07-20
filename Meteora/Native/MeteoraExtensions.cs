namespace StockSharp.Meteora.Native;

static class MeteoraExtensions
{
	public const string ProgramAddress =
		"LBUZKhRxPF3XUpBCjp4YzTKgLccjZhTSDM9YuVaPwxo";
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
	public const string RentSysvarAddress =
		"SysvarRent111111111111111111111111111111111";
	public const string WrappedSolMint =
		"So11111111111111111111111111111111111111112";
	public const string UsdcMint =
		"EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";

	public const int BinArraySize = 70;
	public const int MinimumInternalBinArrayIndex = -512;
	public const int MaximumInternalBinArrayIndex = 511;
	private const int _scaleOffset = 64;
	private const int _basisPointMaximum = 10_000;
	private static readonly BigInteger _scale = BigInteger.One << _scaleOffset;
	private static readonly BigInteger _u128Maximum =
		(BigInteger.One << 128) - BigInteger.One;
	private static readonly BigInteger _feePrecision = 1_000_000_000;
	private static readonly BigInteger _maximumFeeRate = 100_000_000;

	public static readonly byte[] LbPairDiscriminator =
		[33, 11, 49, 98, 181, 101, 177, 13];
	public static readonly byte[] BinArrayDiscriminator =
		[92, 142, 92, 220, 5, 148, 70, 181];
	public static readonly byte[] SwapDiscriminator =
		[81, 108, 227, 190, 205, 208, 10, 196];
	public static readonly byte[] Swap2EventDiscriminator =
		[46, 116, 82, 215, 148, 27, 84, 77];
	public static readonly byte[] EventCpiDiscriminator =
		[228, 69, 165, 46, 81, 203, 154, 29];
	public static readonly byte[] Swap2InstructionDiscriminator =
		[65, 75, 63, 76, 235, 91, 91, 136];
	public static readonly byte[] SwapExactOut2InstructionDiscriminator =
		[43, 215, 247, 132, 137, 60, 243, 81];
	public static readonly byte[] InitializeBinArrayDiscriminator =
		[35, 86, 19, 185, 78, 212, 75, 211];
	public static readonly byte[] PlaceLimitOrderDiscriminator =
		[108, 176, 33, 186, 146, 229, 1, 197];
	public static readonly byte[] CancelLimitOrderDiscriminator =
		[132, 156, 132, 31, 67, 40, 232, 97];
	public static readonly byte[] CloseLimitOrderDiscriminator =
		[57, 124, 36, 155, 126, 249, 93, 171];

	private static readonly Dictionary<TimeSpan, string> _timeFrameCodes = new()
	{
		[TimeSpan.FromMinutes(5)] = "5m",
		[TimeSpan.FromMinutes(30)] = "30m",
		[TimeSpan.FromHours(1)] = "1h",
		[TimeSpan.FromHours(2)] = "2h",
		[TimeSpan.FromHours(4)] = "4h",
		[TimeSpan.FromHours(12)] = "12h",
		[TimeSpan.FromDays(1)] = "24h",
	};

	public static TimeSpan[] TimeFrames => [.. _timeFrameCodes.Keys];

	private readonly record struct BinFill(BigInteger AmountIn,
		BigInteger AmountOut);

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

	public static string BinArrayAddress(string poolAddress, long index)
	{
		var indexBytes = new byte[sizeof(long)];
		BinaryPrimitives.WriteInt64LittleEndian(indexBytes, index);
		return FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("bin_array"),
			poolAddress.ToPublicKey().KeyBytes, indexBytes);
	}

	public static string BitmapExtensionAddress(string poolAddress)
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("bitmap"),
			poolAddress.ToPublicKey().KeyBytes);

	public static string EventAuthorityAddress()
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("__event_authority"));

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

	public static string GetRpcEndpoint(this MeteoraClusters cluster)
		=> cluster switch
		{
			MeteoraClusters.Mainnet => "https://api.mainnet-beta.solana.com",
			MeteoraClusters.Devnet => "https://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static string GetSocketEndpoint(this MeteoraClusters cluster)
		=> cluster switch
		{
			MeteoraClusters.Mainnet => "wss://api.mainnet-beta.solana.com",
			MeteoraClusters.Devnet => "wss://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static SecurityId ToStockSharp(this MeteoraMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Meteora,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string GetTimeFrameCode(this TimeSpan timeFrame)
		=> _timeFrameCodes.TryGetValue(timeFrame, out var code)
			? code
			: throw new NotSupportedException(
				$"Meteora does not publish {timeFrame} OHLCV candles.");

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

	public static MeteoraMarket DecodeLbPair(string poolAddress,
		MeteoraRpcAccount account)
	{
		ArgumentNullException.ThrowIfNull(account);
		if (!account.Owner.Equals(ProgramAddress, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Account '{poolAddress}' is not owned by Meteora DLMM.");
		var reader = new MeteoraDataReader(DecodeAccountData(account));
		reader.ReadDiscriminator(LbPairDiscriminator, "DLMM pool");
		var parameters = new MeteoraStaticParameters
		{
			BaseFactor = reader.ReadUInt16(),
			FilterPeriod = reader.ReadUInt16(),
			DecayPeriod = reader.ReadUInt16(),
			ReductionFactor = reader.ReadUInt16(),
			VariableFeeControl = reader.ReadUInt32(),
			MaximumVolatilityAccumulator = reader.ReadUInt32(),
			MinimumBinId = reader.ReadInt32(),
			MaximumBinId = reader.ReadInt32(),
			ProtocolShare = reader.ReadUInt16(),
			BaseFeePowerFactor = reader.ReadByte(),
			FunctionType = (MeteoraFunctionTypes)reader.ReadByte(),
			CollectFeeMode = (MeteoraCollectFeeModes)reader.ReadByte(),
		};
		reader.Skip(3);
		var variableParameters = new MeteoraVariableParameters
		{
			VolatilityAccumulator = reader.ReadUInt32(),
			VolatilityReference = reader.ReadUInt32(),
			IndexReference = reader.ReadInt32(),
		};
		reader.Skip(4);
		variableParameters.LastUpdateTimestamp = reader.ReadInt64();
		reader.Skip(8);
		reader.Skip(1 + 2);
		var pairType = (MeteoraPairTypes)reader.ReadByte();
		var activeId = reader.ReadInt32();
		var binStep = reader.ReadUInt16();
		var state = (MeteoraPairStates)reader.ReadByte();
		reader.Skip(1 + 2 + 1 + 1);
		var mintX = reader.ReadPublicKey();
		var mintY = reader.ReadPublicKey();
		var reserveX = reader.ReadPublicKey();
		var reserveY = reader.ReadPublicKey();
		reader.Skip(16 + 32);
		var rewards = new string[2];
		for (var index = 0; index < rewards.Length; index++)
		{
			rewards[index] = reader.ReadPublicKey();
			reader.Skip(112);
		}
		var oracle = reader.ReadPublicKey();
		reader.Skip(16 * sizeof(ulong));
		_ = reader.ReadInt64();
		reader.Skip(32 + 32 + 32 + 8 + 8 + 8 + 8 + 32 + 1 + 1 + 1 + 21);
		if (reader.Position != reader.Length || reader.Length != 904)
			throw new InvalidDataException(
				$"Meteora pool '{poolAddress}' has an unexpected data layout.");
		if (binStep == 0 || activeId < parameters.MinimumBinId ||
			activeId > parameters.MaximumBinId || parameters.ProtocolShare > 10_000 ||
			!Enum.IsDefined(parameters.FunctionType) ||
			!Enum.IsDefined(parameters.CollectFeeMode) ||
			!Enum.IsDefined(pairType) || !Enum.IsDefined(state))
			throw new InvalidDataException(
				$"Meteora pool '{poolAddress}' contains invalid state.");
		return new()
		{
			PoolAddress = poolAddress.NormalizePublicKey(),
			Parameters = parameters,
			VariableParameters = variableParameters,
			PairType = pairType,
			ActiveId = activeId,
			BinStep = binStep,
			State = state,
			TokenVaultX = reserveX,
			TokenVaultY = reserveY,
			Oracle = oracle,
			BitmapExtension = BitmapExtensionAddress(poolAddress),
			RewardMints = rewards,
			TokenX = new() { Mint = mintX },
			TokenY = new() { Mint = mintY },
		};
	}

	public static MeteoraToken DecodeMint(string mint, MeteoraRpcAccount account,
		string symbol, string name, MeteoraApiToken apiToken)
	{
		ArgumentNullException.ThrowIfNull(account);
		var data = DecodeAccountData(account);
		if (data.Length < 82)
			throw new InvalidDataException(
				$"Solana mint '{mint}' data is truncated.");
		if (!account.Owner.Equals(TokenProgramAddress, StringComparison.Ordinal) &&
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
				apiToken.Decimals != decimals)
				throw new InvalidDataException(
					$"Meteora API metadata for mint '{mint}' does not match " +
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
			AccountLength = data.Length,
		};
	}

	public static (string Name, string Symbol) DecodeMetadata(
		MeteoraRpcAccount account, string expectedMint)
	{
		if (account is null || !account.Owner.Equals(MetadataProgramAddress,
			StringComparison.Ordinal))
			return (null, null);
		var reader = new MeteoraDataReader(DecodeAccountData(account));
		_ = reader.ReadByte();
		_ = reader.ReadPublicKey();
		var mint = reader.ReadPublicKey();
		if (!mint.Equals(expectedMint, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Metadata account belongs to mint '{mint}', not '{expectedMint}'.");
		var name = reader.ReadString(256).TrimEnd('\0').Trim();
		var symbol = reader.ReadString(64).TrimEnd('\0').Trim();
		return (name, symbol);
	}

	public static MeteoraBinArray DecodeBinArray(string address,
		MeteoraRpcAccount account, string expectedPool, long expectedIndex)
	{
		if (account is null)
			return null;
		if (!account.Owner.Equals(ProgramAddress, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Bin array '{address}' is not owned by Meteora DLMM.");
		var reader = new MeteoraDataReader(DecodeAccountData(account));
		reader.ReadDiscriminator(BinArrayDiscriminator, "bin array");
		var index = reader.ReadInt64();
		var version = reader.ReadByte();
		reader.Skip(7);
		var pool = reader.ReadPublicKey();
		if (index != expectedIndex ||
			!pool.Equals(expectedPool, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Meteora bin array '{address}' belongs to another range or pool.");
		var bins = new MeteoraBin[BinArraySize];
		for (var offset = 0; offset < bins.Length; offset++)
		{
			var amountX = reader.ReadUInt64();
			var amountY = reader.ReadUInt64();
			var price = reader.ReadUInt128();
			var liquiditySupply = reader.ReadUInt128();
			reader.Skip(4 * sizeof(ulong) + 2 * 16);
			var openOrderAmount = reader.ReadUInt64();
			_ = reader.ReadUInt64();
			var processedOrderRemainingAmount = reader.ReadUInt64();
			_ = reader.ReadUInt32();
			var isAsk = reader.ReadByte() != 0;
			reader.Skip(3);
			var id = checked(index * BinArraySize + offset);
			if (id is < int.MinValue or > int.MaxValue)
				throw new InvalidDataException(
					$"Meteora bin array '{address}' has an invalid index.");
			bins[offset] = new()
			{
				Id = (int)id,
				AmountX = amountX,
				AmountY = amountY,
				Price = price,
				LiquiditySupply = liquiditySupply,
				OpenOrderAmount = openOrderAmount,
				ProcessedOrderRemainingAmount = processedOrderRemainingAmount,
				IsLimitOrderAskSide = isAsk,
			};
		}
		if (reader.Position != reader.Length || reader.Length != 10136)
			throw new InvalidDataException(
				$"Meteora bin array '{address}' has an unexpected data layout.");
		return new()
		{
			Address = address.NormalizePublicKey(),
			Index = index,
			Version = version,
			Bins = bins,
		};
	}

	public static MeteoraEvent[] DecodeEvents(string signature,
		MeteoraRpcTransaction transaction, DateTime time)
	{
		var payloads = new List<byte[]>();
		var fingerprints = new HashSet<string>(StringComparer.Ordinal);
		foreach (var group in transaction?.Meta?.InnerInstructions ?? [])
			foreach (var instruction in group?.Instructions ?? [])
			{
				if (instruction?.Data.IsEmpty() != false)
					continue;
				try
				{
					var data = Encoders.Base58.DecodeData(instruction.Data);
					if (data.Length > 16 && data.AsSpan(0, 8).SequenceEqual(
						EventCpiDiscriminator) &&
						fingerprints.Add(Convert.ToBase64String(data)))
						payloads.Add(data.AsSpan(8).ToArray());
				}
				catch (Exception error) when (error is FormatException or
					ArgumentException)
				{
				}
			}
		foreach (var line in transaction?.Meta?.LogMessages ?? [])
		{
			const string prefix = "Program data: ";
			var start = line?.IndexOf(prefix, StringComparison.Ordinal) ?? -1;
			if (start < 0)
				continue;
			var encoded = line[(start + prefix.Length)..].Trim();
			var separator = encoded.IndexOf(' ');
			if (separator >= 0)
				encoded = encoded[..separator];
			try
			{
				var data = Convert.FromBase64String(encoded);
				if (data.Length > 8 && fingerprints.Add(
					Convert.ToBase64String(data)))
					payloads.Add(data);
			}
			catch (FormatException)
			{
			}
		}
		var result = new List<MeteoraEvent>();
		foreach (var data in payloads)
		{
			try
			{
				var reader = new MeteoraDataReader(data);
				var isSwap2 = data.AsSpan(0, 8).SequenceEqual(
					Swap2EventDiscriminator);
				var isSwap = data.AsSpan(0, 8).SequenceEqual(SwapDiscriminator);
				if (!isSwap2 && !isSwap)
					continue;
				reader.Skip(8);
				var pool = reader.ReadPublicKey();
				_ = reader.ReadPublicKey();
				var startBinId = reader.ReadInt32();
				var endBinId = reader.ReadInt32();
				ulong amountIn;
				ulong amountLeft;
				ulong amountOut;
				bool swapForY;
				if (isSwap2)
				{
					swapForY = reader.ReadBoolean();
					_ = reader.ReadUInt128();
					amountIn = reader.ReadUInt64();
					amountLeft = reader.ReadUInt64();
					amountOut = reader.ReadUInt64();
				}
				else
				{
					amountIn = reader.ReadUInt64();
					amountOut = reader.ReadUInt64();
					swapForY = reader.ReadBoolean();
					amountLeft = 0;
				}
				result.Add(new()
				{
					EventIndex = result.Count,
					Signature = signature,
					PoolAddress = pool,
					Time = time.ToUniversalTime(),
					IsSwapForY = swapForY,
					StartBinId = startBinId,
					EndBinId = endBinId,
					InputAmount = amountIn,
					InputAmountLeft = amountLeft,
					OutputAmount = amountOut,
				});
			}
			catch (Exception error) when (error is InvalidDataException or
				ArgumentOutOfRangeException or OverflowException)
			{
			}
		}
		return [.. result];
	}

	public static MeteoraQuote GetQuote(this MeteoraMarket market, Sides side,
		BigInteger baseAmount, decimal slippagePercent)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (baseAmount <= 0 || baseAmount > ulong.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(baseAmount));
		if (slippagePercent is <= 0 or > 50)
			throw new ArgumentOutOfRangeException(nameof(slippagePercent));
		if (market.BinArrays.Length == 0)
			throw new InvalidOperationException(
				$"Meteora pool '{market.PoolAddress}' has no loaded bin arrays.");
		var swapForY = side == Sides.Sell;
		var isExactInput = side == Sides.Sell;
		var slippageBps = checked((int)decimal.Round(slippagePercent * 100m,
			0, MidpointRounding.AwayFromZero));
		var variables = market.VariableParameters.Clone();
		UpdateReference(market.ActiveId, variables, market.Parameters);
		var pathArrays = market.BinArrays
			.Where(array => swapForY
				? array.Index <= GetBinArrayIndex(market.ActiveId)
				: array.Index >= GetBinArrayIndex(market.ActiveId))
			.OrderBy(array => swapForY ? -array.Index : array.Index)
			.ToArray();
		var bins = pathArrays.SelectMany(array => array.Bins.Select(bin =>
			(Array: array, Bin: bin)))
			.Where(item => swapForY
				? item.Bin.Id <= market.ActiveId
				: item.Bin.Id >= market.ActiveId)
			.OrderBy(item => swapForY ? -item.Bin.Id : item.Bin.Id)
			.ToArray();
		var remaining = baseAmount;
		BigInteger inputTotal = 0;
		BigInteger outputTotal = 0;
		var used = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in bins)
		{
			if (remaining <= 0)
				break;
			if (GetMaximumOutput(item.Bin, swapForY,
				market.IsLimitOrderPool) <= 0)
				continue;
			UpdateVolatilityAccumulator(variables, market.Parameters,
				item.Bin.Id);
			BinFill fill;
			if (isExactInput)
			{
				fill = QuoteExactInputAtBin(item.Bin, market, variables,
					remaining, swapForY);
				if (fill.AmountIn <= 0)
					continue;
				remaining -= fill.AmountIn;
			}
			else
			{
				fill = QuoteExactOutputAtBin(item.Bin, market, variables,
					remaining, swapForY);
				if (fill.AmountOut <= 0)
					continue;
				remaining -= fill.AmountOut;
			}
			inputTotal += fill.AmountIn;
			outputTotal += fill.AmountOut;
			used.Add(item.Array.Address);
		}
		if (remaining > 0 || inputTotal <= 0 || outputTotal <= 0)
			throw new InvalidOperationException(
				$"Meteora pool '{market.PoolAddress}' has insufficient loaded " +
				"liquidity for the requested quote.");
		var usedArrays = pathArrays.Where(array => used.Contains(array.Address))
			.ToList();
		var lastUsed = usedArrays.Count == 0
			? -1
			: Array.IndexOf(pathArrays, usedArrays[^1]);
		if (lastUsed >= 0 && lastUsed + 1 < pathArrays.Length)
			usedArrays.Add(pathArrays[lastUsed + 1]);
		if (usedArrays.Count == 0)
			throw new InvalidDataException("Meteora quote selected no bin arrays.");
		if (isExactInput)
		{
			var minimumOut = outputTotal * (_basisPointMaximum - slippageBps) /
				_basisPointMaximum;
			return new()
			{
				BaseAmount = baseAmount,
				QuoteAmount = outputTotal,
				OtherAmountThreshold = minimumOut,
				IsExactInput = true,
				IsSwapForY = true,
				BinArrayAddresses = [.. usedArrays.Select(static array =>
					array.Address)],
			};
		}
		var maximumIn = CeilingDiv(inputTotal *
			(_basisPointMaximum + slippageBps), _basisPointMaximum);
		return new()
		{
			BaseAmount = baseAmount,
			QuoteAmount = inputTotal,
			OtherAmountThreshold = maximumIn,
			IsExactInput = false,
			IsSwapForY = false,
			BinArrayAddresses = [.. usedArrays.Select(static array =>
				array.Address)],
		};
	}

	public static (QuoteChange[] Bids, QuoteChange[] Asks) GetBook(
		this MeteoraMarket market, int depth)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (depth <= 0)
			throw new ArgumentOutOfRangeException(nameof(depth));
		var bids = new Dictionary<decimal, decimal>();
		var asks = new Dictionary<decimal, decimal>();
		foreach (var bin in market.BinArrays.SelectMany(static array => array.Bins))
		{
			if (bin.Price <= 0)
				continue;
			var price = ToHumanPrice(bin.Price, market.TokenX.Decimals,
				market.TokenY.Decimals);
			if (price <= 0)
				continue;
			var orderAmount = market.IsLimitOrderPool
				? new BigInteger(bin.OpenOrderAmount) +
					bin.ProcessedOrderRemainingAmount
				: BigInteger.Zero;
			var bidQuote = new BigInteger(bin.AmountY) +
				(!bin.IsLimitOrderAskSide ? orderAmount : 0);
			var askBase = new BigInteger(bin.AmountX) +
				(bin.IsLimitOrderAskSide ? orderAmount : 0);
			if (bidQuote > 0)
			{
				var volume = bidQuote.FromBaseUnits(market.TokenY.Decimals) /
					price;
				if (volume > 0)
					bids[price] = bids.GetValueOrDefault(price) + volume;
			}
			if (askBase > 0)
			{
				var volume = askBase.FromBaseUnits(market.TokenX.Decimals);
				if (volume > 0)
					asks[price] = asks.GetValueOrDefault(price) + volume;
			}
		}
		return (
			[.. bids.OrderByDescending(static pair => pair.Key).Take(depth)
				.Select(static pair => new QuoteChange(pair.Key, pair.Value))],
			[.. asks.OrderBy(static pair => pair.Key).Take(depth)
				.Select(static pair => new QuoteChange(pair.Key, pair.Value))]);
	}

	public static long GetBinArrayIndex(int binId)
	{
		var quotient = binId / BinArraySize;
		var remainder = binId % BinArraySize;
		return binId < 0 && remainder != 0 ? quotient - 1L : quotient;
	}

	public static int PriceToBinId(this MeteoraMarket market, decimal price,
		Sides side)
	{
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price));
		var decimalScale = Math.Pow(10,
			market.TokenX.Decimals - market.TokenY.Decimals);
		var raw = (double)price / decimalScale;
		var value = Math.Log(raw) / Math.Log(1d + market.BinStep / 10_000d);
		if (double.IsNaN(value) || double.IsInfinity(value))
			throw new InvalidOperationException(
				$"Price '{price}' cannot be represented by Meteora bins.");
		var rounded = side == Sides.Sell ? Math.Ceiling(value) : Math.Floor(value);
		return checked((int)Math.Clamp(rounded,
			market.Parameters.MinimumBinId, market.Parameters.MaximumBinId));
	}

	public static BigInteger GetRawPrice(ushort binStep, int binId)
	{
		var basePrice = _scale + ((BigInteger)binStep << _scaleOffset) /
			_basisPointMaximum;
		var exponent = Math.Abs((long)binId);
		if (exponent > 0x80000)
			return BigInteger.Zero;
		var invert = binId < 0;
		var squaredBase = basePrice;
		var result = _scale;
		if (squaredBase >= result)
		{
			squaredBase = _u128Maximum / squaredBase;
			invert = !invert;
		}
		for (var bit = 0; bit < 20; bit++)
		{
			if ((exponent & (1L << bit)) != 0)
				result = result * squaredBase >> _scaleOffset;
			if (bit < 19)
				squaredBase = squaredBase * squaredBase >> _scaleOffset;
		}
		if (result.IsZero)
			return BigInteger.Zero;
		return invert ? _u128Maximum / result : result;
	}

	public static BigInteger GetLimitOrderInputAmount(
		this MeteoraMarket market, Sides side, BigInteger baseAmount, int binId)
	{
		if (baseAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(baseAmount));
		if (side == Sides.Sell)
			return baseAmount;
		var price = GetRawPrice(market.BinStep, binId);
		if (price <= 0)
			throw new InvalidOperationException(
				$"Meteora bin '{binId}' has an invalid price.");
		return Divide(baseAmount * price, _scale, true);
	}

	public static decimal ToHumanPrice(BigInteger rawPrice, int decimalsX,
		int decimalsY)
	{
		var value = (double)rawPrice / Math.Pow(2, _scaleOffset) *
			Math.Pow(10, decimalsX - decimalsY);
		if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value) ||
			value > (double)decimal.MaxValue)
			throw new OverflowException("Meteora bin price is outside decimal range.");
		return (decimal)value;
	}

	public static byte[] DecodeAccountData(MeteoraRpcAccount account)
	{
		ArgumentNullException.ThrowIfNull(account);
		if (account.Data is not { Length: > 0 } || account.Data[0].IsEmpty())
			throw new InvalidDataException("Solana RPC account data is missing.");
		try
		{
			return Convert.FromBase64String(account.Data[0]);
		}
		catch (FormatException error)
		{
			throw new InvalidDataException(
				"Solana RPC account data is not valid base64.", error);
		}
	}

	private static BinFill QuoteExactInputAtBin(MeteoraBin bin,
		MeteoraMarket market, MeteoraVariableParameters variables,
		BigInteger includedInput, bool swapForY)
	{
		var feeRate = GetTotalFee(market.BinStep, market.Parameters, variables);
		var feeOnInput = IsFeeOnInput(market.Parameters.CollectFeeMode, swapForY);
		var excludedInput = includedInput;
		if (feeOnInput)
			excludedInput = GetExcludedFeeAmount(includedInput, feeRate).Amount;
		var fill = FillWithoutFees(bin, excludedInput, swapForY,
			market.IsLimitOrderPool);
		if (fill.AmountIn <= 0 || fill.AmountOut <= 0)
			return default;
		var actualIncludedInput = fill.AmountIn;
		if (feeOnInput)
			actualIncludedInput = GetIncludedFeeAmount(fill.AmountIn, feeRate).Amount;
		var output = fill.AmountOut;
		if (!feeOnInput)
			output = GetExcludedFeeAmount(output, feeRate).Amount;
		return new(BigInteger.Min(actualIncludedInput, includedInput), output);
	}

	private static BinFill QuoteExactOutputAtBin(MeteoraBin bin,
		MeteoraMarket market, MeteoraVariableParameters variables,
		BigInteger desiredOutput, bool swapForY)
	{
		var maximum = QuoteExactInputAtBin(bin, market, variables,
			ulong.MaxValue, swapForY);
		if (maximum.AmountOut <= 0)
			return default;
		if (maximum.AmountOut <= desiredOutput)
			return maximum;
		BigInteger lower = 1;
		var upper = maximum.AmountIn;
		while (lower < upper)
		{
			var middle = (lower + upper) >> 1;
			var candidate = QuoteExactInputAtBin(bin, market, variables,
				middle, swapForY);
			if (candidate.AmountOut >= desiredOutput)
				upper = middle;
			else
				lower = middle + 1;
		}
		var result = QuoteExactInputAtBin(bin, market, variables, lower,
			swapForY);
		return result.AmountOut >= desiredOutput
			? new(result.AmountIn, desiredOutput)
			: default;
	}

	private static BinFill FillWithoutFees(MeteoraBin bin,
		BigInteger input, bool swapForY, bool supportsLimitOrders)
	{
		var remaining = input;
		BigInteger consumed = 0;
		BigInteger output = 0;
		var marketMakerAmount = swapForY ? bin.AmountY : bin.AmountX;
		FillAvailable(marketMakerAmount);
		if (supportsLimitOrders && remaining > 0 &&
			((swapForY && !bin.IsLimitOrderAskSide) ||
				(!swapForY && bin.IsLimitOrderAskSide)))
		{
			FillAvailable(bin.ProcessedOrderRemainingAmount);
			FillAvailable(bin.OpenOrderAmount);
		}
		return new(consumed, output);

		void FillAvailable(BigInteger availableOutput)
		{
			if (remaining <= 0 || availableOutput <= 0)
				return;
			var maximumInput = GetAmountIn(availableOutput, bin.Price,
				swapForY, true);
			if (remaining >= maximumInput)
			{
				remaining -= maximumInput;
				consumed += maximumInput;
				output += availableOutput;
			}
			else
			{
				var partialOutput = GetAmountOut(remaining, bin.Price, swapForY);
				partialOutput = BigInteger.Min(partialOutput, availableOutput);
				consumed += remaining;
				output += partialOutput;
				remaining = 0;
			}
		}
	}

	private static BigInteger GetMaximumOutput(MeteoraBin bin, bool swapForY,
		bool supportsLimitOrders)
	{
		BigInteger result = swapForY ? bin.AmountY : bin.AmountX;
		if (supportsLimitOrders &&
			((swapForY && !bin.IsLimitOrderAskSide) ||
				(!swapForY && bin.IsLimitOrderAskSide)))
			result += new BigInteger(bin.OpenOrderAmount) +
				bin.ProcessedOrderRemainingAmount;
		return result;
	}

	private static BigInteger GetAmountIn(BigInteger output,
		BigInteger price, bool swapForY, bool roundUp)
		=> swapForY
			? Divide(output << _scaleOffset, price, roundUp)
			: Divide(output * price, _scale, roundUp);

	private static BigInteger GetAmountOut(BigInteger input,
		BigInteger price, bool swapForY)
		=> swapForY
			? input * price >> _scaleOffset
			: (input << _scaleOffset) / price;

	private static (BigInteger Amount, BigInteger Fee) GetExcludedFeeAmount(
		BigInteger included, BigInteger feeRate)
	{
		var fee = CeilingDiv(included * feeRate, _feePrecision);
		return (included - fee, fee);
	}

	private static (BigInteger Amount, BigInteger Fee) GetIncludedFeeAmount(
		BigInteger excluded, BigInteger feeRate)
	{
		var denominator = _feePrecision - feeRate;
		var included = CeilingDiv(excluded * _feePrecision, denominator);
		return (included, included - excluded);
	}

	private static BigInteger GetTotalFee(ushort binStep,
		MeteoraStaticParameters parameters,
		MeteoraVariableParameters variables)
	{
		var baseFee = new BigInteger(parameters.BaseFactor) * binStep * 10 *
			BigInteger.Pow(10, parameters.BaseFeePowerFactor);
		BigInteger variableFee = 0;
		if (parameters.VariableFeeControl > 0)
		{
			var value = new BigInteger(variables.VolatilityAccumulator) * binStep;
			variableFee = CeilingDiv(parameters.VariableFeeControl * value * value,
				100_000_000_000);
		}
		return BigInteger.Min(baseFee + variableFee, _maximumFeeRate);
	}

	private static bool IsFeeOnInput(MeteoraCollectFeeModes collectFeeMode,
		bool swapForY)
		=> collectFeeMode switch
		{
			MeteoraCollectFeeModes.InputOnly => true,
			MeteoraCollectFeeModes.OnlyY => !swapForY,
			_ => throw new InvalidDataException(
				$"Unknown Meteora fee collection mode '{collectFeeMode}'."),
		};

	private static void UpdateReference(int activeId,
		MeteoraVariableParameters variables,
		MeteoraStaticParameters parameters)
	{
		var currentTimestamp = checked((long)(
			DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds);
		var elapsed = currentTimestamp - variables.LastUpdateTimestamp;
		if (elapsed < parameters.FilterPeriod)
			return;
		variables.IndexReference = activeId;
		variables.VolatilityReference = elapsed < parameters.DecayPeriod
			? checked((uint)((ulong)variables.VolatilityAccumulator *
				parameters.ReductionFactor / _basisPointMaximum))
			: 0;
	}

	private static void UpdateVolatilityAccumulator(
		MeteoraVariableParameters variables,
		MeteoraStaticParameters parameters, int activeId)
	{
		var delta = Math.Abs((long)variables.IndexReference - activeId);
		var next = (ulong)variables.VolatilityReference +
			(ulong)delta * _basisPointMaximum;
		variables.VolatilityAccumulator = (uint)Math.Min(next,
			parameters.MaximumVolatilityAccumulator);
	}

	private static BigInteger Divide(BigInteger numerator,
		BigInteger denominator, bool roundUp)
	{
		var quotient = BigInteger.DivRem(numerator, denominator,
			out var remainder);
		return roundUp && remainder != 0 ? quotient + 1 : quotient;
	}

	private static BigInteger CeilingDiv(BigInteger numerator,
		BigInteger denominator)
		=> Divide(numerator, denominator, true);

	private static string NormalizeSymbol(string value, string mint)
	{
		value = value?.Trim();
		if (!value.IsEmpty() && value.Length <= 20 && value.All(static ch =>
			char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-'))
			return value.ToUpperInvariant();
		return "TOKEN-" + mint[..6].ToUpperInvariant();
	}
}
