namespace StockSharp.CoinGlass;

public partial class CoinGlassMessageAdapter
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
		Exchange = NormalizeRequired(
			Exchange, nameof(Exchange));
		Symbol = NormalizeSymbol(Symbol);
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
			await RestClient.ValidateAsync(
				MarketType,
				Exchange,
				Symbol,
				cancellationToken);
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
					var snapshot =
						await RestClient.GetSnapshotAsync(
							pair.Value.Instrument,
							cancellationToken);
					if (snapshot is not null)
						await SendLevel1Async(
							pair.Value.Instrument,
							snapshot,
							pair.Key,
							cancellationToken);
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
