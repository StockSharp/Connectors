namespace StockSharp.Raydium.Native;

static class RaydiumExtensions
{
	public const string SystemProgramAddress =
		"11111111111111111111111111111111";
	public const string AssociatedTokenProgramAddress =
		"ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL";
	public const string TokenProgramAddress =
		"TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
	public const string Token2022ProgramAddress =
		"TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";
	public const string WrappedSolMint =
		"So11111111111111111111111111111111111111112";

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

	public static string GetRpcEndpoint(this RaydiumClusters cluster)
		=> cluster switch
		{
			RaydiumClusters.Mainnet =>
				"https://api.mainnet-beta.solana.com",
			RaydiumClusters.Devnet => "https://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static string GetSocketEndpoint(this RaydiumClusters cluster)
		=> cluster switch
		{
			RaydiumClusters.Mainnet =>
				"wss://api.mainnet-beta.solana.com",
			RaydiumClusters.Devnet => "wss://api.devnet.solana.com",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static string GetApiEndpoint(this RaydiumClusters cluster)
		=> cluster switch
		{
			RaydiumClusters.Mainnet => "https://api-v3.raydium.io",
			RaydiumClusters.Devnet => "https://api-v3-devnet.raydium.io",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static string GetTradeEndpoint(this RaydiumClusters cluster)
		=> cluster switch
		{
			RaydiumClusters.Mainnet => "https://transaction-v1.raydium.io",
			RaydiumClusters.Devnet =>
				"https://transaction-v1-devnet.raydium.io",
			_ => throw new ArgumentOutOfRangeException(nameof(cluster), cluster,
				"Unsupported Solana cluster."),
		};

	public static SecurityId ToStockSharp(this RaydiumMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Raydium,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string GetMintPairKey(string first, string second)
	{
		first = first.NormalizePublicKey();
		second = second.NormalizePublicKey();
		return string.CompareOrdinal(first, second) <= 0
			? first + ":" + second
			: second + ":" + first;
	}

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

	public static byte[] DecodeAccountData(RaydiumRpcAccount account)
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

	public static void ValidateMintAccount(RaydiumRpcAccount account,
		RaydiumToken token)
	{
		ArgumentNullException.ThrowIfNull(account);
		ArgumentNullException.ThrowIfNull(token);
		if (!account.Owner.Equals(token.TokenProgram, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Mint '{token.Mint}' is owned by '{account.Owner}', not " +
				$"'{token.TokenProgram}'.");
		var data = DecodeAccountData(account);
		if (data.Length < 46 || data[45] == 0)
			throw new InvalidDataException(
				$"Mint '{token.Mint}' is truncated or not initialized.");
		if (data[44] != token.Decimals)
			throw new InvalidDataException(
				$"Mint '{token.Mint}' decimals differ from Raydium metadata.");
	}

	public static void ValidateVaultAccount(RaydiumRpcAccount account,
		RaydiumToken token, string vault)
	{
		ArgumentNullException.ThrowIfNull(account);
		if (!account.Owner.Equals(token.TokenProgram, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Vault '{vault}' is not owned by the expected token program.");
		var data = DecodeAccountData(account);
		if (data.Length < 72)
			throw new InvalidDataException(
				$"Vault '{vault}' token account is truncated.");
		var mint = new PublicKey(data.AsSpan(0, 32)).Key;
		if (!mint.Equals(token.Mint, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Vault '{vault}' belongs to mint '{mint}', not '{token.Mint}'.");
	}

	public static RaydiumTrade[] DecodeTrades(string signature,
		RaydiumRpcTransaction transaction, RaydiumMarket market, DateTime time)
	{
		if (transaction?.Meta is null || transaction.Meta.Error is not null ||
			transaction.Transaction?.Message?.AccountKeys is null)
			return [];
		var keys = transaction.Transaction.Message.AccountKeys
			.Concat(transaction.Meta.LoadedAddresses?.Writable ?? [])
			.Concat(transaction.Meta.LoadedAddresses?.ReadOnly ?? []).ToArray();
		var result = new List<RaydiumTrade>();
		foreach (var pool in market.Pools)
		{
			var indexA = Array.IndexOf(keys, pool.VaultA);
			var indexB = Array.IndexOf(keys, pool.VaultB);
			if (indexA < 0 || indexB < 0 ||
				!TryGetDelta(transaction.Meta, indexA, pool.TokenA.Mint,
					out var deltaA) ||
				!TryGetDelta(transaction.Meta, indexB, pool.TokenB.Mint,
					out var deltaB) || deltaA == 0 || deltaB == 0 ||
				deltaA.Sign == deltaB.Sign)
				continue;
			var isDirect = pool.TokenA.Mint.Equals(market.TokenA.Mint,
				StringComparison.Ordinal);
			var baseDelta = isDirect ? deltaA : deltaB;
			var quoteDelta = isDirect ? deltaB : deltaA;
			if (baseDelta.Sign == quoteDelta.Sign)
				continue;
			var side = baseDelta > 0 ? Sides.Sell : Sides.Buy;
			var volume = BigInteger.Abs(baseDelta).FromBaseUnits(
				market.TokenA.Decimals);
			var quote = BigInteger.Abs(quoteDelta).FromBaseUnits(
				market.TokenB.Decimals);
			if (volume <= 0 || quote <= 0)
				continue;
			result.Add(new()
			{
				Id = $"{signature}:{pool.PoolAddress}",
				Signature = signature,
				PoolAddress = pool.PoolAddress,
				Time = time.ToUniversalTime(),
				Side = side,
				Price = quote / volume,
				Volume = volume,
			});
		}
		return [.. result];
	}

	public static bool TryGetDelta(RaydiumRpcTransactionMeta meta,
		int accountIndex, string expectedMint, out BigInteger delta)
	{
		delta = 0;
		var before = meta.PreTokenBalances?.FirstOrDefault(balance =>
			balance.AccountIndex == accountIndex);
		var after = meta.PostTokenBalances?.FirstOrDefault(balance =>
			balance.AccountIndex == accountIndex);
		if (before?.TokenAmount?.Amount.IsEmpty() != false ||
			after?.TokenAmount?.Amount.IsEmpty() != false ||
			!string.Equals(before.Mint, expectedMint,
				StringComparison.Ordinal) ||
			!string.Equals(after.Mint, expectedMint,
				StringComparison.Ordinal) ||
			!BigInteger.TryParse(before.TokenAmount.Amount, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var preAmount) ||
			!BigInteger.TryParse(after.TokenAmount.Amount, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var postAmount))
			return false;
		delta = postAmount - preAmount;
		return true;
	}
}
