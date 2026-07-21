namespace StockSharp.Anchorage;

public partial class AnchorageMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _socketClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (!MarketDataAccount.IsEmpty() && !MarketDataSubaccount.IsEmpty())
			throw new InvalidOperationException(
				"Only one Anchorage market-data account scope may be configured.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			ApiEndpoint = ApiEndpoint.NormalizeAnchorageEndpoint();
			_restClient = new(ApiEndpoint, ApiKey, SigningKey)
			{
				Parent = this,
			};

			var products = await TryReadDomainAsync(
				RestClient.GetTradePairsAsync, Array.Empty<AnchorageTradePair>(),
				"trading pairs", cancellationToken);
			var assets = await TryReadDomainAsync(
				RestClient.GetAssetTypesAsync, Array.Empty<AnchorageAssetType>(),
				"asset types", cancellationToken);
			var accounts = await TryReadDomainAsync(
				RestClient.GetTradingAccountsAsync,
				Array.Empty<AnchorageTradingAccount>(), "trading accounts",
				cancellationToken);
			var vaults = await TryReadDomainAsync(
				ct => RestClient.GetVaultsAsync(PageSize, MaximumItems, ct),
				Array.Empty<AnchorageVault>(), "vaults", cancellationToken);
			var wallets = await TryReadDomainAsync(
				ct => RestClient.GetWalletsAsync(PageSize, MaximumItems, ct),
				Array.Empty<AnchorageWallet>(), "wallets", cancellationToken);
			UpdateReferenceData(products, assets, accounts, vaults, wallets);
			_marketDataAccountId = ResolveMarketDataAccount(accounts);

			if (RestClient.IsSigningAvailable)
			{
				_socketClient = new(SocketEndpoint, ApiKey, SigningKey,
					ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				_socketClient.MarketDataReceived += OnMarketDataReceivedAsync;
				_socketClient.ExecutionReceived += OnExecutionReceivedAsync;
				_socketClient.Error += OnSocketErrorAsync;
				_socketClient.StateChanged += OnSocketStateAsync;
				await _socketClient.EnsureConnectedAsync(cancellationToken);
			}

			using (_sync.EnterScope())
			{
				_nextPrivatePoll = DateTime.UtcNow + PollingInterval;
				_nextMarketPoll = DateTime.UtcNow + MarketPollingInterval;
			}
			connectMsg.SessionId = $"Anchorage {Environment}; " +
				$"{products.Length} pairs, {accounts.Length} trading accounts, " +
				$"{vaults.Length} vaults; " +
				(IsSocketAvailable ? "WebSocket" : "REST read-only");
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
		var currentTime = CurrentTime.EnsureUtc();
		var isPrivatePollRequired = false;
		var isMarketPollRequired = false;
		using (_sync.EnterScope())
		{
			if (_restClient is not null &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_activeTradingOrders.Count > 0 ||
					_activeTransfers.Count > 0 ||
					_activeTransactions.Count > 0) &&
				currentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = currentTime + PollingInterval;
				isPrivatePollRequired = true;
			}
			if (_restClient is not null && _socketClient is null &&
				_marketSubscriptions.Count > 0 &&
				currentTime >= _nextMarketPoll)
			{
				_nextMarketPoll = currentTime + MarketPollingInterval;
				isMarketPollRequired = true;
			}
		}
		if (isPrivatePollRequired)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		if (isMarketPollRequired)
			await RunSafelyAsync(PollMarketDataAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private string ResolveMarketDataAccount(
		IEnumerable<AnchorageTradingAccount> accounts)
	{
		var requested = MarketDataAccount?.Trim();
		if (requested.IsEmpty())
			return null;
		var matches = accounts.Where(account => account is not null &&
			(account.Id.EqualsIgnoreCase(requested) ||
				account.Name.EqualsIgnoreCase(requested))).ToArray();
		return matches.Length switch
		{
			1 => matches[0].Id,
			0 => throw new InvalidOperationException(
				$"Anchorage trading account '{requested}' is not available."),
			_ => throw new InvalidOperationException(
				$"Anchorage account name '{requested}' is ambiguous; configure its ID."),
		};
	}

	private async ValueTask RefreshReferenceDataAsync(
		CancellationToken cancellationToken)
	{
		var products = await TryReadDomainAsync(RestClient.GetTradePairsAsync,
			Array.Empty<AnchorageTradePair>(), "trading pairs",
			cancellationToken);
		var assets = await TryReadDomainAsync(RestClient.GetAssetTypesAsync,
			Array.Empty<AnchorageAssetType>(), "asset types", cancellationToken);
		var accounts = await TryReadDomainAsync(
			RestClient.GetTradingAccountsAsync,
			Array.Empty<AnchorageTradingAccount>(), "trading accounts",
			cancellationToken);
		var vaults = await TryReadDomainAsync(
			ct => RestClient.GetVaultsAsync(PageSize, MaximumItems, ct),
			Array.Empty<AnchorageVault>(), "vaults", cancellationToken);
		var wallets = await TryReadDomainAsync(
			ct => RestClient.GetWalletsAsync(PageSize, MaximumItems, ct),
			Array.Empty<AnchorageWallet>(), "wallets", cancellationToken);
		UpdateReferenceData(products, assets, accounts, vaults, wallets);
		_marketDataAccountId = ResolveMarketDataAccount(accounts);
	}

	private async ValueTask<T> TryReadDomainAsync<T>(
		Func<CancellationToken, ValueTask<T>> action, T fallback,
		string domain, CancellationToken cancellationToken)
	{
		try
		{
			return await action(cancellationToken);
		}
		catch (AnchorageApiException error) when (error.StatusCode is
			HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
		{
			this.AddWarningLog(
				"Anchorage API key cannot read {0}; that domain is disabled: {1}",
				domain, error.Message);
			return fallback;
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
		{
			using (_sync.EnterScope())
				_nextPrivatePoll = default;
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

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socketClient;
		var rest = _restClient;
		_socketClient = null;
		_restClient = null;
		var errors = new List<Exception>();
		if (socket is not null)
		{
			socket.MarketDataReceived -= OnMarketDataReceivedAsync;
			socket.ExecutionReceived -= OnExecutionReceivedAsync;
			socket.Error -= OnSocketErrorAsync;
			socket.StateChanged -= OnSocketStateAsync;
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
			try
			{
				socket.Dispose();
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
		}
		try
		{
			rest?.Dispose();
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
		ClearState();
		if (errors.Count == 1)
			ExceptionDispatchInfo.Capture(errors[0]).Throw();
		if (errors.Count > 1)
			throw new AggregateException(
				"One or more Anchorage clients could not be disposed.", errors);
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_products.Clear();
			_assets.Clear();
			_portfolios.Clear();
			_wallets.Clear();
			_marketSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedOperations.Clear();
			_clientOperations.Clear();
			_nativeIds.Clear();
			_activeTradingOrders.Clear();
			_activeTransfers.Clear();
			_activeTransactions.Clear();
			_balanceFingerprints.Clear();
			_tradingOrderFingerprints.Clear();
			_transferFingerprints.Clear();
			_transactionFingerprints.Clear();
			_seenTrades.Clear();
			_marketDataAccountId = null;
			_nextPrivatePoll = default;
			_nextMarketPoll = default;
		}
	}
}
