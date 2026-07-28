namespace StockSharp.CoinPaprika;

public partial class CoinPaprikaMessageAdapter
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
		RestEndpoint = NormalizeEndpoint(RestEndpoint);
		QuoteCurrency =
			CoinPaprikaExtensions.NormalizeQuote(QuoteCurrency);
		ExchangeId = ExchangeId?.Trim();
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting, cancellationToken);
		try
		{
			_restClient = new(
				RestEndpoint, Token, RequestInterval)
			{
				Parent = this,
			};
			await RestClient.ValidateAsync(cancellationToken);
			await SendOutConnectionStateAsync(
				ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
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
			foreach (var pair in due)
			{
				try
				{
					var ticker = await RestClient.GetTickerAsync(
						pair.Value.Instrument,
						QuoteCurrency,
						cancellationToken);
					if (ticker is not null)
					{
						RememberInstruments([ticker]);
						await SendLevel1Async(
							pair.Value.Instrument,
							ticker,
							pair.Key,
							cancellationToken);
					}
					using (_sync.EnterScope())
						if (_level1Subscriptions.TryGetValue(
							pair.Key, out var current))
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
