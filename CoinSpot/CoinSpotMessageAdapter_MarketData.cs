namespace StockSharp.CoinSpot;

public partial class CoinSpotMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requested = lookupMsg.SecurityId.SecurityCode;
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in GetMarkets().OrderBy(
			static value => value.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.CoinSpot))
				continue;
			if (!requested.IsEmpty() &&
				!requested.EqualsIgnoreCase(market.SecurityCode) &&
				!requested.EqualsIgnoreCase(market.NativeSymbol))
				continue;
			var security = CreateSecurity(
				market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					SecurityId = security.SecurityId,
					ServerTime = CurrentTime,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				}.TryAdd(
					Level1Fields.State,
					SecurityStates.Trading),
				cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"CoinSpot does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(
			market,
			await RestClient.GetTickerAsync(
				market.NativeSymbol, cancellationToken),
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"CoinSpot does not expose historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var maximumDepth = CoinSpotRestClient.NormalizeDepth(
			mdMsg.MaxDepth ?? 50);
		await SendDepthAsync(
			market.SecurityCode,
			await RestClient.GetDepthAsync(
				market.NativeSymbol,
				maximumDepth,
				cancellationToken),
			mdMsg.TransactionId,
			maximumDepth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
				Depth = maximumDepth,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var maximum = (mdMsg.Count ?? 100)
			.Min(500).Max(1).To<int>();
		var trades = await RestClient.GetPublicTradesAsync(
			market.NativeSymbol, cancellationToken);

		foreach (var trade in (trades ?? [])
			.Where(trade =>
				(from is null || trade.Time >= from.Value) &&
				trade.Time <= to)
			.OrderBy(static trade => trade.Time)
			.TakeLast(maximum))
		{
			if (!AddTrade(
				market.NativeSymbol, trade.Id, false))
				continue;
			await SendPublicTradeAsync(
				market.SecurityCode,
				trade,
				mdMsg.TransactionId,
				cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(
		CoinSpotMarket market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToStockSharp(),
			Name = market.SecurityCode,
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteUnit.ToCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = market.VolumeStep,
			OriginalTransactionId = originalTransactionId,
		};

	private ValueTask SendLevel1Async(
		CoinSpotMarket market,
		CoinSpotTicker ticker,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null)
			return default;
		return SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = market.ToStockSharp(),
				ServerTime = ticker.Timestamp > 0
					? DateTimeOffset.FromUnixTimeMilliseconds(
						ticker.Timestamp).UtcDateTime
					: CurrentTime,
				OriginalTransactionId = originalTransactionId,
			}
			.TryAdd(
				Level1Fields.LastTradePrice, ticker.LastPrice)
			.TryAdd(
				Level1Fields.BestBidPrice, ticker.BidPrice)
			.TryAdd(
				Level1Fields.BestAskPrice, ticker.AskPrice)
			.TryAdd(
				Level1Fields.State, SecurityStates.Trading),
			cancellationToken);
	}

	private ValueTask SendDepthAsync(
		string securityCode,
		CoinSpotDepth depth,
		long originalTransactionId,
		int maximumDepth,
		CancellationToken cancellationToken)
	{
		if (depth is null)
			return default;
		return SendOutMessageAsync(
			new QuoteChangeMessage
			{
				SecurityId = new()
				{
					SecurityCode = securityCode,
					BoardCode = BoardCodes.CoinSpot,
				},
				ServerTime = depth.Time ?? CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = [.. depth.Bids
					.Take(maximumDepth)
					.Select(static quote => new QuoteChange(
						quote.Price, quote.Volume))],
				Asks = [.. depth.Asks
					.Take(maximumDepth)
					.Select(static quote => new QuoteChange(
						quote.Price, quote.Volume))],
			},
			cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(
		string securityCode,
		CoinSpotTrade trade,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = new()
				{
					SecurityCode = securityCode,
					BoardCode = BoardCodes.CoinSpot,
				},
				ServerTime = trade.Time,
				OriginalTransactionId =
					originalTransactionId,
				TradeStringId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Volume.Abs(),
				OriginSide = trade.Side,
			},
			cancellationToken);

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
