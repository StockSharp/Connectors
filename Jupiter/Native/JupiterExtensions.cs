namespace StockSharp.Jupiter.Native;

static class JupiterExtensions
{
	public const string WrappedSolMint =
		"So11111111111111111111111111111111111111112";
	public const string WrappedBitcoinMint =
		"3NZ9JMVBmGAqocybic2c7LQCJScmgsAZ6vQqTDzcqmJh";
	public const string WrappedEthereumMint =
		"7vfCXTUXx5WJV5JADk17DUJ4ksgau7utNKj4b963voxs";
	public const string UsdcMint =
		"EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";
	public const string JupiterMint =
		"JUPyiwrYJFskUPiHa7hkeR8VUtAeFoSYbKedZNsDvCN";
	public const string TokenProgramAddress =
		"TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
	public const string Token2022ProgramAddress =
		"TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";

	public static long ToUnixSeconds(this DateTime value)
		=> checked((long)(value.ToUniversalTime() - DateTime.UnixEpoch)
			.TotalSeconds);

	public static string NormalizePublicKey(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		byte[] bytes;
		try
		{
			bytes = Encoders.Base58.DecodeData(value);
		}
		catch (Exception error) when (error is FormatException or
			ArgumentException)
		{
			throw new FormatException(
				$"Invalid Solana public key '{value}'.", error);
		}
		if (bytes.Length != PublicKey.PublicKeyLength)
			throw new FormatException(
				$"Invalid Solana public key '{value}'.");
		return new PublicKey(bytes).Key;
	}

	public static string NormalizeSignature(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		byte[] bytes;
		try
		{
			bytes = Encoders.Base58.DecodeData(value);
		}
		catch (Exception error) when (error is FormatException or
			ArgumentException)
		{
			throw new FormatException(
				$"Invalid Solana transaction signature '{value}'.", error);
		}
		if (bytes.Length != 64)
			throw new FormatException(
				$"Invalid Solana transaction signature '{value}'.");
		return value;
	}

	public static decimal GetUiMultiplier(this JupiterToken token,
		DateTime time)
	{
		ArgumentNullException.ThrowIfNull(token);
		var config = token.ScaledUiConfig;
		if (config is null)
			return 1m;
		var multiplier = config.Multiplier;
		if (!config.NewMultiplierEffectiveAt.IsEmpty() &&
			DateTime.TryParse(config.NewMultiplierEffectiveAt,
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal, out var effectiveAt) &&
			time.ToUniversalTime() >= effectiveAt)
			multiplier = config.NewMultiplier;
		if (multiplier <= 0)
			throw new InvalidDataException(
				$"Jupiter token '{token.Mint}' has an invalid UI multiplier.");
		return multiplier;
	}

	public static BigInteger ToRawAmount(this decimal amount,
		JupiterToken token, DateTime time)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var scale = DecimalScale(token.Decimals);
		var scaled = decimal.Truncate(amount * scale /
			token.GetUiMultiplier(time));
		if (scaled <= 0)
			throw new InvalidOperationException(
				$"Amount rounds to zero units for token '{token.Symbol}'.");
		return new BigInteger(scaled);
	}

	public static decimal FromRawAmount(this string value,
		JupiterToken token, DateTime time)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var raw))
			throw new InvalidDataException(
				$"Jupiter returned invalid token amount '{value}'.");
		try
		{
			return (decimal)raw / DecimalScale(token.Decimals) *
				token.GetUiMultiplier(time);
		}
		catch (OverflowException error)
		{
			throw new InvalidDataException(
				$"Jupiter token amount '{value}' exceeds decimal range.", error);
		}
	}

	public static string ToMicroUsd(this decimal value)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return decimal.Round(value * 1_000_000m, 0,
			MidpointRounding.AwayFromZero).ToString("0",
				CultureInfo.InvariantCulture);
	}

	public static decimal FromMicroUsd(this string value, string field)
		=> ParseDecimal(value, field) / 1_000_000m;

	public static decimal ParseDecimal(string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"Jupiter returned invalid {field} '{value}'.");
		return result;
	}

	public static string ToWire(this decimal value)
		=> value.ToString("0.############################",
			CultureInfo.InvariantCulture);

	public static SecurityId ToStockSharp(this JupiterMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Jupiter,
		};
	}

	public static JupiterPerpetualSides ToJupiter(this Sides side)
		=> side == Sides.Buy
			? JupiterPerpetualSides.Long
			: JupiterPerpetualSides.Short;

	public static Sides ToStockSharp(this JupiterPerpetualSides side)
		=> side == JupiterPerpetualSides.Long ? Sides.Buy : Sides.Sell;

	public static string GetMint(this JupiterCollateralTokens token)
		=> token switch
		{
			JupiterCollateralTokens.Usdc => UsdcMint,
			JupiterCollateralTokens.Sol => WrappedSolMint,
			JupiterCollateralTokens.Bitcoin => WrappedBitcoinMint,
			JupiterCollateralTokens.Ethereum => WrappedEthereumMint,
			_ => throw new ArgumentOutOfRangeException(nameof(token), token,
				null),
		};

	public static int GetDecimals(this JupiterCollateralTokens token)
		=> token switch
		{
			JupiterCollateralTokens.Usdc => 6,
			JupiterCollateralTokens.Sol => 9,
			JupiterCollateralTokens.Bitcoin => 8,
			JupiterCollateralTokens.Ethereum => 8,
			_ => throw new ArgumentOutOfRangeException(nameof(token), token,
				null),
		};

	public static string GetSymbol(this JupiterCollateralTokens token)
		=> token switch
		{
			JupiterCollateralTokens.Usdc => "USDC",
			JupiterCollateralTokens.Sol => "SOL",
			JupiterCollateralTokens.Bitcoin => "BTC",
			JupiterCollateralTokens.Ethereum => "ETH",
			_ => throw new ArgumentOutOfRangeException(nameof(token), token,
				null),
		};

	public static string GetMint(this JupiterPerpetualAssets asset)
		=> asset switch
		{
			JupiterPerpetualAssets.Sol => WrappedSolMint,
			JupiterPerpetualAssets.Bitcoin => WrappedBitcoinMint,
			JupiterPerpetualAssets.Ethereum => WrappedEthereumMint,
			_ => throw new ArgumentOutOfRangeException(nameof(asset), asset,
				null),
		};

	public static string GetSymbol(this JupiterPerpetualAssets asset)
		=> asset switch
		{
			JupiterPerpetualAssets.Sol => "SOL",
			JupiterPerpetualAssets.Bitcoin => "BTC",
			JupiterPerpetualAssets.Ethereum => "ETH",
			_ => throw new ArgumentOutOfRangeException(nameof(asset), asset,
				null),
		};

	public static JupiterPerpetualAssets ToPerpetualAsset(this string mint)
	{
		mint = mint.NormalizePublicKey();
		if (mint == WrappedSolMint)
			return JupiterPerpetualAssets.Sol;
		if (mint == WrappedBitcoinMint)
			return JupiterPerpetualAssets.Bitcoin;
		if (mint == WrappedEthereumMint)
			return JupiterPerpetualAssets.Ethereum;
		throw new InvalidDataException(
			$"Unsupported Jupiter Perps market mint '{mint}'.");
	}

	private static decimal DecimalScale(int decimals)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals), decimals,
				"Token decimals must be between zero and 28.");
		var result = 1m;
		for (var index = 0; index < decimals; index++)
			result *= 10m;
		return result;
	}
}
