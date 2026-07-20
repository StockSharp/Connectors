namespace StockSharp.Jupiter;

public partial class JupiterMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_apiClient is not null || _signer is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(WalletAddress, PrivateKey);
			WalletAddress = Signer.WalletAddress;
			_apiClient = new(ApiEndpoint, PerpetualEndpoint, ApiKey)
			{
				Parent = this,
			};

			var definitions = ParseMarketDefinitions();
			if (definitions.Length == 0 && !IsPerpetualsEnabled)
				throw new InvalidOperationException(
					"At least one Jupiter spot or perpetual market must be " +
					"enabled.");
			var requiredMints = definitions.SelectMany(static definition =>
				new[] { definition.BaseMint, definition.QuoteMint }).ToList();
			if (IsPerpetualsEnabled)
				requiredMints.AddRange([
					JupiterExtensions.WrappedSolMint,
					JupiterExtensions.WrappedBitcoinMint,
					JupiterExtensions.WrappedEthereumMint,
					JupiterExtensions.UsdcMint,
				]);
			await LoadTokensAsync(requiredMints, cancellationToken);
			RegisterMarkets(definitions);

			if (IsPerpetualsEnabled)
			{
				var stats = await ApiClient.GetPerpetualStatsAsync(
					JupiterExtensions.WrappedSolMint, cancellationToken);
				var sol = GetMarketByCode("SOL-PERP");
				sol.LastPrice = JupiterExtensions.ParseDecimal(stats.Price,
					"SOL perpetual price");
			}

			connectMsg.SessionId = "Jupiter " +
				(Signer.IsWalletAvailable
					? Signer.WalletAddress[..8]
					: "public");
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			DisposeClients();
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
			cancellationToken);
		DisposeClients();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClients();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var pollMarket = false;
		var pollPrivate = false;
		using (_sync.EnterScope())
		{
			if (_apiClient is not null && _level1Subscriptions.Count > 0 &&
				CurrentTime >= _nextMarketPoll)
			{
				_nextMarketPoll = CurrentTime + PollingInterval;
				pollMarket = true;
			}
			if (_apiClient is not null && _signer?.IsWalletAvailable == true &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_trackedOrders.Values.Any(static order =>
						order.State == OrderStates.Active)) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				pollPrivate = true;
			}
		}
		if (pollMarket)
			await RunSafelyAsync(PollMarketAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private JupiterMarketDefinition[] ParseMarketDefinitions()
	{
		if (SpotMarkets.IsEmpty())
			return [];
		var result = new List<JupiterMarketDefinition>();
		var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var pairs = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in SpotMarkets.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not (2 or 3))
				throw new FormatException(
					"Each Jupiter spot market must use " +
					"base-mint|quote-mint or " +
					"base-mint|quote-mint|security-code format.");
			var baseMint = fields[0].NormalizePublicKey();
			var quoteMint = fields[1].NormalizePublicKey();
			if (baseMint == quoteMint)
				throw new FormatException(
					"A Jupiter market cannot use the same base and quote mint.");
			var code = fields.Length == 3
				? NormalizeSecurityCode(fields[2])
				: null;
			var pair = baseMint + ":" + quoteMint;
			if (!pairs.Add(pair))
				throw new FormatException(
					$"Duplicate Jupiter market pair '{pair}'.");
			if (!code.IsEmpty() && !codes.Add(code))
				throw new FormatException(
					$"Duplicate Jupiter security code '{code}'.");
			result.Add(new()
			{
				BaseMint = baseMint,
				QuoteMint = quoteMint,
				SecurityCode = code,
			});
		}
		return result.ToArray();
	}

	private async ValueTask LoadTokensAsync(IEnumerable<string> mints,
		CancellationToken cancellationToken)
	{
		var expected = (mints ?? []).Select(static mint =>
			mint.NormalizePublicKey()).Distinct(StringComparer.Ordinal).ToArray();
		var result = new Dictionary<string, JupiterToken>(StringComparer.Ordinal);
		for (var offset = 0; offset < expected.Length; offset += 100)
			foreach (var token in await ApiClient.GetTokensAsync(
				expected.Skip(offset).Take(100), cancellationToken))
			{
				if (token is null || token.Mint.IsEmpty())
					continue;
				var mint = token.Mint.NormalizePublicKey();
				if (!expected.Contains(mint, StringComparer.Ordinal))
					continue;
				ValidateToken(token, mint);
				token.Mint = mint;
				token.TokenProgram = token.TokenProgram.NormalizePublicKey();
				token.Symbol = NormalizeTokenSymbol(token.Symbol, mint);
				token.Name = token.Name.IsEmpty()
					? token.Symbol
					: token.Name.Trim();
				result[mint] = token;
			}
		var missing = expected.Where(mint => !result.ContainsKey(mint))
			.ToArray();
		if (missing.Length > 0)
			throw new InvalidDataException(
				"Jupiter returned no token metadata for: " +
				string.Join(", ", missing));
		using (_sync.EnterScope())
			foreach (var pair in result)
				_tokens[pair.Key] = pair.Value;
	}

	private void RegisterMarkets(IEnumerable<JupiterMarketDefinition> definitions)
	{
		var markets = new List<JupiterMarket>();
		foreach (var definition in definitions)
		{
			var baseToken = GetToken(definition.BaseMint);
			var quoteToken = GetToken(definition.QuoteMint);
			markets.Add(new()
			{
				Kind = JupiterMarketKinds.Spot,
				BaseToken = baseToken,
				QuoteToken = quoteToken,
				SecurityCode = definition.SecurityCode ??
					NormalizeSecurityCode(
						$"{baseToken.Symbol}-{quoteToken.Symbol}"),
				LastPrice = baseToken.UsdPrice is decimal basePrice &&
					quoteToken.UsdPrice is decimal quotePrice && quotePrice > 0
						? basePrice / quotePrice
						: 0m,
			});
		}
		if (IsPerpetualsEnabled)
			foreach (var asset in new[]
			{
				JupiterPerpetualAssets.Sol,
				JupiterPerpetualAssets.Bitcoin,
				JupiterPerpetualAssets.Ethereum,
			})
				markets.Add(new()
				{
					Kind = JupiterMarketKinds.Perpetual,
					BaseToken = GetToken(asset.GetMint()),
					QuoteToken = GetToken(JupiterExtensions.UsdcMint),
					PerpetualAsset = asset,
					SecurityCode = asset.GetSymbol() + "-PERP",
				});

		using (_sync.EnterScope())
			foreach (var market in markets)
				if (!_markets.TryAdd(market.SecurityCode, market))
					throw new InvalidDataException(
						$"Duplicate Jupiter security code " +
						$"'{market.SecurityCode}'.");
	}

	private JupiterToken GetToken(string mint)
	{
		mint = mint.NormalizePublicKey();
		using (_sync.EnterScope())
			return _tokens.TryGetValue(mint, out var token)
				? token
				: throw new InvalidDataException(
					$"Jupiter token '{mint}' is not loaded.");
	}

	private JupiterMarket GetMarketByCode(string code)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidDataException(
					$"Jupiter market '{code}' is not loaded.");
	}

	private static void ValidateToken(JupiterToken token, string mint)
	{
		if (token.Decimals is < 0 or > 28)
			throw new InvalidDataException(
				$"Jupiter token '{mint}' has unsupported decimals " +
				$"'{token.Decimals}'.");
		var program = token.TokenProgram.NormalizePublicKey();
		if (program is not (JupiterExtensions.TokenProgramAddress or
			JupiterExtensions.Token2022ProgramAddress))
			throw new InvalidDataException(
				$"Jupiter token '{mint}' uses unsupported token program " +
				$"'{program}'.");
		_ = token.GetUiMultiplier(DateTime.UtcNow);
	}

	private static string NormalizeTokenSymbol(string value, string mint)
	{
		value = value?.Trim();
		if (value.IsEmpty() || value.Length > 20 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			return mint[..8].ToUpperInvariant();
		return value.ToUpperInvariant();
	}

	private static string NormalizeSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			throw new FormatException(
				$"Invalid Jupiter security code '{value}'.");
		return value;
	}

	private async ValueTask RunSafelyAsync(
		Func<CancellationToken, ValueTask> action,
		CancellationToken cancellationToken)
	{
		try
		{
			await action(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private void DisposeClients()
	{
		_apiClient?.Dispose();
		_apiClient = null;
		_signer?.Dispose();
		_signer = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_tokens.Clear();
			_level1Subscriptions.Clear();
			_level1Fingerprints.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedOrders.Clear();
			_balanceFingerprints.Clear();
			_positionFingerprints.Clear();
			_orderFingerprints.Clear();
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
		}
	}
}
