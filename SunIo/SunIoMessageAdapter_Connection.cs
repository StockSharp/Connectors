namespace StockSharp.SunIo;

public partial class SunIoMessageAdapter
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
			SmartRouterAddress = SmartRouterAddress.NormalizeTronAddress();
			_apiClient = new(DataEndpoint, RouterEndpoint, NodeEndpoint,
				SunApiKey, TronApiKey)
			{
				Parent = this,
			};
			var block = await ApiClient.GetNowBlockAsync(cancellationToken);
			if (block?.Header?.Data is not { Number: > 0 } ||
				block.Id.IsEmpty())
				throw new InvalidDataException(
					"TRON FullNode returned no current block information.");

			var definitions = ParseMarketDefinitions();
			SunIoToken[] tokens;
			if (definitions.Length > 0)
				tokens = await ApiClient.GetTokensAsync(
					[.. definitions.Select(static item => item.TokenAddress),
						SunIoExtensions.NativeTrxAddress], cancellationToken);
			else
			{
				tokens = await ApiClient.GetTopTokensAsync(
					(MaximumDiscoveredMarkets * 4).Min(100),
					cancellationToken);
				if (!tokens.Any(static token => token?.Address.Equals(
					SunIoExtensions.NativeTrxAddress,
					StringComparison.Ordinal) == true))
					tokens = [.. tokens, .. await ApiClient.GetTokensAsync(
						[SunIoExtensions.NativeTrxAddress], cancellationToken)];
			}
			RegisterMarkets(tokens, definitions);
			if (_markets.Count == 0)
				throw new InvalidOperationException(
					"No liquid SUN.io tokens matched the connector settings.");

			connectMsg.SessionId = $"SUN.io TRON #{block.Header.Data.Number} " +
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
			if (_apiClient is not null &&
				(_level1Subscriptions.Count > 0 ||
					_tickSubscriptions.Count > 0 ||
					_candleSubscriptions.Count > 0) &&
				CurrentTime >= _nextMarketPoll)
			{
				_nextMarketPoll = CurrentTime + PollingInterval;
				pollMarket = true;
			}
			if (_apiClient is not null && _signer?.IsWalletAvailable == true &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_trackedSwaps.Values.Any(static swap =>
						swap.State == OrderStates.Active)) &&
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

	private SunIoMarketDefinition[] ParseMarketDefinitions()
	{
		if (Markets.IsEmpty())
			return [];
		var result = new List<SunIoMarketDefinition>();
		var addresses = new HashSet<string>(StringComparer.Ordinal);
		var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in Markets.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not (1 or 2))
				throw new FormatException(
					"Each SUN.io market must use token-address or " +
					"token-address|security-code format.");
			var address = fields[0].NormalizeTronAddress();
			if (address.IsTrx() || address.Equals(
				SunIoExtensions.WrappedTrxAddress, StringComparison.Ordinal))
				throw new FormatException(
					"Native or wrapped TRX cannot be its own destination market.");
			if (!addresses.Add(address))
				throw new FormatException(
					$"Duplicate SUN.io token address '{address}'.");
			var code = fields.Length == 2
				? fields[1].NormalizeSecurityCode()
				: null;
			if (!code.IsEmpty() && !codes.Add(code))
				throw new FormatException(
					$"Duplicate SUN.io security code '{code}'.");
			result.Add(new()
			{
				TokenAddress = address,
				SecurityCode = code,
			});
		}
		if (result.Count > 100)
			throw new FormatException(
				"SUN.io market configuration cannot exceed 100 tokens.");
		return [.. result];
	}

	private void RegisterMarkets(IEnumerable<SunIoToken> source,
		SunIoMarketDefinition[] definitions)
	{
		var tokens = new Dictionary<string, SunIoToken>(StringComparer.Ordinal);
		foreach (var token in source ?? [])
		{
			if (token is null || token.Address.IsEmpty())
				continue;
			ValidateToken(token);
			token.Address = token.Address.NormalizeTronAddress();
			if (!tokens.TryAdd(token.Address, token))
				throw new InvalidDataException(
					$"SUN.io returned duplicate token '{token.Address}'.");
		}
		if (!tokens.TryGetValue(SunIoExtensions.NativeTrxAddress,
			out _trxToken))
			throw new InvalidDataException(
				"SUN.io token metadata does not contain native TRX.");

		var selected = new List<(SunIoToken Token, string Code)>();
		if (definitions.Length > 0)
		{
			foreach (var definition in definitions)
			{
				if (!tokens.TryGetValue(definition.TokenAddress, out var token))
					throw new InvalidDataException(
						$"SUN.io token '{definition.TokenAddress}' was not found.");
				selected.Add((token, definition.SecurityCode));
			}
		}
		else
		{
			selected.AddRange(tokens.Values
				.Where(token => !token.Address.IsTrx() &&
					!token.Address.Equals(SunIoExtensions.WrappedTrxAddress,
						StringComparison.Ordinal) &&
					token.ReserveUsd >= MinimumLiquidityUsd)
				.OrderByDescending(static token => token.VolumeUsd24Hours)
				.ThenByDescending(static token => token.ReserveUsd)
				.Take(MaximumDiscoveredMarkets)
				.Select(static token => (token, (string)null)));
		}

		var symbolCounts = selected.GroupBy(static item => item.Token.Symbol,
			StringComparer.OrdinalIgnoreCase).ToDictionary(static group =>
				group.Key, static group => group.Count(),
				StringComparer.OrdinalIgnoreCase);
		foreach (var item in selected)
		{
			var suffix = symbolCounts[item.Token.Symbol] == 1
				? item.Token.Symbol
				: $"{item.Token.Symbol}-{item.Token.Address[..6]}";
			var code = item.Code.IsEmpty()
				? $"TRX-{suffix}".NormalizeSecurityCode()
				: item.Code;
			var market = new SunIoMarket
			{
				Token = item.Token,
				SecurityCode = code,
				BalanceCode = suffix.NormalizeSecurityCode(),
			};
			using (_sync.EnterScope())
			{
				if (!_markets.TryAdd(code, market))
					throw new InvalidDataException(
						$"Duplicate SUN.io security code '{code}'.");
				_marketsByAddress.Add(item.Token.Address, market);
			}
		}
	}

	private static void ValidateToken(SunIoToken token)
	{
		_ = token.Address.NormalizeTronAddress();
		if (token.Symbol.IsEmpty() || token.Symbol.Length > 32 ||
			token.Name.IsEmpty() || token.Name.Length > 256 ||
			token.Decimals is < 0 or > 18 || token.PriceUsd < 0 ||
			token.ReserveUsd < 0 || token.VolumeUsd24Hours < 0)
			throw new InvalidDataException(
				$"SUN.io token '{token.Address}' contains invalid metadata.");
		token.Symbol = token.Symbol.Trim().ToUpperInvariant();
		token.Name = token.Name.Trim();
	}

	private async ValueTask RefreshTokensAsync(
		CancellationToken cancellationToken)
	{
		string[] addresses;
		using (_sync.EnterScope())
			addresses = [SunIoExtensions.NativeTrxAddress,
				.. _marketsByAddress.Keys];
		var tokens = await ApiClient.GetTokensAsync(addresses,
			cancellationToken);
		foreach (var token in tokens)
		{
			if (token is null || token.Address.IsEmpty())
				continue;
			ValidateToken(token);
			token.Address = token.Address.NormalizeTronAddress();
			using (_sync.EnterScope())
			{
				if (token.Address.IsTrx())
					_trxToken = token;
				else if (_marketsByAddress.TryGetValue(token.Address,
					out var market))
					market.Token = token;
			}
		}
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
		_trxToken = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByAddress.Clear();
			_level1Subscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_seenTrades.Clear();
			_tradeDeliveryOrder.Clear();
			_level1Fingerprints.Clear();
			_candleFingerprints.Clear();
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
