namespace StockSharp.TraderMade;

public partial class TraderMadeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _streamClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (RestKey.IsEmpty() && StreamingKey.IsEmpty())
			throw new InvalidOperationException(
				"Configure a TraderMade REST or streaming API key.");
		try
		{
			if (!RestKey.IsEmpty())
				_restClient = new(RestEndpoint, RestKey,
					RequestInterval)
				{
					Parent = this,
				};
			if (!StreamingKey.IsEmpty())
			{
				_streamClient = new(StreamingEndpoint,
					StreamingKey, EnableLadder)
				{
					Parent = this,
				};
				_streamClient.QuoteReceived +=
					OnStreamQuoteAsync;
				await _streamClient.ConnectAsync(
					cancellationToken);
			}
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			_streamClient?.Dispose();
			_streamClient = null;
			_restClient?.Dispose();
			_restClient = null;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is null && _streamClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
		if (_streamClient is not null)
		{
			await _streamClient.DisconnectAsync(cancellationToken);
			_streamClient.Dispose();
			_streamClient = null;
		}
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
		await base.DisconnectAsync(disconnectMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(
		ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		if (_streamClient is not null)
		{
			await _streamClient.DisconnectAsync(cancellationToken);
			_streamClient.Dispose();
			_streamClient = null;
		}
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}
}
