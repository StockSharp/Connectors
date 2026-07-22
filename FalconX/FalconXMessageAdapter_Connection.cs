namespace StockSharp.FalconX;

public partial class FalconXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _marketSocket is not null ||
			_orderSocket is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_authenticator = new(Key?.UnSecure(), Secret, Passphrase);
			_restClient = new(ApiEndpoint, _authenticator)
			{
				Parent = this,
			};
			var account = await RestClient.GetAccountInfoAsync(cancellationToken) ??
				throw new InvalidDataException(
					"FalconX returned an empty account response.");
			var pairs = await RestClient.GetPairsAsync(cancellationToken) ?? [];
			foreach (var pair in pairs)
				AddPair(pair);
			if (pairs.Length == 0)
				throw new InvalidDataException(
					"FalconX returned no token pairs for this API key.");
			_marketSocket = new(PriceSocketEndpoint, _authenticator,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_marketSocket.PricesReceived += OnPricesReceivedAsync;
			_marketSocket.Error += OnSocketErrorAsync;
			_marketSocket.StateChanged += OnSocketStateAsync;
			_orderSocket = new(OrderSocketEndpoint, _authenticator,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_orderSocket.OrderReceived += OnOrderReceivedAsync;
			_orderSocket.Error += OnSocketErrorAsync;
			_orderSocket.StateChanged += OnSocketStateAsync;
			_portfolioName = "FalconX" +
				(account.SubaccountName.IsEmpty() ? string.Empty : ":" +
					account.SubaccountName.Trim());
			using (_sync.EnterScope())
				_nextPoll = DateTime.UtcNow + PollingInterval;
			connectMsg.SessionId = account.AccountName.IsEmpty()
				? _portfolioName
				: account.AccountName + " / " + _portfolioName;
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
		var isPollRequired = false;
		using (_sync.EnterScope())
		{
			if (_restClient is not null &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0) &&
				DateTime.UtcNow >= _nextPoll)
			{
				_nextPoll = DateTime.UtcNow + PollingInterval;
				isPollRequired = true;
			}
		}
		if (isPollRequired)
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
		if (state == ConnectionStates.Restored)
			SchedulePoll();
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
		var marketSocket = _marketSocket;
		var orderSocket = _orderSocket;
		var rest = _restClient;
		var authenticator = _authenticator;
		_marketSocket = null;
		_orderSocket = null;
		_restClient = null;
		_authenticator = null;
		var errors = new List<Exception>();
		if (marketSocket is not null)
		{
			marketSocket.PricesReceived -= OnPricesReceivedAsync;
			marketSocket.Error -= OnSocketErrorAsync;
			marketSocket.StateChanged -= OnSocketStateAsync;
			try
			{
				await marketSocket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
			try
			{
				marketSocket.Dispose();
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
		}
		if (orderSocket is not null)
		{
			orderSocket.OrderReceived -= OnOrderReceivedAsync;
			orderSocket.Error -= OnSocketErrorAsync;
			orderSocket.StateChanged -= OnSocketStateAsync;
			try
			{
				await orderSocket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
			try
			{
				orderSocket.Dispose();
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
		try
		{
			authenticator?.Dispose();
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
				"One or more FalconX clients could not be disposed.", errors);
	}
}
