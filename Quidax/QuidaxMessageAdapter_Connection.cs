namespace StockSharp.Quidax;

public partial class QuidaxMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (PollingInterval <= TimeSpan.Zero)
			throw new InvalidOperationException(
				"Quidax polling interval must be positive.");
		UserId = UserId.IsEmpty() ? "me" : UserId.Trim();
		RestEndpoint = NormalizeEndpoint(RestEndpoint);

		ClearState();
		var client = new QuidaxRestClient(
			RestEndpoint,
			Token,
			UserId)
		{
			Parent = this,
		};
		_restClient = client;
		try
		{
			var markets = await client.GetMarketsAsync(
				cancellationToken);
			if (markets is not { Length: > 0 })
				throw new InvalidDataException(
					"Quidax returned no spot markets.");
			RegisterMarkets(markets);
			connectMsg.SessionId = client.IsCredentialsAvailable
				? $"Quidax {Token.ToId()}"
				: "Quidax public";
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		DisposeClient();
		await base.DisconnectAsync(
			disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(
		ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(
		TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var shouldPoll = _restClient is not null &&
			CurrentTime - _lastPoll >= PollingInterval;
		if (shouldPoll &&
			await _pollSync.WaitAsync(0, cancellationToken))
		{
			try
			{
				_lastPoll = CurrentTime;
				await RefreshMarketDataAsync(cancellationToken);
				if (RestClient.IsCredentialsAvailable)
				{
					if (_portfolioSubscriptionId != 0)
						await SendPortfolioSnapshotAsync(
							_portfolioSubscriptionId,
							cancellationToken);
					if (_orderStatusSubscriptionId != 0)
						await PollPrivateStateAsync(
							_orderStatusSubscriptionId,
							cancellationToken);
				}
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				_pollSync.Release();
			}
		}
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshMarketDataAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, MarketSubscription>[] level1;
		KeyValuePair<long, DepthSubscription>[] depths;
		KeyValuePair<long, MarketSubscription>[] ticks;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions];
			depths = [.. _depthSubscriptions];
			ticks = [.. _tickSubscriptions];
		}

		foreach (var group in level1.GroupBy(
			static pair => pair.Value.NativeSymbol,
			StringComparer.OrdinalIgnoreCase))
		{
			var ticker = await RestClient.GetTickerAsync(
				group.Key, cancellationToken);
			var market = GetMarket(group.Key);
			if (market is null)
				continue;
			foreach (var pair in group)
				await SendLevel1Async(
					market,
					ticker,
					pair.Key,
					cancellationToken);
		}

		foreach (var group in depths.GroupBy(
			static pair => pair.Value.NativeSymbol,
			StringComparer.OrdinalIgnoreCase))
		{
			var maximum = group.Max(
				static pair => pair.Value.Depth);
			var depth = await RestClient.GetDepthAsync(
				group.Key,
				maximum,
				cancellationToken);
			foreach (var pair in group)
				await SendDepthAsync(
					pair.Value.SecurityCode,
					depth,
					pair.Key,
					pair.Value.Depth,
					cancellationToken);
		}

		foreach (var group in ticks.GroupBy(
			static pair => pair.Value.NativeSymbol,
			StringComparer.OrdinalIgnoreCase))
		{
			var trades = await RestClient.GetPublicTradesAsync(
				group.Key, cancellationToken);
			foreach (var trade in (trades ?? [])
				.OrderBy(static trade => trade.Timestamp))
			{
				var tradeId = GetPublicTradeId(trade);
				if (!AddTrade(group.Key, tradeId, false))
					continue;
				foreach (var pair in group)
					await SendPublicTradeAsync(
						pair.Value.SecurityCode,
						trade,
						tradeId,
						pair.Key,
						cancellationToken);
			}
		}
	}

	private void DisposeClient()
	{
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
