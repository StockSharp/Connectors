namespace StockSharp.Deriv;

public partial class DerivMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_webSocketClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		var token = Token?.UnSecure();
		var hasToken = !token.IsEmpty();
		if (hasToken != !AppId.IsEmpty())
			throw new InvalidOperationException(
				"Deriv access token and application ID must be configured together.");
		if (!hasToken && !AccountId.IsEmpty())
			throw new InvalidOperationException(
				"A Deriv account ID cannot be used without an access token and application ID.");

		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			if (hasToken)
			{
				_restClient = new(RestAddress, Token, AppId) { Parent = this };
				_account = await ResolveAccountAsync(cancellationToken);
				this.AddInfoLog("Connecting authenticated Deriv {0} account {1}.",
					IsDemo ? "demo" : "real", _account.AccountId);
			}
			else
			{
				this.AddInfoLog("Connecting Deriv public market-data session.");
			}

			_webSocketClient = new(GetWebSocketEndpointAsync,
				ReConnectionSettings.ReAttemptCount) { Parent = this };
			_webSocketClient.SubscriptionReceived += ProcessSubscriptionAsync;
			_webSocketClient.Error += SendOutErrorAsync;
			_webSocketClient.StateChanged += ProcessWebSocketStateAsync;
			await _webSocketClient.ConnectAsync(cancellationToken);
			await LoadSymbolsAsync(cancellationToken);
			_lastPing = DateTime.UtcNow;
			_lastAccountRefresh = DateTime.UtcNow;
			await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(CancellationToken.None);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
				CancellationToken.None);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		if (_webSocketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
		this.AddInfoLog("Deriv disconnected.");
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
		_ = timeMsg;
		var now = DateTime.UtcNow;
		if (_webSocketClient?.IsConnected == true &&
			now - _lastPing >= TimeSpan.FromSeconds(30))
		{
			await _webSocketClient.PingAsync(cancellationToken);
			_lastPing = now;
		}

		if (_account is not null &&
			(_portfolioSubscriptionId != 0 || _orderStatusSubscriptionId != 0) &&
			now - _lastAccountRefresh >= TimeSpan.FromSeconds(15))
		{
			await RefreshAccountSubscriptionsAsync(cancellationToken);
			_lastAccountRefresh = now;
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask<DerivRestAccount> ResolveAccountAsync(
		CancellationToken cancellationToken)
	{
		var accounts = await _restClient.GetAccountsAsync(cancellationToken);
		var expectedType = IsDemo ? "demo" : "real";
		var account = !AccountId.IsEmpty()
			? accounts.FirstOrDefault(item => item.AccountId.EqualsIgnoreCase(AccountId))
			: accounts.FirstOrDefault(item => item.Status.EqualsIgnoreCase("active") &&
				item.AccountType.EqualsIgnoreCase(expectedType));

		if (account is null)
			throw new InvalidOperationException(!AccountId.IsEmpty()
				? $"Deriv account '{AccountId}' was not found."
				: $"No active Deriv {expectedType} account is available.");
		if (!account.Status.EqualsIgnoreCase("active"))
			throw new InvalidOperationException(
				$"Deriv account '{account.AccountId}' is not active.");
		if (!account.AccountType.EqualsIgnoreCase(expectedType))
			throw new InvalidOperationException(
				$"Deriv account '{account.AccountId}' is {account.AccountType}, " +
				$"but the adapter is configured for {expectedType}.");
		return account;
	}

	private ValueTask<string> GetWebSocketEndpointAsync(
		CancellationToken cancellationToken)
		=> _restClient is null
			? new(PublicWebSocketAddress.ThrowIfEmpty(nameof(PublicWebSocketAddress)))
			: _restClient.GetWebSocketUrlAsync(PortfolioName, cancellationToken);

	private async ValueTask LoadSymbolsAsync(CancellationToken cancellationToken)
	{
		var response = await WebSocketClient.RequestAsync(new JObject
		{
			["active_symbols"] = "full",
		}, cancellationToken);
		var symbols = response.GetArray<DerivActiveSymbol>("active_symbols")
			.Where(static symbol => !symbol.Symbol.IsEmpty())
			.ToArray();
		using (_sync.EnterScope())
		{
			_symbols.Clear();
			foreach (var symbol in symbols)
				_symbols[symbol.Symbol] = symbol;
		}
		this.AddInfoLog("Deriv loaded {0} active symbols.", symbols.Length);
	}

	private async ValueTask ProcessSubscriptionAsync(string nativeKey,
		DerivResponse response, CancellationToken cancellationToken)
	{
		if (!TryGetSubscription(nativeKey, out var subscription))
			return;

		switch (subscription.Kind)
		{
			case DerivSubscriptionKinds.Level1:
				await ProcessLevel1ResponseAsync(subscription, response, cancellationToken);
				break;
			case DerivSubscriptionKinds.Ticks:
				await ProcessTicksResponseAsync(subscription, response, cancellationToken);
				break;
			case DerivSubscriptionKinds.Candles:
				await ProcessCandleResponseAsync(subscription, response, cancellationToken);
				break;
			case DerivSubscriptionKinds.Balance:
				await ProcessBalanceResponseAsync(subscription, response, cancellationToken);
				break;
			case DerivSubscriptionKinds.Transaction:
				await ProcessTransactionResponseAsync(subscription, response, cancellationToken);
				break;
			case DerivSubscriptionKinds.ContractOrder:
			case DerivSubscriptionKinds.ContractOrderStatus:
			case DerivSubscriptionKinds.ContractPortfolio:
				await ProcessOpenContractResponseAsync(subscription, response,
					cancellationToken);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(subscription.Kind),
					subscription.Kind, null);
		}
	}

	private async ValueTask ProcessWebSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
			this.AddInfoLog("Deriv WebSocket restored with fresh authentication.");
		await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var webSocket = _webSocketClient;
		_webSocketClient = null;
		if (webSocket is not null)
		{
			webSocket.SubscriptionReceived -= ProcessSubscriptionAsync;
			webSocket.Error -= SendOutErrorAsync;
			webSocket.StateChanged -= ProcessWebSocketStateAsync;
			try
			{
				await webSocket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			webSocket.Dispose();
		}

		_restClient?.Dispose();
		_restClient = null;
		_account = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_symbols.Clear();
			_subscriptions.Clear();
			_orders.Clear();
			_seenTransactions.Clear();
			_portfolioSubscriptionId = 0;
			_orderStatusSubscriptionId = 0;
			_lastPing = default;
			_lastAccountRefresh = default;
		}
	}
}
