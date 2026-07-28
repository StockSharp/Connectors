namespace StockSharp.MStock;

public partial class MStockMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		_restClient = new(RestEndpoint, Key, ClientCode, Password,
			Otp, UseTotp, RefreshToken, AccessToken)
		{
			Parent = this,
		};
		try
		{
			await _restClient.AuthenticateAsync(cancellationToken);
			AccessToken = _restClient.AccessToken.Secure();
			RefreshToken = _restClient.RefreshToken.Secure();
			_portfolioName = ClientCode.IsEmpty("m.Stock");
			if (StreamingEnabled)
			{
				_socketClient = new(StreamingEndpoint,
					Key.UnSecure(), _restClient.AccessToken);
				_socketClient.BinaryReceived +=
					OnBinaryReceivedAsync;
				_socketClient.TextReceived += OnTextReceivedAsync;
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
		MStockInstrumentRef instrument,
		CancellationToken cancellationToken)
	{
		if (_socketClient is null)
			return;
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_feedSubscriptions.TryGetValue(instrument.Key,
				out var count);
			_feedSubscriptions[instrument.Key] = count + 1;
			subscribe = count == 0;
		}
		if (!subscribe)
			return;
		try
		{
			await _socketClient.SubscribeAsync(instrument, 3,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_feedSubscriptions.Remove(instrument.Key);
			throw;
		}
	}

	private async ValueTask UnsubscribeFeedAsync(
		MStockInstrumentRef instrument,
		CancellationToken cancellationToken)
	{
		if (_socketClient is null)
			return;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (!_feedSubscriptions.TryGetValue(instrument.Key,
				out var count))
				return;
			if (count <= 1)
			{
				_feedSubscriptions.Remove(instrument.Key);
				unsubscribe = true;
			}
			else
				_feedSubscriptions[instrument.Key] = count - 1;
		}
		if (unsubscribe)
			await _socketClient.UnsubscribeAsync(instrument,
				cancellationToken);
	}

	private async ValueTask OnBinaryReceivedAsync(byte[] payload,
		CancellationToken cancellationToken)
	{
		try
		{
			foreach (var feed in MStockExtensions.ParseMarketData(
				payload))
				await ProcessFeedAsync(
					ResolveFeedInstrument(feed), feed,
					cancellationToken);
		}
		catch (Exception error)
			when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask OnTextReceivedAsync(string text,
		CancellationToken cancellationToken)
	{
		try
		{
			JObject value;
			try
			{
				value = JObject.Parse(text);
			}
			catch (JsonException)
			{
				return;
			}
			var type = value.String("order_status");
			var data = value.Get("orderData") as JObject;
			if (type.EqualsIgnoreCase("order") && data is not null)
				await ProcessOrderStreamAsync(data,
					cancellationToken);
			else if (type.EqualsIgnoreCase("trade") &&
				data is not null)
				await ProcessTradeStreamAsync(data,
					cancellationToken);
			else if (value.String("type")
				.EqualsIgnoreCase("error"))
				throw new InvalidOperationException(
					value.Get("data")?.ToString()
						.IsEmpty("m.Stock streaming error."));
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
			_socketClient.BinaryReceived -=
				OnBinaryReceivedAsync;
			_socketClient.TextReceived -= OnTextReceivedAsync;
			_socketClient.Error -= OnSocketErrorAsync;
			await _socketClient.DisposeAsync();
			_socketClient = null;
		}
		_restClient?.Dispose();
		_restClient = null;
	}
}
