namespace StockSharp.KyberSwap;

public partial class KyberSwapMessageAdapter
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
				$"Unsupported KyberSwap chain '{Chain}'.");
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_rpcClient = new(RpcEndpoint, Chain, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			_httpClient = new(ApiEndpoint, Chain, ClientId)
			{
				Parent = this,
			};
			await RpcClient.VerifyAsync(cancellationToken);
			WalletAddress = RpcClient.IsWalletConfigured
				? RpcClient.WalletAddress
				: null;
			_ = await GetOrLoadTokenAsync(
				KyberSwapExtensions.NativeTokenAddress, cancellationToken);

			KyberSwapMarketDefinition[] definitions;
			try
			{
				definitions = ParseMarketDefinitions();
			}
			catch (Exception error)
			{
				throw new InvalidOperationException(
					"Configured KyberSwap market definitions are invalid.",
					error);
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
						"KyberSwap market {0}/{1} loading failed: {2}",
						definition.BaseToken, definition.QuoteToken,
						error.Message);
				}
			}

			KyberSwapMarket[] markets;
			using (_sync.EnterScope())
				markets = [.. _markets.Values];
			if (markets.Length == 0)
				throw errors.Count == 1
					? errors[0]
					: new AggregateException(
						"No KyberSwap markets could be loaded.", errors);

			connectMsg.SessionId = RpcClient.IsWalletConfigured
				? $"KyberSwap {Chain} {RpcClient.WalletAddress}"
				: $"KyberSwap {Chain} public";
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
		KyberSwapMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var baseToken = await GetOrLoadTokenAsync(definition.BaseToken,
			cancellationToken);
		var quoteToken = await GetOrLoadTokenAsync(definition.QuoteToken,
			cancellationToken);
		if (baseToken.Address.EqualsIgnoreCase(quoteToken.Address))
			throw new InvalidDataException(
				"KyberSwap market tokens must be different.");
		if (baseToken.Address.IsNativeToken() ||
			quoteToken.Address.IsNativeToken())
			throw new NotSupportedException(
				"Configure wrapped ERC-20 tokens for KyberSwap markets.");
		var code = definition.SecurityCode.IsEmpty()
			? NormalizeSecurityCode(
				$"{baseToken.Symbol}-{quoteToken.Symbol}")
			: NormalizeSecurityCode(definition.SecurityCode);
		var market = new KyberSwapMarket
		{
			BaseToken = baseToken,
			QuoteToken = quoteToken,
			SecurityCode = code,
		};
		var probe = ProbeVolume.ToBaseUnits(baseToken.Decimals);
		if (probe <= 0)
			throw new InvalidOperationException(
				"The configured KyberSwap quote probe volume rounds to zero.");
		var bid = await GetQuoteAsync(baseToken, quoteToken, probe,
			cancellationToken);
		_ = await GetQuoteAsync(quoteToken, baseToken, bid.OutputAmount,
			cancellationToken);
		RegisterMarket(market);
	}

	private async ValueTask<KyberSwapToken> GetOrLoadTokenAsync(string address,
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

	private void RegisterMarket(KyberSwapMarket market)
	{
		if (market?.BaseToken is null || market.QuoteToken is null ||
			market.SecurityCode.IsEmpty())
			throw new InvalidDataException(
				"KyberSwap market metadata is incomplete.");
		using (_sync.EnterScope())
		{
			if (_markets.ContainsKey(market.SecurityCode))
				throw new InvalidOperationException(
					$"KyberSwap security code '{market.SecurityCode}' is " +
					"configured twice.");
			if (_markets.Values.Any(item =>
				item.BaseToken.Address.EqualsIgnoreCase(
					market.BaseToken.Address) &&
				item.QuoteToken.Address.EqualsIgnoreCase(
					market.QuoteToken.Address)))
				throw new InvalidOperationException(
					"The same KyberSwap token pair is configured twice.");
			_tokens[market.BaseToken.Address] = market.BaseToken;
			_tokens[market.QuoteToken.Address] = market.QuoteToken;
			_markets.Add(market.SecurityCode, market);
		}
	}

	private KyberSwapMarketDefinition[] ParseMarketDefinitions()
	{
		var configured = Markets.IsEmpty()
			? Chain.GetDefaultMarkets()
			: Markets;
		var result = new List<KyberSwapMarketDefinition>();

		foreach (var item in configured.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not 2 and not 3)
				throw new FormatException(
					"Each KyberSwap market must use " +
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
				"At least one KyberSwap market must be configured.");
		return [.. result];
	}

	private static string NormalizeSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
			throw new FormatException(
				$"Invalid KyberSwap security code '{value}'.");
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
