namespace StockSharp.SSI;

public partial class SSIMessageAdapter
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
			_restClient = new(RestEndpoint, ClientId, Key, Secret,
				PrivateKey, Otp)
			{
				Parent = this,
			};
			await RestClient.AuthenticateAsync(cancellationToken);
			await ReconnectStreamAsync(cancellationToken);
			using (_sync.EnterScope())
			{
				_nextPrivatePoll = CurrentTime;
				_nextStreamReconnect = CurrentTime;
			}
			connectMsg.SessionId = "SSI FastConnect v3";
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
				(_streamClient is null ||
					!_streamClient.IsConnected) &&
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
		if (_streamClient?.IsConnected == true)
			return;
		await ReconnectStreamAsync(cancellationToken);
	}

	private async ValueTask ReconnectStreamAsync(
		CancellationToken cancellationToken)
	{
		await _streamGate.WaitAsync(cancellationToken);
		try
		{
			if (_streamClient?.IsConnected == true)
				return;
			var previous = _streamClient;
			_streamClient = null;
			if (previous is not null)
			{
				previous.MessageReceived -= OnStreamMessageAsync;
				previous.Error -= OnStreamErrorAsync;
				await previous.DisposeAsync();
			}
			await RestClient.EnsureTokenAsync(cancellationToken);
			var client = new SSIWebSocketClient(StreamingEndpoint,
				RestClient.TokenType, RestClient.AccessToken);
			client.MessageReceived += OnStreamMessageAsync;
			client.Error += OnStreamErrorAsync;
			try
			{
				await client.ConnectAsync(cancellationToken);
				(string Channel, string[] Topics)[] groups;
				using (_sync.EnterScope())
					groups =
					[
						.. _streamTopics.Keys
							.GroupBy(TopicChannel)
							.Select(static group =>
								(group.Key, group.ToArray()))
					];
				foreach (var group in groups)
					await client.SubscribeAsync(group.Channel,
						group.Topics, cancellationToken);
				_streamClient = client;
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
		var wasConnected = _streamClient?.IsConnected == true;
		var first = false;
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
				await _streamClient.SubscribeAsync(
					TopicChannel(topic), [topic], cancellationToken);
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
		if (last && _streamClient?.IsConnected == true)
			await _streamClient.UnsubscribeAsync(TopicChannel(topic),
				[topic], cancellationToken);
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
		SSIWebSocketClient stream;
		SSIRestClient rest;
		using (_sync.EnterScope())
		{
			stream = _streamClient;
			_streamClient = null;
			rest = _restClient;
			_restClient = null;
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_streamTopics.Clear();
			_securityBoards.Clear();
			_securityTypes.Clear();
			_orderTransactions.Clear();
			_matchIds.Clear();
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
