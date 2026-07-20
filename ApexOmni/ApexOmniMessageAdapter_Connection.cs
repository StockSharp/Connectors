namespace StockSharp.ApexOmni;

public partial class ApexOmniMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		MarketDepth = ValidateDepth(MarketDepth);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret, Passphrase)
			{
				Parent = this,
			};
			await RefreshInstrumentsAsync(cancellationToken);
			await SynchronizeTimeAsync(cancellationToken);

			if (!Seeds.IsEmpty())
				_signer = new(Seeds.UnSecure());
			if (RestClient.IsAuthenticated)
			{
				_account = await RestClient.GetAccountAsync(cancellationToken);
				ValidateAccount(_account);
			}

			_publicSocket = CreatePublicSocket();
			await _publicSocket.ConnectAsync(cancellationToken);

			if (_account is not null)
			{
				_privateSocket = CreatePrivateSocket();
				await _privateSocket.ConnectAsync(cancellationToken);
				await _privateSocket.SubscribeAsync("ws_zk_accounts_v3",
					cancellationToken);
			}

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
		_ = timeMsg;
		if (_restClient is not null && DateTime.UtcNow >= _nextServerTimeSync)
			await SynchronizeTimeAsync(cancellationToken);
		if (_publicSocket is not null)
			await _publicSocket.SendPingAsync(cancellationToken);
		if (_privateSocket is not null)
			await _privateSocket.SendPingAsync(cancellationToken);
	}

	private async ValueTask RefreshInstrumentsAsync(
		CancellationToken cancellationToken)
	{
		var configuration = await RestClient.GetConfigurationAsync(
			cancellationToken);
		var contracts = configuration?.Contracts?.GetInstruments() ?? [];
		var assets = configuration?.Contracts?.Assets ?? [];
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			_publicInstruments.Clear();
			_assets.Clear();
			foreach (var asset in assets)
				if (asset?.Token.IsEmpty() == false && asset.Decimals is >= 0 and <= 28)
					_assets[asset.Token] = asset;
			foreach (var instrument in contracts)
			{
				if (instrument?.Symbol.IsEmpty() != false ||
					instrument.CrossSymbolName.IsEmpty() ||
					instrument.BaseTokenId.IsEmpty() ||
					instrument.SettleAssetId.IsEmpty() ||
					instrument.TickSize.ToDecimal() is not > 0 ||
					instrument.StepSize.ToDecimal() is not > 0 ||
					!instrument.IsDisplayEnabled)
					continue;
				_instruments[instrument.Symbol] = instrument;
				_publicInstruments[instrument.CrossSymbolName] = instrument;
			}
			if (_instruments.Count == 0)
				throw new InvalidDataException(
					"ApeX Omni returned no active contract instruments.");
		}
	}

	private async ValueTask SynchronizeTimeAsync(
		CancellationToken cancellationToken)
	{
		var before = DateTime.UtcNow;
		var server = await RestClient.GetServerTimeAsync(cancellationToken);
		var after = DateTime.UtcNow;
		var midpoint = before.AddTicks((after - before).Ticks / 2);
		_serverTimeOffset = server - midpoint;
		RestClient.SetServerTime(server, midpoint);
		_nextServerTimeSync = after.AddMinutes(5);
	}

	private ApexOmniWebSocketClient CreatePublicSocket()
	{
		var client = new ApexOmniWebSocketClient(WebSocketEndpoint, null,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.TickerReceived += OnTickerAsync;
		client.BookReceived += OnBookAsync;
		client.TradeReceived += OnPublicTradeAsync;
		client.CandleReceived += OnCandleAsync;
		client.Error += OnWebSocketErrorAsync;
		return client;
	}

	private ApexOmniWebSocketClient CreatePrivateSocket()
	{
		var client = new ApexOmniWebSocketClient(WebSocketEndpoint, RestClient,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.PrivateReceived += OnPrivateFeedAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPrivateSocketStateChangedAsync;
		return client;
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnPrivateSocketStateChangedAsync(
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state != ConnectionStates.Restored)
			return;
		_account = await RestClient.GetAccountAsync(cancellationToken);
		ValidateAccount(_account);
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null,
				cancellationToken);
	}

	private static void ValidateAccount(ApexOmniAccount account)
	{
		if (account?.Id.IsEmpty() != false || account.ContractAccount is null ||
			account.ContractAccount.MakerFeeRate.ToDecimal() is not >= 0 ||
			account.ContractAccount.TakerFeeRate.ToDecimal() is not >= 0)
			throw new InvalidDataException(
				"ApeX Omni returned an incomplete trading account.");
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var publicSocket = _publicSocket;
		var privateSocket = _privateSocket;
		_publicSocket = null;
		_privateSocket = null;
		foreach (var client in new[] { publicSocket, privateSocket })
		{
			if (client is null)
				continue;
			try
			{
				await client.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			client.Dispose();
		}
		_restClient?.Dispose();
		_restClient = null;
		_signer?.Dispose();
		_signer = null;
		_account = null;
		ClearState();
	}

	private void DisposeClients()
	{
		_publicSocket?.Dispose();
		_privateSocket?.Dispose();
		_restClient?.Dispose();
		_signer?.Dispose();
		_publicSocket = null;
		_privateSocket = null;
		_restClient = null;
		_signer = null;
		_account = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			_publicInstruments.Clear();
			_assets.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_topicReferences.Clear();
			_fillFingerprints.Clear();
			_fillFingerprintOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_serverTimeOffset = default;
		_nextServerTimeSync = default;
	}
}
