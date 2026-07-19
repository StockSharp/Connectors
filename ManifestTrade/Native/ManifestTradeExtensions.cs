namespace StockSharp.ManifestTrade.Native;

static class ManifestTradeExtensions
{
	public const string ProgramAddress =
		"MNFSTqtC93rEfYHB6hF82sKdZpUDFWkViLByLd1k1Ms";
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
	public const uint NilIndex = uint.MaxValue;
	public const uint NoExpirationSlot = 0;
	public const int MarketHeaderSize = 256;
	public const int MarketBlockSize = 80;
	public const ulong MarketDiscriminant = 4859840929024028656;
	private static readonly BigInteger _priceScale =
		BigInteger.Pow(10, 18);

	public static readonly byte[] FillEventDiscriminator =
		[58, 230, 242, 3, 75, 113, 4, 169];
	public static readonly byte[] PlaceEventDiscriminator =
		[157, 118, 247, 213, 47, 19, 164, 120];
	public static readonly byte[] PlaceV2EventDiscriminator =
		[189, 97, 159, 235, 136, 5, 1, 141];
	public static readonly byte[] CancelEventDiscriminator =
		[22, 65, 71, 33, 244, 235, 255, 215];

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

	public static string FindProgramAddress(string programAddress,
		params byte[][] seeds)
	{
		if (!PublicKey.TryFindProgramAddress(seeds,
			programAddress.ToPublicKey(), out var address, out _))
			throw new InvalidOperationException(
				$"Unable to derive a PDA for program '{programAddress}'.");
		return address.Key;
	}

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

	public static string GlobalAddress(string mint)
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("global"), mint.ToPublicKey().KeyBytes);

	public static string GlobalVaultAddress(string mint)
		=> FindProgramAddress(ProgramAddress,
			Encoding.UTF8.GetBytes("global-vault"),
			mint.ToPublicKey().KeyBytes);

	public static string GetRpcEndpoint(this ManifestTradeClusters cluster)
		=> cluster switch
		{
			ManifestTradeClusters.Mainnet =>
				"https://api.mainnet-beta.solana.com",
			ManifestTradeClusters.Devnet => "https://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static string GetSocketEndpoint(this ManifestTradeClusters cluster)
		=> cluster switch
		{
			ManifestTradeClusters.Mainnet =>
				"wss://api.mainnet-beta.solana.com",
			ManifestTradeClusters.Devnet => "wss://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static SecurityId ToStockSharp(this ManifestTradeMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.ManifestTrade,
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
		return digits.IsEmpty() ? BigInteger.Zero : BigInteger.Parse(digits,
			NumberStyles.Integer, CultureInfo.InvariantCulture);
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

	public static byte[] DecodeAccountData(ManifestTradeRpcAccount account)
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

	public static ManifestTradeToken DecodeMint(string mint,
		ManifestTradeRpcAccount account, string symbol, string name)
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
		var normalized = NormalizeSymbol(symbol, mint);
		return new()
		{
			Mint = mint.NormalizePublicKey(),
			Symbol = normalized,
			Name = name.IsEmpty() ? normalized : name.Trim(),
			Decimals = decimals,
			Supply = supply,
			TokenProgram = account.Owner.NormalizePublicKey(),
			IsDirectTradingSupported = account.Owner.Equals(
				TokenProgramAddress, StringComparison.Ordinal),
		};
	}

	public static (string Name, string Symbol) DecodeMetadata(
		ManifestTradeRpcAccount account, string expectedMint)
	{
		if (account is null || !account.Owner.Equals(MetadataProgramAddress,
			StringComparison.Ordinal))
			return (null, null);
		var reader = new ManifestTradeDataReader(DecodeAccountData(account));
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

	public static ManifestTradeMarket DecodeMarket(string marketAddress,
		ManifestTradeRpcAccount account, long slot)
	{
		ArgumentNullException.ThrowIfNull(account);
		marketAddress = marketAddress.NormalizePublicKey();
		if (!account.Owner.Equals(ProgramAddress, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Account '{marketAddress}' is not owned by Manifest Trade.");
		var data = DecodeAccountData(account);
		if (data.Length < MarketHeaderSize)
			throw new InvalidDataException(
				$"Manifest market '{marketAddress}' is truncated.");
		var reader = new ManifestTradeDataReader(data);
		if (reader.ReadUInt64() != MarketDiscriminant)
			throw new InvalidDataException(
				$"Account '{marketAddress}' is not a Manifest market.");
		var version = reader.ReadByte();
		var baseDecimals = reader.ReadByte();
		var quoteDecimals = reader.ReadByte();
		_ = reader.ReadByte();
		_ = reader.ReadByte();
		reader.Skip(3);
		var baseMint = reader.ReadPublicKey();
		var quoteMint = reader.ReadPublicKey();
		var baseVault = reader.ReadPublicKey();
		var quoteVault = reader.ReadPublicKey();
		var sequence = reader.ReadUInt64();
		var allocated = reader.ReadUInt32();
		var bidsRoot = reader.ReadUInt32();
		_ = reader.ReadUInt32();
		var asksRoot = reader.ReadUInt32();
		_ = reader.ReadUInt32();
		var seatsRoot = reader.ReadUInt32();
		_ = reader.ReadUInt32();
		_ = reader.ReadUInt32();
		var quoteVolume = reader.ReadUInt64();
		reader.Skip(64);
		if (reader.Position != MarketHeaderSize ||
			allocated % MarketBlockSize != 0 ||
			allocated > data.Length - MarketHeaderSize)
			throw new InvalidDataException(
				$"Manifest market '{marketAddress}' has an invalid dynamic area.");
		var used = new HashSet<uint>();
		var bids = TraverseOrders(data, allocated, bidsRoot, true, used,
			marketAddress);
		var asks = TraverseOrders(data, allocated, asksRoot, false, used,
			marketAddress);
		var seats = TraverseSeats(data, allocated, seatsRoot, used,
			marketAddress);
		return new()
		{
			MarketAddress = marketAddress,
			Version = version,
			BaseToken = new()
			{
				Mint = baseMint,
				Decimals = baseDecimals,
			},
			QuoteToken = new()
			{
				Mint = quoteMint,
				Decimals = quoteDecimals,
			},
			BaseVault = baseVault,
			QuoteVault = quoteVault,
			NextOrderSequence = sequence,
			QuoteVolumeAtoms = quoteVolume,
			Slot = slot,
			Bids = [.. bids.OrderByDescending(static order => order.RawPrice)
				.ThenBy(static order => order.Sequence)],
			Asks = [.. asks.OrderBy(static order => order.RawPrice)
				.ThenBy(static order => order.Sequence)],
			Seats = seats,
		};
	}

	public static ManifestTradeBookLevel[] GetBookLevels(
		this ManifestTradeMarket market, Sides side, int maximum)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (maximum <= 0)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var orders = side == Sides.Buy ? market.Bids : market.Asks;
		return [.. orders.Where(order => !IsExpired(order, market.Slot))
			.GroupBy(order => order.RawPrice)
			.Select(group => new ManifestTradeBookLevel(
				RawPriceToTokenPrice(group.Key, market.BaseToken.Decimals,
					market.QuoteToken.Decimals),
				group.Aggregate(BigInteger.Zero,
					static (sum, order) => sum + order.BaseAtoms)
					.FromBaseUnits(market.BaseToken.Decimals)))
			.Take(maximum)];
	}

	public static ManifestTradeQuote GetQuote(this ManifestTradeMarket market,
		Sides side, ulong requestedBaseAtoms, decimal slippagePercent)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (requestedBaseAtoms == 0)
			throw new ArgumentOutOfRangeException(nameof(requestedBaseAtoms));
		var remaining = requestedBaseAtoms;
		BigInteger quoteAtoms = 0;
		var orders = side == Sides.Sell ? market.Bids : market.Asks;
		foreach (var order in orders)
		{
			if (IsExpired(order, market.Slot))
				continue;
			var fill = Math.Min(remaining, order.BaseAtoms);
			var isMakerFullyFilled = fill >= order.BaseAtoms;
			var isRoundUp = side == Sides.Sell
				? isMakerFullyFilled
				: !isMakerFullyFilled;
			quoteAtoms += QuoteForBase(order.RawPrice, fill, isRoundUp);
			remaining -= fill;
			if (remaining == 0)
				break;
		}
		if (remaining != 0)
			throw new InvalidOperationException(
				"The requested Manifest Trade volume exceeds visible liquidity.");
		if (quoteAtoms <= 0 || quoteAtoms > ulong.MaxValue)
			throw new OverflowException(
				"Manifest Trade quote does not fit into an unsigned 64-bit value.");
		var basisPoints = checked((int)(slippagePercent * 100m));
		var quote = (ulong)quoteAtoms;
		var minimum = (ulong)(quoteAtoms * (10_000 - basisPoints) / 10_000);
		var maximum = checked((ulong)CeilDiv(
			quoteAtoms * (10_000 + basisPoints), 10_000));
		if (minimum == 0)
			throw new InvalidOperationException(
				"Manifest Trade minimum output rounds to zero atoms.");
		return new()
		{
			BaseAtoms = requestedBaseAtoms,
			QuoteAtoms = quote,
			InputLimitAtoms = side == Sides.Buy ? maximum : requestedBaseAtoms,
			OutputLimitAtoms = side == Sides.Sell ? minimum : requestedBaseAtoms,
		};
	}

	public static ulong RequiredQuoteAtoms(BigInteger rawPrice,
		ulong baseAtoms)
		=> checked((ulong)QuoteForBase(rawPrice, baseAtoms, true));

	public static decimal RawPriceToTokenPrice(BigInteger rawPrice,
		int baseDecimals, int quoteDecimals)
	{
		if (rawPrice <= 0)
			throw new InvalidDataException(
				"Manifest Trade price must be positive.");
		var exponent = baseDecimals - quoteDecimals - 18;
		var digits = rawPrice.ToString(CultureInfo.InvariantCulture);
		if (exponent >= 0)
			digits += new string('0', exponent);
		else
		{
			var decimals = -exponent;
			digits = digits.PadLeft(decimals + 1, '0');
			digits = digits.Insert(digits.Length - decimals, ".");
		}
		if (!decimal.TryParse(digits, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) || result <= 0)
			throw new OverflowException(
				"Manifest Trade price exceeds the supported decimal range.");
		return result;
	}

	public static (uint Mantissa, sbyte Exponent, BigInteger RawPrice)
		EncodePrice(decimal tokenPrice, int baseDecimals, int quoteDecimals)
	{
		if (tokenPrice <= 0)
			throw new ArgumentOutOfRangeException(nameof(tokenPrice));
		var atomicPrice = tokenPrice * DecimalPowerOfTen(
			quoteDecimals - baseDecimals);
		var exponent = 0;
		while (exponent < 8 && RoundMantissa(atomicPrice, exponent) >
			uint.MaxValue)
			exponent++;
		while (exponent > -18 && RoundMantissa(atomicPrice, exponent - 1) <=
			uint.MaxValue)
			exponent--;
		var rounded = RoundMantissa(atomicPrice, exponent);
		if (rounded is < 1 or > uint.MaxValue)
			throw new InvalidOperationException(
				$"Price '{tokenPrice}' cannot be represented by Manifest Trade.");
		var mantissa = checked((uint)rounded);
		var raw = new BigInteger(mantissa) * BigInteger.Pow(10, 18 + exponent);
		return (mantissa, checked((sbyte)exponent), raw);
	}

	public static ManifestTradeFillEvent[] DecodeFillEvents(string signature,
		IEnumerable<string> logs, DateTime time)
	{
		var result = new List<ManifestTradeFillEvent>();
		foreach (var data in DecodeProgramData(logs))
		{
			if (data.Length != 232 ||
				!data.AsSpan(0, 8).SequenceEqual(FillEventDiscriminator))
				continue;
			try
			{
				var reader = new ManifestTradeDataReader(data);
				reader.Skip(8);
				var market = reader.ReadPublicKey();
				var maker = reader.ReadPublicKey();
				var taker = reader.ReadPublicKey();
				var baseMint = reader.ReadPublicKey();
				var quoteMint = reader.ReadPublicKey();
				var rawPrice = reader.ReadUInt128();
				var baseAtoms = reader.ReadUInt64();
				var quoteAtoms = reader.ReadUInt64();
				var makerSequence = reader.ReadUInt64();
				var takerSequence = reader.ReadUInt64();
				var isBuy = ReadPodBoolean(reader);
				_ = ReadPodBoolean(reader);
				reader.Skip(14);
				if (rawPrice <= 0 || baseAtoms == 0 || quoteAtoms == 0 ||
					reader.Position != reader.Length)
					continue;
				result.Add(new()
				{
					EventIndex = result.Count,
					Signature = signature,
					Time = time.ToUniversalTime(),
					MarketAddress = market,
					Maker = maker,
					Taker = taker,
					BaseMint = baseMint,
					QuoteMint = quoteMint,
					RawPrice = rawPrice,
					BaseAtoms = baseAtoms,
					QuoteAtoms = quoteAtoms,
					MakerSequence = makerSequence,
					TakerSequence = takerSequence,
					IsTakerBuy = isBuy,
				});
			}
			catch (Exception error) when (error is InvalidDataException or
				ArgumentOutOfRangeException)
			{
			}
		}
		return [.. result];
	}

	public static ManifestTradePlaceEvent[] DecodePlaceEvents(
		IEnumerable<string> logs)
	{
		var result = new List<ManifestTradePlaceEvent>();
		foreach (var data in DecodeProgramData(logs))
		{
			var isV2 = data.Length == 152 &&
				data.AsSpan(0, 8).SequenceEqual(PlaceV2EventDiscriminator);
			if (!isV2 && (data.Length != 120 ||
				!data.AsSpan(0, 8).SequenceEqual(PlaceEventDiscriminator)))
				continue;
			try
			{
				var reader = new ManifestTradeDataReader(data);
				reader.Skip(8);
				var market = reader.ReadPublicKey();
				var trader = reader.ReadPublicKey();
				if (isV2)
					_ = reader.ReadPublicKey();
				var rawPrice = reader.ReadUInt128();
				var baseAtoms = reader.ReadUInt64();
				var sequence = reader.ReadUInt64();
				var orderIndex = reader.ReadUInt32();
				_ = reader.ReadUInt32();
				var type = ReadOrderType(reader.ReadByte());
				var isBid = ReadPodBoolean(reader);
				reader.Skip(6);
				if (rawPrice <= 0 || baseAtoms == 0 ||
					reader.Position != reader.Length)
					continue;
				result.Add(new()
				{
					MarketAddress = market,
					Trader = trader,
					RawPrice = rawPrice,
					BaseAtoms = baseAtoms,
					Sequence = sequence,
					OrderIndex = orderIndex,
					OrderType = type,
					IsBid = isBid,
				});
			}
			catch (Exception error) when (error is InvalidDataException or
				ArgumentOutOfRangeException)
			{
			}
		}
		return [.. result];
	}

	public static ManifestTradeCancelEvent[] DecodeCancelEvents(
		IEnumerable<string> logs)
	{
		var result = new List<ManifestTradeCancelEvent>();
		foreach (var data in DecodeProgramData(logs))
		{
			if (data.Length != 80 ||
				!data.AsSpan(0, 8).SequenceEqual(CancelEventDiscriminator))
				continue;
			try
			{
				var reader = new ManifestTradeDataReader(data);
				reader.Skip(8);
				result.Add(new()
				{
					MarketAddress = reader.ReadPublicKey(),
					Trader = reader.ReadPublicKey(),
					Sequence = reader.ReadUInt64(),
				});
			}
			catch (InvalidDataException)
			{
			}
		}
		return [.. result];
	}

	private static ManifestTradeOrder[] TraverseOrders(byte[] data,
		uint allocated, uint root, bool isBid, HashSet<uint> used,
		string marketAddress)
	{
		if (root == NilIndex)
			return [];
		var result = new List<ManifestTradeOrder>();
		var pending = new Stack<uint>();
		pending.Push(root);
		while (pending.Count > 0)
		{
			var index = pending.Pop();
			ValidateBlockIndex(index, allocated, marketAddress);
			if (!used.Add(index))
				throw new InvalidDataException(
					$"Manifest market '{marketAddress}' contains a cyclic tree.");
			var reader = CreateBlockReader(data, index);
			var left = reader.ReadUInt32();
			var right = reader.ReadUInt32();
			_ = reader.ReadUInt32();
			var color = reader.ReadByte();
			var payloadType = reader.ReadByte();
			reader.Skip(2);
			if (color > 1 || payloadType != 2)
				throw new InvalidDataException(
					$"Manifest market '{marketAddress}' has an invalid order node.");
			var rawPrice = reader.ReadUInt128();
			var baseAtoms = reader.ReadUInt64();
			var sequence = reader.ReadUInt64();
			var traderIndex = reader.ReadUInt32();
			var lastValidSlot = reader.ReadUInt32();
			var orderIsBid = ReadPodBoolean(reader);
			var type = ReadOrderType(reader.ReadByte());
			var reverseSpread = reader.ReadUInt16();
			reader.Skip(20);
			if (reader.Position != MarketBlockSize || rawPrice <= 0 ||
				baseAtoms == 0 || orderIsBid != isBid)
				throw new InvalidDataException(
					$"Manifest market '{marketAddress}' contains an invalid order.");
			result.Add(new()
			{
				Index = index,
				RawPrice = rawPrice,
				BaseAtoms = baseAtoms,
				Sequence = sequence,
				TraderIndex = traderIndex,
				LastValidSlot = lastValidSlot,
				IsBid = orderIsBid,
				OrderType = type,
				ReverseSpread = reverseSpread,
			});
			if (left != NilIndex)
				pending.Push(left);
			if (right != NilIndex)
				pending.Push(right);
		}
		return [.. result];
	}

	private static ManifestTradeSeat[] TraverseSeats(byte[] data,
		uint allocated, uint root, HashSet<uint> used, string marketAddress)
	{
		if (root == NilIndex)
			return [];
		var result = new List<ManifestTradeSeat>();
		var pending = new Stack<uint>();
		pending.Push(root);
		while (pending.Count > 0)
		{
			var index = pending.Pop();
			ValidateBlockIndex(index, allocated, marketAddress);
			if (!used.Add(index))
				throw new InvalidDataException(
					$"Manifest market '{marketAddress}' contains a cyclic tree.");
			var reader = CreateBlockReader(data, index);
			var left = reader.ReadUInt32();
			var right = reader.ReadUInt32();
			_ = reader.ReadUInt32();
			var color = reader.ReadByte();
			var payloadType = reader.ReadByte();
			reader.Skip(2);
			if (color > 1 || payloadType != 1)
				throw new InvalidDataException(
					$"Manifest market '{marketAddress}' has an invalid seat node.");
			result.Add(new()
			{
				Index = index,
				Trader = reader.ReadPublicKey(),
				BaseWithdrawableAtoms = reader.ReadUInt64(),
				QuoteWithdrawableAtoms = reader.ReadUInt64(),
				QuoteVolumeAtoms = reader.ReadUInt64(),
			});
			reader.Skip(8);
			if (reader.Position != MarketBlockSize)
				throw new InvalidDataException(
					$"Manifest market '{marketAddress}' contains an invalid seat.");
			if (left != NilIndex)
				pending.Push(left);
			if (right != NilIndex)
				pending.Push(right);
		}
		return [.. result];
	}

	private static ManifestTradeDataReader CreateBlockReader(byte[] data,
		uint index)
		=> new(data.AsSpan(checked(MarketHeaderSize + (int)index),
			MarketBlockSize).ToArray());

	private static void ValidateBlockIndex(uint index, uint allocated,
		string marketAddress)
	{
		if (index == NilIndex || index % MarketBlockSize != 0 ||
			index > allocated || allocated - index < MarketBlockSize)
			throw new InvalidDataException(
				$"Manifest market '{marketAddress}' contains an invalid tree index.");
	}

	private static bool ReadPodBoolean(ManifestTradeDataReader reader)
		=> reader.ReadByte() switch
		{
			0 => false,
			1 => true,
			var value => throw new InvalidDataException(
				$"Invalid Manifest boolean value '{value}'."),
		};

	private static ManifestTradeOrderTypes ReadOrderType(byte value)
		=> value <= (byte)ManifestTradeOrderTypes.ReverseTight
			? (ManifestTradeOrderTypes)value
			: throw new InvalidDataException(
				$"Unknown Manifest order type '{value}'.");

	private static bool IsExpired(ManifestTradeOrder order, long slot)
		=> order.LastValidSlot != NoExpirationSlot && slot >= 0 &&
			order.LastValidSlot < (ulong)slot;

	private static BigInteger QuoteForBase(BigInteger rawPrice,
		ulong baseAtoms, bool isRoundUp)
	{
		var product = rawPrice * baseAtoms;
		return isRoundUp ? CeilDiv(product, _priceScale) :
			product / _priceScale;
	}

	private static BigInteger CeilDiv(BigInteger numerator,
		BigInteger denominator)
		=> (numerator + denominator - 1) / denominator;

	private static decimal DecimalPowerOfTen(int exponent)
	{
		if (exponent is < -28 or > 28)
			throw new InvalidOperationException(
				"Token decimal difference exceeds decimal precision.");
		var value = 1m;
		if (exponent >= 0)
			for (var index = 0; index < exponent; index++)
				value *= 10m;
		else
			for (var index = 0; index > exponent; index--)
				value /= 10m;
		return value;
	}

	private static decimal RoundMantissa(decimal value, int exponent)
		=> decimal.Round(value / DecimalPowerOfTen(exponent), 0,
			MidpointRounding.AwayFromZero);

	private static IEnumerable<byte[]> DecodeProgramData(
		IEnumerable<string> logs)
	{
		const string prefix = "Program data: ";
		foreach (var line in logs ?? [])
		{
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
			yield return data;
		}
	}

	private static string NormalizeSymbol(string value, string mint)
	{
		if (value.IsEmpty())
			return mint.Equals(WrappedSolMint, StringComparison.Ordinal)
				? "SOL"
				: mint[..8].ToUpperInvariant();
		value = value.Trim();
		if (value.Length > 20 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			throw new InvalidDataException(
				$"Invalid token symbol '{value}' for mint '{mint}'.");
		return value.ToUpperInvariant();
	}
}
