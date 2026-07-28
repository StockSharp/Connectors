namespace StockSharp.Samco;

public partial class SamcoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		_restClient = new(RestEndpoint, InstrumentEndpoint,
			Key, Secret, SessionToken)
		{
			Parent = this,
		};
		try
		{
			await _restClient.AuthenticateAsync(cancellationToken);
			SessionToken = _restClient.SessionToken.Secure();
			_portfolioName = _restClient.AccountId.IsEmpty("Samco");
			if (StreamingEnabled)
			{
				_socketClient = new(StreamingEndpoint,
					_restClient.SessionToken);
				_socketClient.MessageReceived +=
					OnSocketMessageAsync;
				_socketClient.Error += OnSocketErrorAsync;
				await _socketClient.ConnectAsync(cancellationToken);
			}
			_lastPolling = CurrentTime;
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		try
		{
			await base.DisconnectAsync(disconnectMsg,
				cancellationToken);
		}
		finally
		{
			await DisposeClientsAsync();
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync();
		using (_sync.EnterScope())
		{
			_instrumentDetails.Clear();
			_instrumentsByNative.Clear();
			_instrumentsBySymbol.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_feedSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderTransactions.Clear();
			_transactionOrders.Clear();
			_tradeIds.Clear();
			_lastTicks.Clear();
			_instrumentsLoaded = false;
		}
		_portfolioName = null;
		_lastPolling = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null &&
			CurrentTime - _lastPolling >= PollingInterval)
		{
			await PollCandlesAsync(cancellationToken);
			await PollOrdersAsync(cancellationToken);
			await PollPortfoliosAsync(cancellationToken);
			if (!StreamingEnabled)
				await PollMarketDataAsync(cancellationToken);
			_lastPolling = CurrentTime;
		}
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask SubscribeFeedAsync(
		SamcoInstrumentRef instrument,
		CancellationToken cancellationToken)
	{
		if (_socketClient is null)
			return;
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_feedSubscriptions.TryGetValue(instrument.SymbolCode,
				out var count);
			_feedSubscriptions[instrument.SymbolCode] = count + 1;
			subscribe = count == 0;
		}
		if (subscribe)
			await _socketClient.SubscribeAsync(instrument.SymbolCode,
				cancellationToken);
	}

	private async ValueTask UnsubscribeFeedAsync(
		SamcoInstrumentRef instrument,
		CancellationToken cancellationToken)
	{
		if (_socketClient is null)
			return;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (!_feedSubscriptions.TryGetValue(
				instrument.SymbolCode, out var count))
				return;
			if (count <= 1)
			{
				_feedSubscriptions.Remove(instrument.SymbolCode);
				unsubscribe = true;
			}
			else
				_feedSubscriptions[instrument.SymbolCode] = count - 1;
		}
		if (unsubscribe)
			await _socketClient.UnsubscribeAsync(
				instrument.SymbolCode, cancellationToken);
	}

	private async ValueTask OnSocketMessageAsync(string text,
		CancellationToken cancellationToken)
	{
		try
		{
			var feed = SamcoExtensions.ParseFeed(text);
			if (feed is not null)
				await ProcessFeedAsync(
					ResolveFeedInstrument(feed.SymbolCode), feed,
					cancellationToken);
		}
		catch (Exception error)
			when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask DisposeClientsAsync()
	{
		if (_socketClient is not null)
		{
			_socketClient.MessageReceived -= OnSocketMessageAsync;
			_socketClient.Error -= OnSocketErrorAsync;
			await _socketClient.DisposeAsync();
			_socketClient = null;
		}
		_restClient?.Dispose();
		_restClient = null;
	}
}
