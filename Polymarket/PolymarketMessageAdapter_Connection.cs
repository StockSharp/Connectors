namespace StockSharp.Polymarket;

public partial class PolymarketMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _socketClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(PrivateKey, FunderAddress, SignatureType, BuilderCode);
			var signerAddress = SignerAddress.IsEmpty()
				? Signer.SignerAddress
				: SignerAddress.NormalizeAddress(nameof(SignerAddress));
			if (Signer.IsAvailable && !signerAddress.Equals(
				Signer.SignerAddress, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"The configured Polymarket signer address does not match " +
					"the private key.");
			if (SignatureType == PolymarketSignatureTypes.Eoa &&
				!signerAddress.IsEmpty() && !FunderAddress.IsEmpty() &&
				!FunderAddress.NormalizeAddress(
					nameof(FunderAddress)).Equals(signerAddress,
						StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"An EOA Polymarket funder address must match the signer address.");
			_authenticator = new(ApiKey, ApiSecret, Passphrase, signerAddress);
			_restClient = new(ClobEndpoint, DataEndpoint, _authenticator)
			{
				Parent = this,
			};
			var version = await RestClient.GetVersionAsync(cancellationToken);
			_orderVersion = version?.Version ?? 2;
			if (_orderVersion is not (2 or 3))
				throw new NotSupportedException(
					$"Polymarket CLOB order version {_orderVersion} is unsupported.");
			var serverSeconds = await RestClient.GetTimeAsync(cancellationToken);
			if (serverSeconds > 0)
				UpdateServerTime(DateTime.UnixEpoch.AddSeconds(serverSeconds));
			await RefreshMarketsAsync(cancellationToken);

			_socketClient = new(MarketSocketEndpoint, UserSocketEndpoint,
				_authenticator, ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_socketClient.EventReceived += OnSocketEventAsync;
			_socketClient.Error += OnSocketErrorAsync;
			_socketClient.StateChanged += OnSocketStateAsync;
			await SocketClient.ConnectAsync(cancellationToken);

			_portfolioAddress = Signer.FunderAddress.IsEmpty()
				? signerAddress
				: Signer.FunderAddress;
			_portfolioName = _portfolioAddress.IsEmpty()
				? null
				: "Polymarket_" + _portfolioAddress;
			using (_sync.EnterScope())
			{
				_nextPing = CurrentTime + TimeSpan.FromSeconds(10);
				_nextPrivatePoll = CurrentTime + PollingInterval;
				_nextMarketRefresh = CurrentTime + TimeSpan.FromMinutes(10);
			}
			connectMsg.SessionId = "Polymarket Polygon " +
				(_portfolioAddress.IsEmpty() ? "public" : _portfolioAddress);
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
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
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var isPing = false;
		var isPrivatePoll = false;
		var isMarketRefresh = false;
		using (_sync.EnterScope())
		{
			if (_socketClient is not null && CurrentTime >= _nextPing)
			{
				_nextPing = CurrentTime + TimeSpan.FromSeconds(10);
				isPing = true;
			}
			if (_restClient is not null &&
				((_portfolioSubscriptions.Count > 0 &&
					!_portfolioAddress.IsEmpty()) ||
					(_authenticator?.IsAvailable == true &&
						_orderSubscriptions.Count > 0)) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				isPrivatePoll = true;
			}
			if (_restClient is not null && CurrentTime >= _nextMarketRefresh)
			{
				_nextMarketRefresh = CurrentTime + TimeSpan.FromMinutes(10);
				isMarketRefresh = true;
			}
		}
		if (isPing)
			await RunSafelyAsync(SocketClient.PingAsync, cancellationToken);
		if (isMarketRefresh)
			await RunSafelyAsync(RefreshMarketsAsync, cancellationToken);
		if (isPrivatePoll)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var definitions = await RestClient.GetMarketsAsync(cancellationToken);
		var markets = new List<PolymarketMarket>();
		var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var definition in definitions ?? [])
		{
			if (definition?.ConditionId.IsEmpty() != false ||
				definition.Tokens is null)
				continue;
			foreach (var token in definition.Tokens)
			{
				if (token?.TokenId.IsEmpty() != false)
					continue;
				var code = PolymarketExtensions.ToSecurityCode(
					definition.MarketSlug, token.Outcome, token.TokenId);
				if (!usedCodes.Add(code))
				{
					code += ":" + token.TokenId[^Math.Min(8,
						token.TokenId.Length)..];
					if (!usedCodes.Add(code))
						throw new InvalidDataException(
							"Polymarket returned duplicate token ID '" +
							token.TokenId + "'.");
				}
				markets.Add(new()
				{
					SecurityCode = code,
					TokenId = token.TokenId,
					ConditionId = definition.ConditionId,
					Question = definition.Question,
					Description = definition.Description,
					Slug = definition.MarketSlug,
					Outcome = token.Outcome,
					ExpiryDate = definition.EndDate.TryParsePolymarketTime(),
					PriceStep = definition.MinimumTickSize > 0
						? definition.MinimumTickSize
						: 0.01m,
					MinimumVolume = definition.MinimumOrderSize > 0
						? definition.MinimumOrderSize
						: 0.01m,
					ReferencePrice = token.Price,
					IsNegativeRisk = definition.IsNegativeRisk,
					IsActive = definition.IsActive && !definition.IsClosed &&
						!definition.IsArchived && definition.IsOrderBookEnabled &&
						definition.IsAcceptingOrders,
				});
			}
		}
		if (markets.Count == 0)
			throw new InvalidDataException(
				"Polymarket returned no usable outcome tokens.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByToken.Clear();
			foreach (var market in markets)
			{
				_markets.Add(market.SecurityCode, market);
				_marketsByToken.Add(market.TokenId, market);
			}
		}
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
		if (state == ConnectionStates.Restored)
			await RefreshMarketsAsync(cancellationToken);
	}

	private async ValueTask RunSafelyAsync(
		Func<CancellationToken, ValueTask> action,
		CancellationToken cancellationToken)
	{
		try
		{
			await action(cancellationToken);
		}
		catch (Exception error)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socketClient;
		var rest = _restClient;
		_socketClient = null;
		_restClient = null;
		if (socket is not null)
		{
			socket.EventReceived -= OnSocketEventAsync;
			socket.Error -= OnSocketErrorAsync;
			socket.StateChanged -= OnSocketStateAsync;
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			finally
			{
				socket.Dispose();
			}
		}
		rest?.Dispose();
		ClearState();
	}
}
