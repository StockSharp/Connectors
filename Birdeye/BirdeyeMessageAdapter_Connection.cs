namespace StockSharp.Birdeye;

public partial class BirdeyeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		RestEndpoint = NormalizeEndpoint(
			RestEndpoint, _defaultRestEndpoint);
		WebSocketEndpoint = NormalizeEndpoint(
			WebSocketEndpoint, _defaultWebSocketEndpoint);
		WebSocketOrigin = NormalizeEndpoint(
			WebSocketOrigin, _defaultWebSocketOrigin);
		Chain = BirdeyeExtensions.NormalizeChain(Chain);
		TokenAddress = TokenAddress?.Trim();
		if (!TokenAddress.IsEmpty() &&
			!BirdeyeExtensions.IsSafeAddress(TokenAddress))
			throw new InvalidOperationException(
				"Birdeye token address contains unsupported characters.");
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting, cancellationToken);
		try
		{
			_restClient = new(
				RestEndpoint,
				Token,
				Chain,
				RequestInterval)
			{
				Parent = this,
			};
			BirdeyeToken[] validation;
			if (TokenAddress.IsEmpty())
				validation = await RestClient.GetTokensAsync(
					MinimumLiquidity,
					1,
					cancellationToken);
			else
				validation =
					[
						await RestClient.GetOverviewAsync(
							TokenAddress,
							cancellationToken),
					];
			RememberTokens(validation);
			if (StreamingEnabled)
			{
				_webSocketClient = new(
					WebSocketEndpoint,
					WebSocketOrigin,
					Token,
					Chain)
				{
					Parent = this,
				};
				_webSocketClient.CandleReceived +=
					OnStreamCandleAsync;
				await _webSocketClient.ConnectAsync(
					cancellationToken);
			}
			await SendOutConnectionStateAsync(
				ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			if (_webSocketClient is not null)
			{
				_webSocketClient.CandleReceived -=
					OnStreamCandleAsync;
				_webSocketClient.Dispose();
				_webSocketClient = null;
			}
			_restClient?.Dispose();
			_restClient = null;
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
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnecting, cancellationToken);
		if (_webSocketClient is not null)
		{
			_webSocketClient.CandleReceived -=
				OnStreamCandleAsync;
			await _webSocketClient.DisconnectAsync(
				cancellationToken);
			_webSocketClient.Dispose();
			_webSocketClient = null;
		}
		_restClient.Dispose();
		_restClient = null;
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnected, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(
		ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		if (_webSocketClient is not null)
		{
			_webSocketClient.CandleReceived -=
				OnStreamCandleAsync;
			await _webSocketClient.DisconnectAsync(
				cancellationToken);
			_webSocketClient.Dispose();
			_webSocketClient = null;
		}
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(
		TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		if (StreamingEnabled)
			return;
		KeyValuePair<long, Level1Subscription>[] due;
		using (_sync.EnterScope())
			due = [.. _level1Subscriptions.Where(pair =>
				CurrentTime - pair.Value.LastUpdate >=
					PollingInterval)];
		if (due.Length == 0 ||
			!await _pollSync.WaitAsync(0, cancellationToken))
			return;
		try
		{
			foreach (var item in due)
			{
				try
				{
					var snapshot =
						await RestClient.GetOverviewAsync(
							item.Value.Token.Address,
							cancellationToken);
					if (snapshot is not null)
						await SendLevel1Async(
							item.Value.Token,
							snapshot,
							item.Key,
							cancellationToken);
					using (_sync.EnterScope())
						if (_level1Subscriptions.TryGetValue(
							item.Key, out var current))
							current.LastUpdate = CurrentTime;
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					await SendOutErrorAsync(
						error, cancellationToken);
				}
			}
		}
		finally
		{
			_pollSync.Release();
		}
	}
}
