namespace StockSharp.Paxos;

using StockSharp.Paxos.Native;
using StockSharp.Paxos.Native.Model;

public partial class PaxosMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(ApiEndpoint, OAuthEndpoint, ClientId, ClientSecret,
				Scopes)
			{
				Parent = this,
			};
			var markets = await RestClient.GetMarketsAsync(cancellationToken);
			UpdateMarkets(markets);
			using (_sync.EnterScope())
				_nextPrivatePoll = DateTime.UtcNow + PollingInterval;
			connectMsg.SessionId = $"Paxos {Environment}; {markets.Length} markets; " +
				(RestClient.IsAuthenticationAvailable
					? "OAuth private API enabled"
					: "public market data only");
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
			cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var currentTime = CurrentTime.EnsureUtc();
		var isPollRequired = false;
		using (_sync.EnterScope())
		{
			if (_restClient?.IsAuthenticationAvailable == true &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_activeOperations.Count > 0) &&
				currentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = currentTime + PollingInterval;
				isPollRequired = true;
			}
		}
		if (isPollRequired)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private void UpdateMarkets(IEnumerable<PaxosMarket> markets)
	{
		var valid = (markets ?? []).Where(static market =>
			market?.Market.IsEmpty() == false &&
			market.BaseAsset.IsEmpty() == false &&
			market.QuoteAsset.IsEmpty() == false).ToArray();
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in valid)
				_markets[market.Market] = market;
		}
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
		=> UpdateMarkets(await RestClient.GetMarketsAsync(cancellationToken));

	private async ValueTask RefreshProfilesAsync(
		CancellationToken cancellationToken)
	{
		EnsureAuthenticated();
		var profiles = await RestClient.GetProfilesAsync(PageSize, MaximumItems,
			cancellationToken);
		var references = new List<PortfolioReference>();
		foreach (var group in profiles.Where(static profile =>
			profile?.Id.IsEmpty() == false).GroupBy(profile =>
			profile.Nickname.IsEmpty() ? profile.Id : profile.Nickname,
			StringComparer.OrdinalIgnoreCase))
		{
			var duplicates = group.Count() > 1;
			foreach (var profile in group)
			{
				var suffix = duplicates
					? "_" + profile.Id[..profile.Id.Length.Min(8)]
					: string.Empty;
				references.Add(new()
				{
					Name = "Paxos_" + group.Key + suffix,
					Profile = profile,
				});
			}
		}
		using (_sync.EnterScope())
		{
			_portfolios.Clear();
			foreach (var portfolio in references)
				_portfolios[portfolio.Name] = portfolio;
		}
	}

	private async ValueTask RunSafelyAsync(
		Func<CancellationToken, ValueTask> action,
		CancellationToken cancellationToken)
	{
		try
		{
			await action(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask<T> TryReadDomainAsync<T>(
		Func<CancellationToken, ValueTask<T>> action, T fallback, string domain,
		CancellationToken cancellationToken)
	{
		try
		{
			return await action(cancellationToken);
		}
		catch (PaxosApiException error) when (error.StatusCode is
			HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
		{
			this.AddWarningLog(
				"Paxos credentials cannot read {0}; that domain is disabled: {1}",
				domain, error.Message);
			return fallback;
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		PaxosSocketClient[] sockets;
		PaxosRestClient rest;
		using (_sync.EnterScope())
		{
			sockets = [.. _marketSockets.Values.Concat(
				_executionSockets.Values).Distinct()];
			_marketSockets.Clear();
			_executionSockets.Clear();
			rest = _restClient;
			_restClient = null;
		}
		var errors = new List<Exception>();
		foreach (var socket in sockets)
		{
			UnsubscribeSocket(socket);
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
			try
			{
				socket.Dispose();
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
		}
		try
		{
			rest?.Dispose();
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
		ClearState();
		if (errors.Count == 1)
			ExceptionDispatchInfo.Capture(errors[0]).Throw();
		if (errors.Count > 1)
			throw new AggregateException(
				"One or more Paxos clients could not be disposed.", errors);
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_portfolios.Clear();
			_marketSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_books.Clear();
			_trackedOperations.Clear();
			_trackedReferences.Clear();
			_nativeIds.Clear();
			_activeOperations.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
			_transferFingerprints.Clear();
			_conversionFingerprints.Clear();
			_seenPublicTrades.Clear();
			_publicTradeOrder.Clear();
			_seenPrivateExecutions.Clear();
			_nextPrivatePoll = default;
		}
	}
}
