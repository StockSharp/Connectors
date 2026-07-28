namespace StockSharp.IIFL;

public partial class IIFLMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);

		_restClient = new(RestEndpoint, ClientId, AuthorizationCode,
			Secret, SessionToken)
		{
			Parent = this,
		};
		try
		{
			await _restClient.AuthenticateAsync(cancellationToken);
			SessionToken = _restClient.AccessToken.Secure();
			_resolvedPortfolio = PortfolioName
				.IsEmpty(_restClient.UserId)
				.IsEmpty("IIFL");

			if (StreamingEnabled)
			{
				await _restClient.ValidateTokenAsync(
					TokenValidationEndpoint, cancellationToken);
				_mqttClient = new(BridgeHost, BridgePort,
					_restClient.AccessToken);
				_mqttClient.MessageReceived += OnMqttMessageAsync;
				_mqttClient.Error += OnMqttErrorAsync;
				await _mqttClient.ConnectAsync(cancellationToken);
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
			await base.DisconnectAsync(disconnectMsg, cancellationToken);
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
			_instrumentsByNative.Clear();
			_instrumentDetails.Clear();
			_instrumentsBySymbol.Clear();
			_loadedExchanges.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_feedTopics.Clear();
			_openInterestTopics.Clear();
			_orderSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderTransactions.Clear();
			_transactionOrders.Clear();
			_lastTicks.Clear();
			_tradeIds.Clear();
			_privateStreamSubscribed = false;
		}
		_resolvedPortfolio = null;
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
		IIFLInstrumentRef instrument, bool openInterest,
		CancellationToken cancellationToken)
	{
		if (_mqttClient is null)
			return;
		var subscribeFeed = false;
		var subscribeOpenInterest = false;
		using (_sync.EnterScope())
		{
			_feedTopics.TryGetValue(instrument.Topic, out var feedCount);
			_feedTopics[instrument.Topic] = feedCount + 1;
			subscribeFeed = feedCount == 0;
			if (openInterest)
			{
				_openInterestTopics.TryGetValue(instrument.Topic,
					out var interestCount);
				_openInterestTopics[instrument.Topic] =
					interestCount + 1;
				subscribeOpenInterest = interestCount == 0;
			}
		}
		try
		{
			if (subscribeFeed)
				await _mqttClient.SubscribeMarketFeedAsync(
					instrument.Topic, cancellationToken);
			if (subscribeOpenInterest)
				await _mqttClient.SubscribeOpenInterestAsync(
					instrument.Topic, cancellationToken);
		}
		catch
		{
			await UnsubscribeFeedAsync(instrument, openInterest,
				CancellationToken.None);
			throw;
		}
	}

	private async ValueTask UnsubscribeFeedAsync(
		IIFLInstrumentRef instrument, bool openInterest,
		CancellationToken cancellationToken)
	{
		if (_mqttClient is null)
			return;
		var unsubscribeFeed = false;
		var unsubscribeOpenInterest = false;
		using (_sync.EnterScope())
		{
			if (_feedTopics.TryGetValue(instrument.Topic, out var feed))
			{
				if (feed <= 1)
				{
					_feedTopics.Remove(instrument.Topic);
					unsubscribeFeed = true;
				}
				else
					_feedTopics[instrument.Topic] = feed - 1;
			}
			if (openInterest &&
				_openInterestTopics.TryGetValue(instrument.Topic,
					out var interest))
			{
				if (interest <= 1)
				{
					_openInterestTopics.Remove(instrument.Topic);
					unsubscribeOpenInterest = true;
				}
				else
					_openInterestTopics[instrument.Topic] =
						interest - 1;
			}
		}
		if (unsubscribeFeed)
			await _mqttClient.UnsubscribeMarketFeedAsync(
				instrument.Topic, cancellationToken);
		if (unsubscribeOpenInterest)
			await _mqttClient.UnsubscribeOpenInterestAsync(
				instrument.Topic, cancellationToken);
	}

	private async ValueTask OnMqttMessageAsync(string topic,
		byte[] payload, CancellationToken cancellationToken)
	{
		try
		{
			if (topic.StartsWith(IIFLMqttClient.MarketFeedPrefix,
				StringComparison.Ordinal))
			{
				var nativeTopic =
					topic[IIFLMqttClient.MarketFeedPrefix.Length..];
				await ProcessMarketFeedAsync(
					ResolveStreamInstrument(nativeTopic),
					IIFLExtensions.ParseMarketFeed(payload),
					cancellationToken);
				return;
			}
			if (topic.StartsWith(IIFLMqttClient.OpenInterestPrefix,
				StringComparison.Ordinal))
			{
				var nativeTopic =
					topic[IIFLMqttClient.OpenInterestPrefix.Length..];
				await ProcessOpenInterestAsync(
					ResolveStreamInstrument(nativeTopic),
					IIFLExtensions.ParseOpenInterest(payload),
					cancellationToken);
				return;
			}
			if (topic.StartsWith(IIFLMqttClient.OrderPrefix,
				StringComparison.Ordinal))
			{
				await ProcessOrderStreamAsync(
					Encoding.UTF8.GetString(payload),
					cancellationToken);
				return;
			}
			if (topic.StartsWith(IIFLMqttClient.TradePrefix,
				StringComparison.Ordinal))
				await ProcessTradeStreamAsync(
					Encoding.UTF8.GetString(payload),
					cancellationToken);
		}
		catch (Exception error)
			when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private ValueTask OnMqttErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask DisposeClientsAsync()
	{
		if (_mqttClient is not null)
		{
			_mqttClient.MessageReceived -= OnMqttMessageAsync;
			_mqttClient.Error -= OnMqttErrorAsync;
			await _mqttClient.DisposeAsync();
			_mqttClient = null;
		}
		_restClient?.Dispose();
		_restClient = null;
	}
}
