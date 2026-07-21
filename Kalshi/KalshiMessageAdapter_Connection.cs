namespace StockSharp.Kalshi;

public partial class KalshiMessageAdapter
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
			_authenticator = new(ApiKey, PrivateKey);
			_restClient = new(RestEndpoint, _authenticator)
			{
				Parent = this,
			};
			foreach (var market in await RestClient.GetMarketsAsync(1,
				cancellationToken))
				AddMarket(market);
			_socketClient = new(SocketEndpoint, _authenticator,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_socketClient.EventReceived += OnSocketEventAsync;
			_socketClient.Error += OnSocketErrorAsync;
			_socketClient.StateChanged += OnSocketStateAsync;
			await SocketClient.ConnectAsync(cancellationToken);
			_portfolioName = _authenticator.IsAvailable
				? Subaccount == 0 ? "Kalshi" : "Kalshi_" +
					Subaccount.ToString(CultureInfo.InvariantCulture)
				: null;
			using (_sync.EnterScope())
				_nextPrivatePoll = CurrentTime + PollingInterval;
			connectMsg.SessionId = (IsDemo ? "Kalshi Demo" : "Kalshi") +
				(_portfolioName.IsEmpty() ? " Public" : " " + _portfolioName);
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
		var isPrivatePoll = false;
		using (_sync.EnterScope())
		{
			if (_restClient is not null &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				isPrivatePoll = true;
			}
		}
		if (isPrivatePoll)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
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
		var authenticator = _authenticator;
		_socketClient = null;
		_restClient = null;
		_authenticator = null;
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
		authenticator?.Dispose();
		ClearState();
	}
}
