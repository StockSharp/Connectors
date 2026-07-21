namespace StockSharp.BitGo;

public partial class BitGoMessageAdapter
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
			_restClient = new(ApiEndpoint, Token)
			{
				Parent = this,
			};
			var accounts = (await RestClient.GetAccountsAsync(cancellationToken))
				.Where(static account => account?.Id.IsEmpty() == false)
				.ToArray();
			_selectedAccount = SelectAccount(accounts);
			var products = await RestClient.GetProductsAsync(AccountId,
				cancellationToken);
			foreach (var product in products)
				AddProduct(product);
			if (GetProducts().Length == 0)
				throw new InvalidDataException(
					"BitGo returned no Prime trading products for the selected account.");
			_portfolioName = "BitGo:" + (_selectedAccount.Name.IsEmpty()
				? _selectedAccount.Id
				: _selectedAccount.Name.Trim());
			_socketClient = new(SocketEndpoint, Token, AccountId,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_socketClient.BookReceived += OnBookReceivedAsync;
			_socketClient.OrderReceived += OnOrderReceivedAsync;
			_socketClient.Error += OnSocketErrorAsync;
			_socketClient.StateChanged += OnSocketStateAsync;
			await SocketClient.EnsureConnectedAsync(cancellationToken);
			await SocketClient.SubscribeOrdersAsync(cancellationToken);
			using (_sync.EnterScope())
				_nextPoll = DateTime.UtcNow + PollingInterval;
			connectMsg.SessionId = _selectedAccount.Id;
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

	private BitGoAccount SelectAccount(BitGoAccount[] accounts)
	{
		if (accounts.Length == 0)
			throw new InvalidDataException(
				"BitGo returned no Prime trading accounts for this token.");
		var requested = Account?.Trim();
		if (!requested.IsEmpty())
		{
			var matches = accounts.Where(account =>
				requested.Equals(account.Id, StringComparison.OrdinalIgnoreCase) ||
				requested.Equals(account.Name, StringComparison.OrdinalIgnoreCase))
				.ToArray();
			return matches.Length switch
			{
				1 => matches[0],
				0 => throw new InvalidOperationException(
					"BitGo account '" + requested + "' is not available to this token."),
				_ => throw new InvalidOperationException(
					"BitGo account name '" + requested +
					"' is ambiguous; configure its account ID."),
			};
		}
		if (accounts.Length == 1)
			return accounts[0];
		throw new InvalidOperationException(
			"The BitGo token has access to multiple Prime accounts. Configure " +
			"Account with an ID or exact name. Available accounts: " +
			string.Join(", ", accounts.Select(static account =>
				account.Name.IsEmpty() ? account.Id : account.Name + " (" +
					account.Id + ")")) + ".");
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
		var socket = _socketClient;
		var rest = _restClient;
		_socketClient = null;
		_restClient = null;
		var errors = new List<Exception>();
		if (socket is not null)
		{
			socket.BookReceived -= OnBookReceivedAsync;
			socket.OrderReceived -= OnOrderReceivedAsync;
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
				"One or more BitGo clients could not be disposed.", errors);
	}
}
