namespace StockSharp.Balancer.Native;

static class BalancerExtensions
{
	public const string ZeroAddress =
		"0x0000000000000000000000000000000000000000";
	public const string Permit2Address =
		"0x000000000022d473030f116ddee9f6b43ac78ba3";
	public const string V2VaultAddress =
		"0xba12222222228d8ba445958a75a0704d566bf2c8";
	public const string V3VaultAddress =
		"0xba1333333333a1ba1108e8412f11850a5c319ba9";

	public static readonly string V2SwapTopic = AbiTopic(
		"Swap(bytes32,address,address,uint256,uint256)");
	public static readonly string V3SwapTopic = AbiTopic(
		"Swap(address,address,address,uint256,uint256,uint256,uint256)");
	public static readonly string[] SwapTopics = [V2SwapTopic, V3SwapTopic];

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

	private static readonly BalancerDeployment[] _deployments =
	[
		new()
		{
			Network = BalancerNetworks.Ethereum,
			Name = "Ethereum",
			ChainId = 1,
			GraphChain = BalancerGraphChains.Mainnet,
			RpcEndpoint = "https://ethereum-rpc.publicnode.com",
			WebSocketEndpoint = "wss://ethereum-rpc.publicnode.com",
			NativeSymbol = "ETH",
			V2Vault = V2VaultAddress,
			V3Vault = V3VaultAddress,
			V3Router = "0xae563e3f8219521950555f5962419c8919758ea2",
		},
		new()
		{
			Network = BalancerNetworks.Arbitrum,
			Name = "Arbitrum",
			ChainId = 42161,
			GraphChain = BalancerGraphChains.Arbitrum,
			RpcEndpoint = "https://arbitrum-one-rpc.publicnode.com",
			WebSocketEndpoint = "wss://arbitrum-one-rpc.publicnode.com",
			NativeSymbol = "ETH",
			V2Vault = V2VaultAddress,
			V3Vault = V3VaultAddress,
			V3Router = "0xeaedc32a51c510d35ebc11088fd5ff2b47aacf2e",
		},
		new()
		{
			Network = BalancerNetworks.Base,
			Name = "Base",
			ChainId = 8453,
			GraphChain = BalancerGraphChains.Base,
			RpcEndpoint = "https://base-rpc.publicnode.com",
			WebSocketEndpoint = "wss://base-rpc.publicnode.com",
			NativeSymbol = "ETH",
			V2Vault = V2VaultAddress,
			V3Vault = V3VaultAddress,
			V3Router = "0x3f170631ed9821ca51a59d996ab095162438dc10",
		},
		new()
		{
			Network = BalancerNetworks.Optimism,
			Name = "Optimism",
			ChainId = 10,
			GraphChain = BalancerGraphChains.Optimism,
			RpcEndpoint = "https://optimism-rpc.publicnode.com",
			WebSocketEndpoint = "wss://optimism-rpc.publicnode.com",
			NativeSymbol = "ETH",
			V2Vault = V2VaultAddress,
			V3Vault = V3VaultAddress,
			V3Router = "0xe2fa4e1d17725e72dcdafe943ecf45df4b9e285b",
		},
		new()
		{
			Network = BalancerNetworks.Polygon,
			Name = "Polygon",
			ChainId = 137,
			GraphChain = BalancerGraphChains.Polygon,
			RpcEndpoint = "https://polygon-bor-rpc.publicnode.com",
			WebSocketEndpoint = "wss://polygon-bor-rpc.publicnode.com",
			NativeSymbol = "POL",
			V2Vault = V2VaultAddress,
		},
		new()
		{
			Network = BalancerNetworks.Gnosis,
			Name = "Gnosis",
			ChainId = 100,
			GraphChain = BalancerGraphChains.Gnosis,
			RpcEndpoint = "https://gnosis-rpc.publicnode.com",
			WebSocketEndpoint = "wss://gnosis-rpc.publicnode.com",
			NativeSymbol = "XDAI",
			V2Vault = V2VaultAddress,
			V3Vault = V3VaultAddress,
			V3Router = "0x4eff2d77d9ffbaefb4b141a3e494c085b3ff4cb5",
		},
		new()
		{
			Network = BalancerNetworks.Avalanche,
			Name = "Avalanche",
			ChainId = 43114,
			GraphChain = BalancerGraphChains.Avalanche,
			RpcEndpoint = "https://avalanche-c-chain-rpc.publicnode.com",
			WebSocketEndpoint = "wss://avalanche-c-chain-rpc.publicnode.com",
			NativeSymbol = "AVAX",
			V2Vault = V2VaultAddress,
			V3Vault = V3VaultAddress,
			V3Router = "0xf39ca6ede9bf7820a952b52f3c94af526bab9015",
		},
	];

	public static BalancerDeployment GetDeployment(
		this BalancerNetworks network)
		=> _deployments.FirstOrDefault(item => item.Network == network) ??
			throw new ArgumentOutOfRangeException(nameof(network), network,
				"Unsupported Balancer network.");

	public static string NormalizeAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length != 42 || !value.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || value.Skip(2).Any(
			static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException($"Invalid EVM address '{value}'.",
				nameof(value));
		return "0x" + value[2..].ToLowerInvariant();
	}

	public static string NormalizeHash(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length != 66 || !value.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || value.Skip(2).Any(
			static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException(
				$"Invalid EVM transaction hash '{value}'.");
		return "0x" + value[2..].ToLowerInvariant();
	}

	public static string NormalizePoolId(this string value,
		int protocolVersion)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (protocolVersion == 3)
			return value.NormalizeAddress();
		if (protocolVersion != 2 || value.Length != 66 ||
			!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			value.Skip(2).Any(static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException($"Invalid Balancer pool id '{value}'.");
		return "0x" + value[2..].ToLowerInvariant();
	}

	public static string PoolAddress(this string poolId, int protocolVersion)
		=> protocolVersion == 3
			? poolId.NormalizeAddress()
			: ("0x" + poolId.NormalizePoolId(2).Substring(2, 40))
				.NormalizeAddress();

	public static BalancerPool ToBalancer(this BalancerGraphPool source)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (source.ProtocolVersion is not (2 or 3))
			throw new InvalidDataException(
				$"Unsupported Balancer protocol version {source.ProtocolVersion}.");
		var id = source.Id.NormalizePoolId(source.ProtocolVersion);
		var address = source.Address.NormalizeAddress();
		if (!address.EqualsIgnoreCase(id.PoolAddress(source.ProtocolVersion)))
			throw new InvalidDataException(
				$"Balancer pool '{id}' returned an inconsistent address.");
		var tokens = (source.Tokens ?? [])
			.Where(token => token is not null && !token.IsNestedPool &&
				token.IsAllowed && !token.Address.EqualsIgnoreCase(address))
			.Select(token => token.ToBalancer())
			.GroupBy(static token => token.Address,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static token => token.Index)
			.ToArray();
		if (tokens.Length < 2)
			throw new InvalidDataException(
				$"Balancer pool '{id}' has fewer than two directly tradable tokens.");
		var state = source.Dynamic ?? throw new InvalidDataException(
			$"Balancer pool '{id}' has no current state.");
		return new()
		{
			Id = id,
			Address = address,
			Name = source.Name?.Trim(),
			Symbol = NormalizeSymbol(source.Symbol),
			Type = source.Type,
			PoolVersion = source.Version,
			ProtocolVersion = source.ProtocolVersion,
			TotalLiquidity = ParseDecimal(state.TotalLiquidity,
				"pool liquidity"),
			Volume24Hours = ParseDecimal(state.Volume24Hours,
				"24-hour volume"),
			Fees24Hours = ParseDecimal(state.Fees24Hours, "24-hour fees"),
			SwapFee = ParseDecimal(state.SwapFee, "swap fee"),
			IsSwapEnabled = state.IsSwapEnabled && !state.IsPaused &&
				!state.IsInRecoveryMode,
			Tokens = tokens,
		};
	}

	private static BalancerToken ToBalancer(this BalancerGraphToken source)
	{
		if (source.Decimals is < 0 or > 255)
			throw new InvalidDataException(
				$"Balancer token '{source.Address}' has invalid decimals.");
		return new()
		{
			Address = source.Address.NormalizeAddress(),
			Symbol = NormalizeSymbol(source.Symbol),
			Name = source.Name?.Trim(),
			Decimals = source.Decimals,
			Index = source.Index,
		};
	}

	public static BalancerMarket CreateMarket(BalancerPool pool,
		BalancerToken baseToken, BalancerToken quoteToken, string securityCode)
	{
		ArgumentNullException.ThrowIfNull(pool);
		ArgumentNullException.ThrowIfNull(baseToken);
		ArgumentNullException.ThrowIfNull(quoteToken);
		if (baseToken.Address.EqualsIgnoreCase(quoteToken.Address))
			throw new ArgumentException("Balancer market tokens must differ.");
		securityCode = securityCode.IsEmpty()
			? CreateSecurityCode(pool, baseToken, quoteToken)
			: NormalizeSecurityCode(securityCode);
		return new()
		{
			Key = CreateMarketKey(pool.Id, baseToken.Address, quoteToken.Address),
			SecurityCode = securityCode,
			Pool = pool,
			BaseToken = baseToken,
			QuoteToken = quoteToken,
		};
	}

	public static string CreateMarketKey(string poolId, string baseToken,
		string quoteToken)
		=> poolId.Trim().ToLowerInvariant() + "|" +
			baseToken.NormalizeAddress() + "|" + quoteToken.NormalizeAddress();

	public static string CreateSecurityCode(BalancerPool pool,
		BalancerToken baseToken, BalancerToken quoteToken)
		=> NormalizeSecurityCode(baseToken.Symbol + "-" + quoteToken.Symbol +
			"-V" + pool.ProtocolVersion.ToString(CultureInfo.InvariantCulture) +
			"-" + pool.Address[2..10]);

	public static string NormalizeSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		var normalized = new string(value.Select(static ch =>
			char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-')
			.ToArray()).Trim('-');
		return normalized.ThrowIfEmpty(nameof(value));
	}

	public static string NormalizeSymbol(string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return "TOKEN";
		var result = new string(value.Where(static ch =>
			char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-').ToArray());
		return result.IsEmpty() ? "TOKEN" : result.ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this BalancerMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Balancer,
			Native = market.Key,
		};

	public static decimal ParseDecimal(string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"Balancer returned invalid {field} '{value}'.");
		return result;
	}

	public static decimal? TryParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ? result : null;

	public static BigInteger ParseInteger(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			var hex = value[2..];
			return hex.IsEmpty() ? BigInteger.Zero : BigInteger.Parse("0" + hex,
				NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		}
		return BigInteger.Parse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture);
	}

	public static string ToRpcHex(this BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
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

	public static DateTime ToUtcTime(this BigInteger seconds)
	{
		if (seconds < 0 || seconds > long.MaxValue)
			throw new InvalidDataException($"Invalid Unix timestamp '{seconds}'.");
		return DateTime.UnixEpoch.AddSeconds((long)seconds);
	}

	public static long ToUnixSeconds(this DateTime value)
	{
		value = value.EnsureUtc();
		return checked((long)(value - DateTime.UnixEpoch).TotalSeconds);
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Utc ? value :
			value.Kind == DateTimeKind.Local ? value.ToUniversalTime() :
			DateTime.SpecifyKind(value, DateTimeKind.Utc);

	public static DateTime Floor(this DateTime value, TimeSpan interval)
	{
		if (interval <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(interval));
		value = value.EnsureUtc();
		return new DateTime(value.Ticks - value.Ticks % interval.Ticks,
			DateTimeKind.Utc);
	}

	public static decimal Step(int decimals)
		=> 1m / (decimal)BigInteger.Pow(10, Math.Min(8, Math.Max(0, decimals)));

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string AbiSelector(string signature)
		=> new Sha3Keccack().CalculateHash(signature)[..8];

	public static string AbiTopic(string signature)
		=> "0x" + new Sha3Keccack().CalculateHash(signature);

	public static string AbiWord(BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		var hex = value.ToString("x", CultureInfo.InvariantCulture);
		if (hex.Length > 64)
			throw new ArgumentOutOfRangeException(nameof(value),
				"ABI integer exceeds 256 bits.");
		return hex.PadLeft(64, '0');
	}

	public static string AbiAddress(string address)
		=> address.NormalizeAddress()[2..].PadLeft(64, '0');

	public static string AbiBytes32(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length != 66 || !value.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || value.Skip(2).Any(
			static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException($"Invalid ABI bytes32 value '{value}'.");
		return value[2..].ToLowerInvariant();
	}

	public static string EncodeStaticCall(string signature,
		params string[] words)
		=> "0x" + AbiSelector(signature) + string.Concat(words);

	public static BigInteger ReadAbiWord(string value, int index)
	{
		if (value.IsEmpty() || !value.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("Invalid ABI response.");
		var start = 2 + checked(index * 64);
		if (start < 2 || start + 64 > value.Length)
			throw new InvalidDataException("ABI response is truncated.");
		return BigInteger.Parse("0" + value.Substring(start, 64),
			NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	}

	public static string ReadAbiAddress(string value, int index)
		=> ("0x" + ReadAbiWord(value, index).ToString("x",
			CultureInfo.InvariantCulture).PadLeft(40, '0')).NormalizeAddress();

	public static BalancerRawSwap DecodeSwap(BalancerRpcLog log,
		BalancerDeployment deployment)
	{
		ArgumentNullException.ThrowIfNull(log);
		ArgumentNullException.ThrowIfNull(deployment);
		if (log.IsRemoved || log.Topics?.Length != 4)
			return null;
		var topic = log.Topics[0];
		var address = log.Address.NormalizeAddress();
		string poolId;
		if (topic.EqualsIgnoreCase(V2SwapTopic) &&
			address.EqualsIgnoreCase(deployment.V2Vault))
			poolId = log.Topics[1].NormalizePoolId(2);
		else if (topic.EqualsIgnoreCase(V3SwapTopic) &&
			!deployment.V3Vault.IsEmpty() &&
			address.EqualsIgnoreCase(deployment.V3Vault))
			poolId = ReadTopicAddress(log.Topics[1]);
		else
			return null;
		var logIndex = log.LogIndex.ParseInteger();
		if (logIndex < 0 || logIndex > int.MaxValue)
			throw new InvalidDataException("Balancer log index is outside Int32.");
		return new()
		{
			PoolId = poolId,
			TokenIn = ReadTopicAddress(log.Topics[2]),
			TokenOut = ReadTopicAddress(log.Topics[3]),
			AmountIn = ReadAbiWord(log.Data, 0),
			AmountOut = ReadAbiWord(log.Data, 1),
			TransactionHash = log.TransactionHash.NormalizeHash(),
			BlockNumber = log.BlockNumber.ParseInteger(),
			LogIndex = (int)logIndex,
		};
	}

	public static bool TryCreateTrade(this BalancerRawSwap source,
		BalancerMarket market, DateTime time, out BalancerTrade trade)
	{
		trade = null;
		if (source is null || market is null ||
			!source.PoolId.EqualsIgnoreCase(market.Pool.Id))
			return false;
		Sides side;
		decimal volume;
		decimal quote;
		if (source.TokenIn.EqualsIgnoreCase(market.BaseToken.Address) &&
			source.TokenOut.EqualsIgnoreCase(market.QuoteToken.Address))
		{
			side = Sides.Sell;
			volume = source.AmountIn.FromBaseUnits(market.BaseToken.Decimals);
			quote = source.AmountOut.FromBaseUnits(market.QuoteToken.Decimals);
		}
		else if (source.TokenIn.EqualsIgnoreCase(market.QuoteToken.Address) &&
			source.TokenOut.EqualsIgnoreCase(market.BaseToken.Address))
		{
			side = Sides.Buy;
			volume = source.AmountOut.FromBaseUnits(market.BaseToken.Decimals);
			quote = source.AmountIn.FromBaseUnits(market.QuoteToken.Decimals);
		}
		else
			return false;
		if (volume <= 0 || quote <= 0)
			return false;
		trade = new()
		{
			Id = source.TransactionHash + ":" + source.LogIndex.ToString(
				CultureInfo.InvariantCulture),
			TransactionHash = source.TransactionHash,
			PoolId = source.PoolId,
			Time = time.EnsureUtc(),
			Price = quote / volume,
			Volume = volume,
			Side = side,
			BlockNumber = source.BlockNumber > long.MaxValue ? long.MaxValue :
				(long)source.BlockNumber,
			LogIndex = source.LogIndex,
		};
		return true;
	}

	public static string EncodeV3Swap(BalancerMarket market,
		BalancerSwapTypes swapType, BigInteger amount, BigInteger limit,
		long deadline)
	{
		var isExactIn = swapType == BalancerSwapTypes.ExactIn;
		var signature = isExactIn
			? "swapSingleTokenExactIn(address,address,address,uint256,uint256,uint256,bool,bytes)"
			: "swapSingleTokenExactOut(address,address,address,uint256,uint256,uint256,bool,bytes)";
		var tokenIn = isExactIn ? market.BaseToken.Address :
			market.QuoteToken.Address;
		var tokenOut = isExactIn ? market.QuoteToken.Address :
			market.BaseToken.Address;
		return "0x" + AbiSelector(signature) +
			AbiAddress(market.Pool.Address) + AbiAddress(tokenIn) +
			AbiAddress(tokenOut) + AbiWord(amount) + AbiWord(limit) +
			AbiWord(deadline) + AbiWord(BigInteger.Zero) + AbiWord(256) +
			AbiWord(BigInteger.Zero);
	}

	public static string EncodeV2Swap(BalancerMarket market,
		BalancerSwapTypes swapType, BigInteger amount, BigInteger limit,
		long deadline, string sender)
	{
		var isExactIn = swapType == BalancerSwapTypes.ExactIn;
		var tokenIn = isExactIn ? market.BaseToken.Address :
			market.QuoteToken.Address;
		var tokenOut = isExactIn ? market.QuoteToken.Address :
			market.BaseToken.Address;
		const string signature =
			"swap((bytes32,uint8,address,address,uint256,bytes),(address,bool,address,bool),uint256,uint256)";
		return "0x" + AbiSelector(signature) +
			AbiWord(224) +
			AbiAddress(sender) + AbiWord(BigInteger.Zero) +
			AbiAddress(sender) + AbiWord(BigInteger.Zero) +
			AbiWord(limit) + AbiWord(deadline) +
			AbiBytes32(market.Pool.Id) +
			AbiWord(isExactIn ? BigInteger.Zero : BigInteger.One) +
			AbiAddress(tokenIn) + AbiAddress(tokenOut) + AbiWord(amount) +
			AbiWord(192) + AbiWord(BigInteger.Zero);
	}

	private static string ReadTopicAddress(string topic)
	{
		topic = topic.ThrowIfEmpty(nameof(topic)).Trim();
		if (topic.Length != 66 || !topic.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || topic.Skip(2).Any(
			static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException($"Invalid indexed address '{topic}'.");
		return ("0x" + topic[^40..]).NormalizeAddress();
	}
}
