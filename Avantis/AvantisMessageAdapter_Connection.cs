namespace StockSharp.Avantis;

public partial class AvantisMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_apiClient is not null || _rpcClient is not null ||
			_feedClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_apiClient = new(MarketDataEndpoint, CoreApiEndpoint, FeedEndpoint,
				LazerEndpoint) { Parent = this };
			_rpcClient = new(RpcEndpoint, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await RpcClient.VerifyChainAsync(cancellationToken);
			if (RpcClient.IsWalletConfigured)
			{
				WalletAddress = RpcClient.WalletAddress;
				_portfolioName = "Avantis_Base_" +
					RpcClient.WalletAddress[2..10];
			}
			await RefreshMarketsAsync(cancellationToken);
			_feedClient = new(LazerEndpoint, HermesEndpoint) { Parent = this };
			FeedClient.PriceReceived += OnPriceAsync;
			FeedClient.Error += OnFeedErrorAsync;
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
		_ = timeMsg;
		EnsureConnected();
		if (!RpcClient.IsWalletConfigured ||
			(_portfolioSubscriptionId == 0 && _orderStatusSubscriptionId == 0) ||
			DateTime.UtcNow - _lastAccountRefresh < AccountRefreshInterval)
			return;
		_lastAccountRefresh = DateTime.UtcNow;
		await RefreshAccountSubscriptionsAsync(cancellationToken);
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var data = await ApiClient.GetMarketsAsync(cancellationToken);
		var markets = new List<AvantisMarket>();
		foreach (var pair in data.Pairs ?? [])
		{
			if (pair is null || !pair.IsPairListed || pair.Index < 0 ||
				pair.From.IsEmpty() || pair.To.IsEmpty() ||
				pair.Feed?.FeedId.IsEmpty() != false)
				continue;
			string feedId;
			try
			{
				feedId = pair.Feed.FeedId.NormalizeFeedId();
			}
			catch (Exception error)
			{
				this.AddWarningLog(
					"Skipping Avantis pair {0}: invalid feed ID ({1}).",
					pair.Index, error.Message);
				continue;
			}
			var exponent = pair.LazerFeed?.Exponent ?? -10;
			var minimumPosition = pair.PairMinimumPositionValue > 0
				? pair.PairMinimumPositionValue
				: pair.MinimumPositionValue;
			markets.Add(new()
			{
				PairIndex = pair.Index,
				Symbol = pair.From.Trim().ToUpperInvariant() + "/" +
					pair.To.Trim().ToUpperInvariant(),
				BaseAsset = pair.From.Trim().ToUpperInvariant(),
				QuoteAsset = pair.To.Trim().ToUpperInvariant(),
				FeedId = feedId,
				LazerFeedId = pair.LazerFeed?.FeedId,
				LazerExponent = pair.LazerFeed?.Exponent,
				IsLazerStable = pair.LazerFeed?.State.Equals(
					"stable", StringComparison.OrdinalIgnoreCase) == true,
				IsOpen = pair.Feed.Attributes?.IsOpen != false,
				MinimumLeverage = pair.Leverages?.Minimum ?? 1m,
				MaximumLeverage = pair.Leverages?.Maximum ?? 1m,
				MinimumPnlLeverage = pair.Leverages?.MinimumPnl ?? 0m,
				MaximumPnlLeverage = pair.Leverages?.MaximumPnl ?? 0m,
				MinimumPositionValue = minimumPosition,
				OpenInterest = pair.PairOpenInterest > 0
					? pair.PairOpenInterest
					: (pair.OpenInterest?.Long ?? 0m) +
						(pair.OpenInterest?.Short ?? 0m),
				PriceStep = AvantisExtensions.PriceStep(exponent),
			});
		}
		if (markets.Count == 0)
			throw new InvalidDataException(
				"Avantis returned no listed perpetual markets.");
		var duplicates = markets.GroupBy(static market => market.Symbol,
			StringComparer.OrdinalIgnoreCase).FirstOrDefault(
			static group => group.Count() > 1);
		if (duplicates is not null)
			throw new InvalidDataException(
				"Avantis returned duplicate market symbol '" +
				duplicates.Key + "'.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByIndex.Clear();
			foreach (var market in markets)
			{
				_markets.Add(market.Symbol, market);
				_marketsByIndex.Add(market.PairIndex, market);
			}
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var feed = _feedClient;
		var api = _apiClient;
		var rpc = _rpcClient;
		_feedClient = null;
		_apiClient = null;
		_rpcClient = null;
		if (feed is not null)
		{
			try
			{
				await feed.StopAsync(cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			feed.Dispose();
		}
		api?.Dispose();
		rpc?.Dispose();
		ClearState();
	}

	private ValueTask OnFeedErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);
}
