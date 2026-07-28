namespace StockSharp.DexScreener;

public partial class DexScreenerMessageAdapter
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
		ChainId = ChainId?.Trim();
		TokenAddress = TokenAddress?.Trim();
		SearchQuery = SearchQuery?.Trim();
		if (!TokenAddress.IsEmpty() && ChainId.IsEmpty())
			throw new InvalidOperationException(
				"ChainId is required when TokenAddress is set.");
		if (TokenAddress.IsEmpty() && SearchQuery.IsEmpty())
			throw new InvalidOperationException(
				"SearchQuery is required when TokenAddress is empty.");
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting, cancellationToken);
		try
		{
			_restClient = new(
				RestEndpoint, RequestInterval)
			{
				Parent = this,
			};
			RememberPairs(
				await RestClient.LookupAsync(
					ChainId,
					TokenAddress,
					SearchQuery,
					cancellationToken));
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
			foreach (var item in due)
			{
				try
				{
					var snapshot = await RestClient.GetPairAsync(
						item.Value.Pair.ChainId,
						item.Value.Pair.PairAddress,
						cancellationToken);
					if (snapshot is not null)
						await SendLevel1Async(
							item.Value.Pair,
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
