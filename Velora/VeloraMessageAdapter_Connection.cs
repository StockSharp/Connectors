namespace StockSharp.Velora;

public partial class VeloraMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rpcClient is not null || _httpClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (!System.Enum.IsDefined(Chain))
			throw new InvalidOperationException(
				$"Unsupported Velora chain '{Chain}'.");
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_rpcClient = new(RpcEndpoint, Chain, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			_httpClient = new(ApiEndpoint, Chain, Partner)
			{
				Parent = this,
			};
			await RpcClient.VerifyAsync(cancellationToken);
			WalletAddress = RpcClient.IsWalletConfigured
				? RpcClient.WalletAddress
				: null;
			_ = await GetOrLoadTokenAsync(
				VeloraExtensions.NativeTokenAddress, cancellationToken);

			VeloraMarketDefinition[] definitions;
			try
			{
				definitions = ParseMarketDefinitions();
			}
			catch (Exception error)
			{
				throw new InvalidOperationException(
					"Configured Velora market definitions are invalid.", error);
			}
			var errors = new List<Exception>();

			foreach (var definition in definitions)
			{
				try
				{
					await RegisterDefinitionAsync(definition,
						cancellationToken);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog(
						"Velora market {0}/{1} loading failed: {2}",
						definition.BaseToken, definition.QuoteToken,
						error.Message);
				}
			}

			VeloraMarket[] markets;
			using (_sync.EnterScope())
				markets = [.. _markets.Values];
			if (markets.Length == 0)
				throw errors.Count == 1
					? errors[0]
					: new AggregateException(
						"No Velora markets could be loaded.", errors);

			connectMsg.SessionId = RpcClient.IsWalletConfigured
				? $"Velora {Chain} {RpcClient.WalletAddress}"
				: $"Velora {Chain} public";
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
		var now = DateTime.UtcNow;
		var pollMarket = false;
		var pollPrivate = false;
		using (_sync.EnterScope())
		{
			if (_rpcClient is not null && _httpClient is not null &&
				_level1Subscriptions.Count > 0 && now >= _nextMarketPoll)
			{
				_nextMarketPoll = now + PollingInterval;
				pollMarket = true;
			}
			if (_rpcClient is not null && _httpClient is not null &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_trackedSwaps.Values.Any(static swap =>
						swap.State == OrderStates.Active)) &&
				now >= _nextPrivatePoll)
			{
				_nextPrivatePoll = now + PollingInterval;
				pollPrivate = true;
			}
		}
		if (pollMarket)
			await RunSafelyAsync(PollMarketAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RegisterDefinitionAsync(
		VeloraMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var baseToken = await GetOrLoadTokenAsync(definition.BaseToken,
			cancellationToken);
		var quoteToken = await GetOrLoadTokenAsync(definition.QuoteToken,
			cancellationToken);
		if (baseToken.Address.EqualsIgnoreCase(quoteToken.Address))
			throw new InvalidDataException(
				"Velora market tokens must be different.");
		if (baseToken.Address.IsNativeToken() ||
			quoteToken.Address.IsNativeToken())
			throw new NotSupportedException(
				"Configure wrapped ERC-20 tokens for Velora markets.");
		var code = definition.SecurityCode.IsEmpty()
			? NormalizeSecurityCode(
				$"{baseToken.Symbol}-{quoteToken.Symbol}")
			: NormalizeSecurityCode(definition.SecurityCode);
		var market = new VeloraMarket
		{
			BaseToken = baseToken,
			QuoteToken = quoteToken,
			SecurityCode = code,
		};
		var probe = ProbeVolume.ToBaseUnits(baseToken.Decimals);
		if (probe <= 0)
			throw new InvalidOperationException(
				"The configured Velora quote probe volume rounds to zero.");
		var bid = await GetQuoteAsync(baseToken, quoteToken, probe,
			cancellationToken);
		_ = await GetQuoteAsync(quoteToken, baseToken, bid.OutputAmount,
			cancellationToken);
		RegisterMarket(market);
	}

	private async ValueTask<VeloraToken> GetOrLoadTokenAsync(string address,
		CancellationToken cancellationToken)
	{
		address = address.NormalizeAddress();
		using (_sync.EnterScope())
			if (_tokens.TryGetValue(address, out var cached))
				return cached;
		var token = await RpcClient.GetTokenAsync(address,
			cancellationToken);
		using (_sync.EnterScope())
		{
			_tokens[token.Address] = token;
			return token;
		}
	}

	private void RegisterMarket(VeloraMarket market)
	{
		if (market?.BaseToken is null || market.QuoteToken is null ||
			market.SecurityCode.IsEmpty())
			throw new InvalidDataException(
				"Velora market metadata is incomplete.");
		using (_sync.EnterScope())
		{
			if (_markets.ContainsKey(market.SecurityCode))
				throw new InvalidOperationException(
					$"Velora security code '{market.SecurityCode}' is " +
					"configured twice.");
			if (_markets.Values.Any(item =>
				item.BaseToken.Address.EqualsIgnoreCase(
					market.BaseToken.Address) &&
				item.QuoteToken.Address.EqualsIgnoreCase(
					market.QuoteToken.Address)))
				throw new InvalidOperationException(
					"The same Velora token pair is configured twice.");
			_tokens[market.BaseToken.Address] = market.BaseToken;
			_tokens[market.QuoteToken.Address] = market.QuoteToken;
			_markets.Add(market.SecurityCode, market);
		}
	}

	private VeloraMarketDefinition[] ParseMarketDefinitions()
	{
		var configured = Markets.IsEmpty()
			? Chain.GetDefaultMarkets()
			: Markets;
		var result = new List<VeloraMarketDefinition>();

		foreach (var item in configured.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not 2 and not 3)
				throw new FormatException(
					"Each Velora market must use " +
					"base-token|quote-token|security-code format; the security " +
					"code is optional.");
			result.Add(new()
			{
				BaseToken = fields[0].NormalizeAddress(),
				QuoteToken = fields[1].NormalizeAddress(),
				SecurityCode = fields.Length == 3
					? NormalizeSecurityCode(fields[2])
					: null,
			});
		}

		if (result.Count == 0)
			throw new InvalidOperationException(
				"At least one Velora market must be configured.");
		return [.. result];
	}

	private static string NormalizeSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
			throw new FormatException(
				$"Invalid Velora security code '{value}'.");
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
		_httpClient?.Dispose();
		_httpClient = null;
		_rpcClient?.Dispose();
		_rpcClient = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_tokens.Clear();
			_level1Subscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedSwaps.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
		}
	}
}
