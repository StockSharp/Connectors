namespace StockSharp.Quidax;

public partial class QuidaxMessageAdapter
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
					BoardCodes.Quidax))
				continue;
			if (!requested.IsEmpty() &&
				!requested.EqualsIgnoreCase(market.SecurityCode) &&
				!requested.EqualsIgnoreCase(market.Id))
				continue;
			var security = CreateSecurity(
				market,
				lookupMsg.TransactionId);
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
			mdMsg.TransactionId,
			cancellationToken);
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
				"Quidax does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(
			market,
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
				NativeSymbol = market.Id,
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
			mdMsg.TransactionId,
			cancellationToken);
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
				"Quidax does not expose historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = QuidaxRestClient.NormalizeDepth(
			mdMsg.MaxDepth ?? 50);
		var snapshot = await RestClient.GetDepthAsync(
			market.Id,
			depth,
			cancellationToken);
		await SendDepthAsync(
			market.SecurityCode,
			snapshot,
			mdMsg.TransactionId,
			depth,
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
				NativeSymbol = market.Id,
				SecurityCode = market.SecurityCode,
				Depth = depth,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);
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
			.Min(1000).Max(1).To<int>();
		var trades = await RestClient.GetPublicTradesAsync(
			market.Id,
			cancellationToken);
		foreach (var trade in (trades ?? [])
			.Where(trade =>
			{
				var time = GetTradeTime(trade);
				return (from is null || time >= from.Value) &&
					time <= to;
			})
			.OrderBy(static trade => trade.Timestamp)
			.TakeLast(maximum))
		{
			var tradeId = GetPublicTradeId(trade);
			if (!AddTrade(market.Id, tradeId, false))
				continue;
			await SendPublicTradeAsync(
				market.SecurityCode,
				trade,
				tradeId,
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
				NativeSymbol = market.Id,
				SecurityCode = market.SecurityCode,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (!mdMsg.IsHistoryOnly())
			throw new NotSupportedException(
				"Quidax candles are available as REST history only.");

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Quidax does not support the {timeFrame} " +
					"candle interval.");
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = (mdMsg.Count ??
			GetCandleCount(mdMsg.From, to, timeFrame))
				.Min(10000).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime() ??
			SubtractSafely(to, timeFrame, count);
		var candles = await RestClient.GetCandlesAsync(
			market.Id,
			timeFrame,
			from,
			count,
			cancellationToken);
		foreach (var candle in (candles ?? [])
			.Where(candle =>
			{
				var time =
					candle.Timestamp.FromQuidaxTimestamp();
				return time >= from && time <= to;
			})
			.OrderBy(static candle => candle.Timestamp)
			.TakeLast(count))
			await SendCandleAsync(
				market,
				candle,
				timeFrame,
				mdMsg.TransactionId,
				cancellationToken);
		await CompleteMarketSubscriptionAsync(
			mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(
		QuidaxMarket market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Name.IsEmpty()
				? market.SecurityCode
				: market.Name,
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteUnit.ToCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = market.VolumeStep,
			MinVolume = market.VolumeStep,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask SendLevel1SnapshotAsync(
		QuidaxMarket market,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> await SendLevel1Async(
			market,
			await RestClient.GetTickerAsync(
				market.Id, cancellationToken),
			originalTransactionId,
			cancellationToken);

	private ValueTask SendLevel1Async(
		QuidaxMarket market,
		QuidaxTicker ticker,
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
					? ticker.Timestamp.FromQuidaxTimestamp()
					: CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(Level1Fields.LastTradePrice,
				ticker.LastPrice)
			.TryAdd(Level1Fields.BestBidPrice,
				ticker.BidPrice)
			.TryAdd(Level1Fields.BestAskPrice,
				ticker.AskPrice)
			.TryAdd(Level1Fields.OpenPrice,
				ticker.OpenPrice)
			.TryAdd(Level1Fields.HighPrice,
				ticker.HighPrice)
			.TryAdd(Level1Fields.LowPrice,
				ticker.LowPrice)
			.TryAdd(Level1Fields.Volume,
				ticker.Volume)
			.TryAdd(Level1Fields.State,
				SecurityStates.Trading),
			cancellationToken);
	}

	private ValueTask SendDepthAsync(
		string securityCode,
		QuidaxDepth depth,
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
					BoardCode = BoardCodes.Quidax,
				},
				ServerTime = depth.Timestamp > 0
					? depth.Timestamp.FromQuidaxTimestamp()
					: CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				Bids = ToQuotes(
					depth.Bids, false, maximumDepth),
				Asks = ToQuotes(
					depth.Asks, true, maximumDepth),
			},
			cancellationToken);
	}

	private ValueTask SendPublicTradeAsync(
		string securityCode,
		QuidaxTrade trade,
		string tradeId,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = new()
				{
					SecurityCode = securityCode,
					BoardCode = BoardCodes.Quidax,
				},
				ServerTime = GetTradeTime(trade),
				OriginalTransactionId =
					originalTransactionId,
				TradeStringId = tradeId,
				TradePrice = trade.Price,
				TradeVolume = trade.EffectiveVolume.Abs(),
				OriginSide = trade.EffectiveSide.ToSide(),
			},
			cancellationToken);

	private ValueTask SendCandleAsync(
		QuidaxMarket market,
		QuidaxCandle candle,
		TimeSpan timeFrame,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var openTime =
			candle.Timestamp.FromQuidaxTimestamp();
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(
			new TimeFrameCandleMessage
			{
				SecurityId = market.ToStockSharp(),
				OpenTime = openTime,
				CloseTime = closeTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TypedArg = timeFrame,
				OriginalTransactionId =
					originalTransactionId,
				State = closeTime <= CurrentTime
					? CandleStates.Finished
					: CandleStates.Active,
			},
			cancellationToken);
	}

	private static QuoteChange[] ToQuotes(
		decimal[][] levels,
		bool isAsk,
		int depth)
	{
		var grouped = (levels ?? [])
			.Where(static level =>
				level is { Length: >= 2 } &&
				level[0] > 0 &&
				level[1] > 0)
			.GroupBy(static level => level[0])
			.Select(static group => new QuoteChange(
				group.Key,
				group.Sum(static level => level[1])));
		return [.. (isAsk
			? grouped.OrderBy(static quote => quote.Price)
			: grouped.OrderByDescending(
				static quote => quote.Price))
			.Take(depth)];
	}

	private DateTime GetTradeTime(QuidaxTrade trade)
		=> trade?.CreatedAt?.ToUniversalTime() ??
			(trade?.Timestamp > 0
				? trade.Timestamp.FromQuidaxTimestamp()
				: CurrentTime);

	private static string GetPublicTradeId(QuidaxTrade trade)
		=> !trade.EffectiveId.IsEmpty()
			? trade.EffectiveId
			: string.Join(
				"-",
				trade.Timestamp.ToString(
					CultureInfo.InvariantCulture),
				trade.Price.ToWire(),
				trade.EffectiveVolume.ToWire(),
				trade.EffectiveSide);

	private static long GetCandleCount(
		DateTime? from,
		DateTime to,
		TimeSpan timeFrame)
	{
		if (from is null)
			return 1000;
		var count = (long)Math.Ceiling(
			(to - from.Value.ToUniversalTime()).Ticks /
			(double)timeFrame.Ticks) + 1;
		return count.Max(1).Min(10000);
	}

	private static DateTime SubtractSafely(
		DateTime to,
		TimeSpan timeFrame,
		int count)
	{
		var ticks = (decimal)timeFrame.Ticks * count;
		var maximum = to.Ticks - DateTime.UnixEpoch.Ticks;
		return ticks >= maximum
			? DateTime.UnixEpoch
			: to - TimeSpan.FromTicks((long)ticks);
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId,
			cancellationToken);
	}
}
