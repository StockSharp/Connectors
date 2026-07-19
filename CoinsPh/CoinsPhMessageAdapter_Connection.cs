namespace StockSharp.CoinsPh;

public partial class CoinsPhMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _publicSocketClient is not null ||
			_privateSocketClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() != Secret.IsEmpty())
			throw new InvalidOperationException(
				"Coins.ph API key and secret must be configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret)
			{
				Parent = this,
			};
			await RestClient.SyncTimeAsync(cancellationToken);
			var exchangeInfo = await RestClient.GetExchangeInfoAsync(
				cancellationToken);
			if (exchangeInfo?.Symbols is not { Length: > 0 })
				throw new InvalidDataException(
					"Coins.ph returned no market definitions.");
			RegisterMarkets(exchangeInfo.Symbols);

			_publicSocketClient = new(WebSocketEndpoint,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_publicSocketClient.TradeReceived += OnSocketTradeAsync;
			_publicSocketClient.TickerReceived += OnSocketTickerAsync;
			_publicSocketClient.DepthReceived += OnSocketDepthAsync;
			_publicSocketClient.KlineReceived += OnSocketKlineAsync;
			_publicSocketClient.Error += OnSocketErrorAsync;
			_publicSocketClient.StateChanged += OnPublicSocketStateAsync;
			await _publicSocketClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				_privateSocketClient = new(WebSocketEndpoint, RestClient,
					ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				_privateSocketClient.MessageReceived += OnPrivateMessageAsync;
				_privateSocketClient.Error += OnSocketErrorAsync;
				_privateSocketClient.StateChanged += OnPrivateSocketStateAsync;
				await _privateSocketClient.ConnectAsync(cancellationToken);
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

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnPublicSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;
		await RefreshPrivateSnapshotsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Restored,
			cancellationToken);
	}

	private async ValueTask OnPrivateSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;
		await RefreshPrivateSnapshotsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Restored,
			cancellationToken);
	}

	private async ValueTask RefreshPrivateSnapshotsAsync(
		CancellationToken cancellationToken)
	{
		if (!RestClient.IsCredentialsAvailable)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var subscriptionId in subscriptions)
			await SendPortfolioSnapshotAsync(subscriptionId, cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var privateClient = _privateSocketClient;
		_privateSocketClient = null;
		if (privateClient is not null)
		{
			try
			{
				await privateClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				privateClient.Dispose();
			}
		}

		var publicClient = _publicSocketClient;
		_publicSocketClient = null;
		if (publicClient is not null)
		{
			try
			{
				await publicClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				publicClient.Dispose();
			}
		}

		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void DisposeClients()
	{
		_privateSocketClient?.Dispose();
		_privateSocketClient = null;
		_publicSocketClient?.Dispose();
		_publicSocketClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
