namespace StockSharp.Copper;

public partial class CopperMessageAdapter
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
			ApiEndpoint = ApiEndpoint.NormalizeCopperEndpoint();
			_restClient = new(ApiEndpoint, ApiKey, ApiSecret)
			{
				Parent = this,
			};
			var portfolios = await RestClient.GetPortfoliosAsync(PageSize,
				MaximumItems, cancellationToken);
			var clearLoop = await RestClient.TryGetClearLoopPortfoliosAsync(
				cancellationToken);
			UpdatePortfolios(portfolios, clearLoop);
			var currencies = await RestClient.GetCurrenciesAsync(cancellationToken);
			UpdateCurrencies(currencies);
			connectMsg.SessionId = $"Copper {Environment}, {portfolios.Length} " +
				$"portfolios, {clearLoop.Length} ClearLoop accounts";
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
					_activeOrders.Count > 0) &&
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

	private async ValueTask<PortfolioReference[]> RefreshPortfoliosAsync(
		CancellationToken cancellationToken)
	{
		var portfolios = await RestClient.GetPortfoliosAsync(PageSize,
			MaximumItems, cancellationToken);
		var clearLoop = await RestClient.TryGetClearLoopPortfoliosAsync(
			cancellationToken);
		UpdatePortfolios(portfolios, clearLoop);
		return GetPortfolios();
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
			_portfolios.Clear();
			_currencies.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_localTransactionIds.Clear();
			_copperOrderIds.Clear();
			_activeOrders.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
			_nextPrivatePoll = default;
		}
	}
}
