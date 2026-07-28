namespace StockSharp.Settrade;

public partial class SettradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.KeyNotSpecified);
		if (Secret.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.SecretNotSpecified);
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(EffectiveRestEndpoint,
				EffectiveMarketEndpoint, Key.UnSecure(), Secret,
				AppCode, EffectiveBrokerId, LoginParameters)
			{
				Parent = this,
			};
			await RestClient.LoginAsync(cancellationToken);
			using (_sync.EnterScope())
			{
				_nextPrivatePoll = CurrentTime;
				_nextStreamReconnect = CurrentTime;
			}
			connectMsg.SessionId =
				$"Settrade {(IsDemo ? "sandbox" : "production")} " +
				$"{EffectiveBrokerId}";
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync();
			await SendOutConnectionStateAsync(
				ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnecting, cancellationToken);
		await DisposeClientsAsync();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var pollPrivate = false;
		var reconnectStream = false;
		using (_sync.EnterScope())
		{
			pollPrivate = _restClient is not null &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0) &&
				CurrentTime >= _nextPrivatePoll;
			if (pollPrivate)
				_nextPrivatePoll = CurrentTime + PollingInterval;
			reconnectStream = _restClient is not null &&
				_streamTopics.Count > 0 &&
				(_mqttClient is null || !_mqttClient.IsConnected) &&
				CurrentTime >= _nextStreamReconnect;
			if (reconnectStream)
				_nextStreamReconnect =
					CurrentTime + TimeSpan.FromSeconds(15);
		}
		if (_restClient is not null)
			await RunSafelyAsync(RestClient.EnsureTokenAsync,
				cancellationToken);
		if (reconnectStream)
			await RunSafelyAsync(ReconnectStreamAsync,
				cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		_ = timeMsg;
	}

	private async ValueTask EnsureStreamAsync(
		CancellationToken cancellationToken)
	{
		if (_mqttClient?.IsConnected == true)
			return;
		await ReconnectStreamAsync(cancellationToken);
	}

	private async ValueTask ReconnectStreamAsync(
		CancellationToken cancellationToken)
	{
		await _streamGate.WaitAsync(cancellationToken);
		try
		{
			if (_mqttClient?.IsConnected == true)
				return;
			var previous = _mqttClient;
			_mqttClient = null;
			if (previous is not null)
			{
				previous.MessageReceived -= OnStreamMessageAsync;
				previous.Error -= OnStreamErrorAsync;
				await previous.DisposeAsync();
			}
			var dispatcher = await RestClient.GetDispatcherAsync(
				cancellationToken);
			var client = new SettradeMqttClient(dispatcher.Host,
				$"/api/dispatcher/v3/{EffectiveBrokerId}/mqtt",
				RestClient.TokenType, dispatcher.Token);
			client.MessageReceived += OnStreamMessageAsync;
			client.Error += OnStreamErrorAsync;
			try
			{
				await client.ConnectAsync(cancellationToken);
				string[] topics;
				using (_sync.EnterScope())
					topics = _streamTopics.Keys.ToArray();
				foreach (var topic in topics)
					await client.SubscribeAsync(topic,
						cancellationToken);
				_mqttClient = client;
			}
			catch
			{
				client.MessageReceived -= OnStreamMessageAsync;
				client.Error -= OnStreamErrorAsync;
				await client.DisposeAsync();
				throw;
			}
		}
		finally
		{
			_streamGate.Release();
		}
	}

	private async ValueTask SubscribeTopicAsync(string topic,
		CancellationToken cancellationToken)
	{
		var first = false;
		var wasConnected = _mqttClient?.IsConnected == true;
		using (_sync.EnterScope())
		{
			_streamTopics.TryGetValue(topic, out var count);
			_streamTopics[topic] = count + 1;
			first = count == 0;
		}
		try
		{
			await EnsureStreamAsync(cancellationToken);
			if (first && wasConnected)
				await _mqttClient.SubscribeAsync(topic,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (_streamTopics.TryGetValue(topic, out var count))
				{
					if (count <= 1)
						_streamTopics.Remove(topic);
					else
						_streamTopics[topic] = count - 1;
				}
			}
			throw;
		}
	}

	private async ValueTask UnsubscribeTopicAsync(string topic,
		CancellationToken cancellationToken)
	{
		var last = false;
		using (_sync.EnterScope())
		{
			if (!_streamTopics.TryGetValue(topic, out var count))
				return;
			if (count <= 1)
			{
				_streamTopics.Remove(topic);
				last = true;
			}
			else
				_streamTopics[topic] = count - 1;
		}
		if (last && _mqttClient?.IsConnected == true)
			await _mqttClient.UnsubscribeAsync(topic,
				cancellationToken);
	}

	private async ValueTask OnStreamErrorAsync(Exception error,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			_nextStreamReconnect = CurrentTime;
		await SendOutErrorAsync(error, cancellationToken);
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
			when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask DisposeClientsAsync()
	{
		SettradeMqttClient stream;
		SettradeRestClient rest;
		using (_sync.EnterScope())
		{
			stream = _mqttClient;
			_mqttClient = null;
			rest = _restClient;
			_restClient = null;
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_streamTopics.Clear();
			_orderTransactions.Clear();
			_tradeIds.Clear();
		}
		if (stream is not null)
		{
			stream.MessageReceived -= OnStreamMessageAsync;
			stream.Error -= OnStreamErrorAsync;
			await stream.DisposeAsync();
		}
		rest?.Dispose();
	}
}
