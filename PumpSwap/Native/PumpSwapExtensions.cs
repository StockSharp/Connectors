namespace StockSharp.PumpSwap.Native;

static class PumpSwapExtensions
{
	public const string ProgramAddress =
		"pAMMBay6oceH9fJKBRHGP5D4bD4sWpmSwMn52FMfXEA";
	public const string PumpProgramAddress =
		"6EF8rrecthR5Dkzon8Nwu78hRvfCKubJ14M5uBEwF6P";
	public const string FeeProgramAddress =
		"pfeeUxB6jkeY1Hxd7CsFCAjcbHA9rWtchMGdZ6VojVZ";
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
	public const string WrappedSolMint =
		"So11111111111111111111111111111111111111112";
	public const string PumpMint =
		"pumpCmXqMfrsAkQ5r49WcJnRayYRqmXz6ae8H7H9Dfn";
	public const string UsdcMint =
		"EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";
	public const string GlobalConfigAddress =
		"ADyA8hdefvWN2dbGGWFotbzWxrAvLW83WG6QCVXvJKqw";

	public static readonly byte[] PoolDiscriminator =
		[0xf1, 0x9a, 0x6d, 0x04, 0x11, 0xb1, 0x6d, 0xbc];
	public static readonly byte[] GlobalConfigDiscriminator =
		[0x95, 0x08, 0x9c, 0xca, 0xa0, 0xfc, 0xb0, 0xd9];
	public static readonly byte[] FeeConfigDiscriminator =
		[0x8f, 0x34, 0x92, 0xbb, 0xdb, 0x7b, 0x4c, 0x9b];
	public static readonly byte[] BuyDiscriminator =
		[0x66, 0x06, 0x3d, 0x12, 0x01, 0xda, 0xeb, 0xea];
	public static readonly byte[] SellDiscriminator =
		[0x33, 0xe6, 0x85, 0xa4, 0x01, 0x7f, 0x83, 0xad];
	public static readonly byte[] ExtendAccountDiscriminator =
		[0xea, 0x66, 0xc2, 0xcb, 0x96, 0x48, 0x3e, 0xe5];
	public static readonly byte[] BuyEventDiscriminator =
		[0x67, 0xf4, 0x52, 0x1f, 0x2c, 0xf5, 0x77, 0x77];
	public static readonly byte[] SellEventDiscriminator =
		[0x3e, 0x2f, 0x37, 0x0a, 0xa5, 0x03, 0xdc, 0x2a];

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

	public static bool IsDefaultPublicKey(this string value)
		=> !value.IsEmpty() && value.Equals(
			SystemProgramAddress, StringComparison.Ordinal);

	public static string FindProgramAddress(string programAddress,
		params byte[][] seeds)
	{
		if (!PublicKey.TryFindProgramAddress(seeds,
			programAddress.ToPublicKey(), out var address, out _))
			throw new InvalidOperationException(
				$"Unable to derive a PDA for program '{programAddress}'.");
		return address.Key;
	}

	public static string GlobalVolumeAccumulatorAddress()
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("global_volume_accumulator"));

	public static string UserVolumeAccumulatorAddress(string walletAddress)
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("user_volume_accumulator"),
			walletAddress.ToPublicKey().KeyBytes);

	public static string FeeConfigAddress()
		=> FindProgramAddress(FeeProgramAddress,
			Encoding.UTF8.GetBytes("fee_config"),
			ProgramAddress.ToPublicKey().KeyBytes);

	public static string EventAuthorityAddress()
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("__event_authority"));

	public static string PoolV2Address(string baseMint)
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("pool-v2"),
			baseMint.ToPublicKey().KeyBytes);

	public static string PumpPoolAuthorityAddress(string baseMint)
		=> FindProgramAddress(PumpProgramAddress,
			Encoding.UTF8.GetBytes("pool-authority"),
			baseMint.ToPublicKey().KeyBytes);

	public static string CoinCreatorVaultAuthorityAddress(string coinCreator)
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("creator_vault"),
			coinCreator.ToPublicKey().KeyBytes);

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

	public static string GetRpcEndpoint(this PumpSwapClusters cluster)
		=> cluster switch
		{
			PumpSwapClusters.Mainnet =>
				"https://api.mainnet-beta.solana.com",
			PumpSwapClusters.Devnet => "https://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static string GetSocketEndpoint(this PumpSwapClusters cluster)
		=> cluster switch
		{
			PumpSwapClusters.Mainnet =>
				"wss://api.mainnet-beta.solana.com",
			PumpSwapClusters.Devnet => "wss://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static SecurityId ToStockSharp(this PumpSwapMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.PumpSwap,
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

	public static PumpSwapMarket DecodePool(string poolAddress,
		PumpSwapRpcAccount account)
	{
		ArgumentNullException.ThrowIfNull(account);
		var data = DecodeAccountData(account);
		var reader = new PumpSwapDataReader(data);
		reader.ReadDiscriminator(PoolDiscriminator, "pool");
		_ = reader.ReadByte();
		var index = reader.ReadUInt16();
		var creator = reader.ReadPublicKey();
		var baseMint = reader.ReadPublicKey();
		var quoteMint = reader.ReadPublicKey();
		_ = reader.ReadPublicKey();
		var poolBase = reader.ReadPublicKey();
		var poolQuote = reader.ReadPublicKey();
		_ = reader.ReadUInt64();
		var coinCreator = reader.Remaining >= PublicKey.PublicKeyLength
			? reader.ReadPublicKey()
			: SystemProgramAddress;
		var isMayhemMode = reader.Remaining >= 1 && reader.ReadBoolean();
		var isCashbackCoin = reader.Remaining >= 1 && reader.ReadBoolean();
		var virtualQuoteReserves = reader.Remaining >= 16
			? reader.ReadInt128()
			: BigInteger.Zero;
		return new()
		{
			PoolAddress = poolAddress.NormalizePublicKey(),
			PoolDataLength = data.Length,
			Index = index,
			Creator = creator,
			CoinCreator = coinCreator,
			PoolBaseTokenAccount = poolBase,
			PoolQuoteTokenAccount = poolQuote,
			BaseToken = new() { Mint = baseMint },
			QuoteToken = new() { Mint = quoteMint },
			IsMayhemMode = isMayhemMode,
			IsCashbackCoin = isCashbackCoin,
			VirtualQuoteReserves = virtualQuoteReserves,
		};
	}

	public static PumpSwapGlobalConfig DecodeGlobalConfig(
		PumpSwapRpcAccount account)
	{
		var reader = new PumpSwapDataReader(DecodeAccountData(account));
		reader.ReadDiscriminator(GlobalConfigDiscriminator, "global config");
		_ = reader.ReadPublicKey();
		var lpFee = reader.ReadUInt64();
		var protocolFee = reader.ReadUInt64();
		_ = reader.ReadByte();
		var protocolRecipients = ReadPublicKeys(reader, 8);
		var creatorFee = reader.ReadUInt64();
		_ = reader.ReadPublicKey();
		_ = reader.ReadPublicKey();
		var reservedRecipient = reader.ReadPublicKey();
		_ = reader.ReadBoolean();
		var reservedRecipients = ReadPublicKeys(reader, 7);
		_ = reader.ReadBoolean();
		var buybackRecipients = ReadPublicKeys(reader, 8);
		_ = reader.ReadUInt64();
		_ = reader.ReadPublicKey();
		_ = reader.ReadBoolean();
		return new()
		{
			LpFeeBasisPoints = lpFee,
			ProtocolFeeBasisPoints = protocolFee,
			ProtocolFeeRecipients = protocolRecipients,
			CoinCreatorFeeBasisPoints = creatorFee,
			ReservedFeeRecipient = reservedRecipient,
			ReservedFeeRecipients = reservedRecipients,
			BuybackFeeRecipients = buybackRecipients,
		};
	}

	public static PumpSwapFeeConfig DecodeFeeConfig(PumpSwapRpcAccount account)
	{
		var reader = new PumpSwapDataReader(DecodeAccountData(account));
		reader.ReadDiscriminator(FeeConfigDiscriminator, "fee config");
		_ = reader.ReadByte();
		_ = reader.ReadPublicKey();
		var flatFees = ReadFees(reader);
		var tiers = ReadFeeTiers(reader);
		_ = ReadFeeTiers(reader);
		return new()
		{
			FlatFees = flatFees,
			FeeTiers = tiers,
		};
	}

	public static PumpSwapToken DecodeMint(string mint,
		PumpSwapRpcAccount account, string symbol, string name)
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
		return new()
		{
			Mint = mint.NormalizePublicKey(),
			Symbol = NormalizeSymbol(symbol, mint),
			Name = name.IsEmpty() ? NormalizeSymbol(symbol, mint) : name.Trim(),
			Decimals = decimals,
			Supply = supply,
			TokenProgram = account.Owner.NormalizePublicKey(),
		};
	}

	public static ulong DecodeTokenAmount(PumpSwapRpcAccount account,
		string expectedMint)
	{
		if (account is null)
			return 0;
		var data = DecodeAccountData(account);
		if (data.Length < 72)
			throw new InvalidDataException("SPL token account data is truncated.");
		var mint = new PublicKey(data.AsSpan(0, 32)).Key;
		if (!mint.Equals(expectedMint, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"SPL token account belongs to mint '{mint}', not " +
				$"'{expectedMint}'.");
		return BinaryPrimitives.ReadUInt64LittleEndian(
			data.AsSpan(64, sizeof(ulong)));
	}

	public static (string Name, string Symbol) DecodeMetadata(
		PumpSwapRpcAccount account, string expectedMint)
	{
		if (account is null)
			return (null, null);
		if (!account.Owner.Equals(MetadataProgramAddress,
			StringComparison.Ordinal))
			return (null, null);
		var reader = new PumpSwapDataReader(DecodeAccountData(account));
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

	public static PumpSwapEvent[] DecodeEvents(string signature,
		IEnumerable<string> logs)
	{
		var result = new List<PumpSwapEvent>();
		foreach (var line in logs ?? [])
		{
			const string prefix = "Program data: ";
			var start = line?.IndexOf(prefix,
				StringComparison.Ordinal) ?? -1;
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
			if (data.Length < 152)
				continue;
			PumpSwapTradeTypes tradeType;
			if (data.AsSpan(0, 8).SequenceEqual(BuyEventDiscriminator))
				tradeType = PumpSwapTradeTypes.Buy;
			else if (data.AsSpan(0, 8).SequenceEqual(SellEventDiscriminator))
				tradeType = PumpSwapTradeTypes.Sell;
			else
				continue;
			try
			{
				var reader = new PumpSwapDataReader(data);
				reader.Skip(8);
				var timestamp = reader.ReadInt64();
				var baseAmount = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				var poolBaseReserves = reader.ReadUInt64();
				var poolQuoteReserves = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				_ = reader.ReadUInt64();
				var quoteAmount = reader.ReadUInt64();
				var pool = reader.ReadPublicKey();
				result.Add(new()
				{
					EventIndex = result.Count,
					Signature = signature,
					PoolAddress = pool,
					Time = timestamp.FromUnix(),
					TradeType = tradeType,
					BaseAmount = baseAmount,
					QuoteAmount = quoteAmount,
					PoolBaseReserves = poolBaseReserves,
					PoolQuoteReserves = poolQuoteReserves,
				});
			}
			catch (Exception error) when (error is InvalidDataException or
				ArgumentOutOfRangeException)
			{
				continue;
			}
		}
		return [.. result];
	}

	public static PumpSwapQuote GetQuote(this PumpSwapMarket market,
		PumpSwapTradeTypes tradeType, BigInteger baseAmount,
		decimal slippageTolerance, PumpSwapGlobalConfig globalConfig,
		PumpSwapFeeConfig feeConfig)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(globalConfig);
		if (baseAmount <= 0 || baseAmount > ulong.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(baseAmount));
		var baseReserves = new BigInteger(market.BaseReserves);
		var rawQuoteReserves = new BigInteger(market.QuoteReserves);
		var quoteReserves = rawQuoteReserves + market.VirtualQuoteReserves;
		if (baseReserves <= 0 || rawQuoteReserves <= 0 || quoteReserves <= 0)
			throw new InvalidOperationException(
				"PumpSwap pool reserves must be positive.");
		var fees = GetFees(market, globalConfig, feeConfig, baseReserves,
			quoteReserves);
		var creatorFee = market.CoinCreator.IsDefaultPublicKey()
			? BigInteger.Zero
			: fees.CreatorBasisPoints;
		var slippageBasisPoints = new BigInteger(slippageTolerance * 100m);
		if (tradeType == PumpSwapTradeTypes.Buy)
		{
			if (baseAmount >= baseReserves)
				throw new InvalidOperationException(
					"PumpSwap buy would deplete the pool base reserves.");
			var rawQuote = CeilDiv(quoteReserves * baseAmount,
				baseReserves - baseAmount);
			var totalQuote = rawQuote + Fee(rawQuote, fees.LpBasisPoints) +
				Fee(rawQuote, fees.ProtocolBasisPoints) +
				Fee(rawQuote, creatorFee);
			var maximumQuote = totalQuote *
				(10_000 + slippageBasisPoints) / 10_000;
			return new()
			{
				BaseAmount = baseAmount,
				QuoteAmount = totalQuote,
				QuoteLimit = maximumQuote,
			};
		}
		else
		{
			var rawQuote = quoteReserves * baseAmount /
				(baseReserves + baseAmount);
			var lpFee = Fee(rawQuote, fees.LpBasisPoints);
			if (rawQuote - lpFee > rawQuoteReserves)
				throw new InvalidOperationException(
					"PumpSwap has insufficient real quote reserves.");
			var finalQuote = rawQuote - lpFee -
				Fee(rawQuote, fees.ProtocolBasisPoints) -
				Fee(rawQuote, creatorFee);
			if (finalQuote <= 0)
				throw new InvalidOperationException(
					"PumpSwap fees exceed the quoted sell output.");
			var minimumQuote = finalQuote *
				(10_000 - slippageBasisPoints) / 10_000;
			return new()
			{
				BaseAmount = baseAmount,
				QuoteAmount = finalQuote,
				QuoteLimit = minimumQuote,
			};
		}
	}

	public static byte[] DecodeAccountData(PumpSwapRpcAccount account)
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

	private static PumpSwapFees GetFees(PumpSwapMarket market,
		PumpSwapGlobalConfig globalConfig, PumpSwapFeeConfig feeConfig,
		BigInteger baseReserves, BigInteger quoteReserves)
	{
		PumpSwapFees fees;
		if (feeConfig is null)
			fees = new(globalConfig.LpFeeBasisPoints,
				globalConfig.ProtocolFeeBasisPoints,
				globalConfig.CoinCreatorFeeBasisPoints);
		else if (!market.Creator.Equals(
			PumpPoolAuthorityAddress(market.BaseToken.Mint),
			StringComparison.Ordinal))
			fees = feeConfig.FlatFees;
		else
		{
			if (feeConfig.FeeTiers is not { Length: > 0 } tiers)
				throw new InvalidDataException(
					"Pump fee configuration contains no fee tiers.");
			var marketCap = quoteReserves * market.BaseToken.Supply /
				baseReserves;
			fees = tiers[0].Fees;
			foreach (var tier in tiers.Reverse())
				if (marketCap >= tier.MarketCapThreshold)
				{
					fees = tier.Fees;
					break;
				}
		}
		if (fees.LpBasisPoints < 0 || fees.LpBasisPoints > 10_000 ||
			fees.ProtocolBasisPoints < 0 ||
			fees.ProtocolBasisPoints > 10_000 ||
			fees.CreatorBasisPoints < 0 ||
			fees.CreatorBasisPoints > 10_000 ||
			fees.LpBasisPoints + fees.ProtocolBasisPoints +
				fees.CreatorBasisPoints >= 10_000)
			throw new InvalidDataException(
				"PumpSwap returned invalid fee basis points.");
		return fees;
	}

	private static PumpSwapFees ReadFees(PumpSwapDataReader reader)
		=> new(reader.ReadUInt64(), reader.ReadUInt64(),
			reader.ReadUInt64());

	private static PumpSwapFeeTier[] ReadFeeTiers(
		PumpSwapDataReader reader)
	{
		var count = reader.ReadUInt32();
		if (count > 64)
			throw new InvalidDataException(
				$"Pump fee tier count '{count}' exceeds the safety limit.");
		var result = new PumpSwapFeeTier[checked((int)count)];
		for (var index = 0; index < result.Length; index++)
			result[index] = new(reader.ReadUInt128(), ReadFees(reader));
		return result;
	}

	private static string[] ReadPublicKeys(PumpSwapDataReader reader,
		int count)
	{
		var result = new string[count];
		for (var index = 0; index < count; index++)
			result[index] = reader.ReadPublicKey();
		return result;
	}

	private static BigInteger CeilDiv(BigInteger numerator,
		BigInteger denominator)
	{
		if (numerator < 0 || denominator <= 0)
			throw new ArgumentOutOfRangeException(nameof(denominator));
		return (numerator + denominator - 1) / denominator;
	}

	private static BigInteger Fee(BigInteger amount,
		BigInteger basisPoints)
		=> basisPoints == 0
			? BigInteger.Zero
			: CeilDiv(amount * basisPoints, 10_000);

	private static string NormalizeSymbol(string value, string mint)
	{
		if (mint.Equals(WrappedSolMint, StringComparison.Ordinal))
			return "WSOL";
		if (mint.Equals(UsdcMint, StringComparison.Ordinal))
			return "USDC";
		if (mint.Equals(PumpMint, StringComparison.Ordinal))
			return "PUMP";
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
