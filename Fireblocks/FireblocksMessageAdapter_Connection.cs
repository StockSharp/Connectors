namespace StockSharp.Fireblocks;

public partial class FireblocksMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			ApiEndpoint = ApiEndpoint.NormalizeFireblocksEndpoint();
			_restClient = new(ApiEndpoint, ApiKey, PrivateKey)
			{
				Parent = this,
			};
			var accounts = await RestClient.GetVaultAccountsAsync(VaultPageSize,
				MaximumVaultAccounts, cancellationToken);
			UpdateVaults(accounts);
			connectMsg.SessionId = $"Fireblocks {Environment}, " +
				$"{accounts.Length} vault accounts";
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			DisposeClient();
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
		DisposeClient();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var isPollRequired = false;
		var currentTime = CurrentTime.EnsureUtc();
		using (_sync.EnterScope())
		{
			if (_restClient is not null &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_activeTransactions.Count > 0) &&
				currentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = currentTime + PollingInterval;
				isPollRequired = true;
			}
		}
		if (isPollRequired)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask<FireblocksVaultAccount[]> RefreshVaultsAsync(
		CancellationToken cancellationToken)
	{
		var accounts = await RestClient.GetVaultAccountsAsync(VaultPageSize,
			MaximumVaultAccounts, cancellationToken);
		UpdateVaults(accounts);
		return accounts;
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

	private void DisposeClient()
	{
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_vaults.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_localTransactionIds.Clear();
			_fireblocksTransactionIds.Clear();
			_activeTransactions.Clear();
			_balanceFingerprints.Clear();
			_transactionFingerprints.Clear();
			_nextPrivatePoll = default;
		}
	}
}
