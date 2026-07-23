namespace StockSharp.MetaApi;

public partial class MetaApiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var count = (int)Math.Clamp(lookupMsg.Count ?? 10000, 1, 10000);
		var query = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode);
		var specifications = GetSpecifications();
		if (!query.IsEmpty() && !specifications.Any(item =>
			item.Symbol.EqualsIgnoreCase(query)))
		{
			MetaApiSymbolSpecification specification = null;
			try
			{
				specification = await Rest.GetSpecificationAsync(PortfolioName, query,
					cancellationToken);
			}
			catch (MetaApiApiException error) when (
				error.StatusCode == HttpStatusCode.NotFound)
			{
				this.AddDebugLog("MetaApi has no exact symbol match for {0}.", query);
			}
			if (specification?.Symbol.IsEmpty() == false)
			{
				using (_sync.EnterScope())
					_specifications[specification.Symbol] = specification;
				specifications = [.. specifications, specification];
			}
		}
		if (specifications.Length == 0)
		{
			var symbols = await Rest.GetSymbolsAsync(PortfolioName,
				cancellationToken) ?? [];
			if (!query.IsEmpty())
				symbols = [.. symbols.Where(symbol =>
					symbol.Contains(query, StringComparison.OrdinalIgnoreCase))];
			var loaded = new List<MetaApiSymbolSpecification>();
			foreach (var symbol in symbols.Take(count))
			{
				var specification = await Rest.GetSpecificationAsync(PortfolioName,
					symbol, cancellationToken);
				if (specification is null)
					continue;
				loaded.Add(specification);
				using (_sync.EnterScope())
					_specifications[specification.Symbol] = specification;
			}
			specifications = [.. loaded];
		}

		var sent = 0;
		foreach (var specification in specifications
			.OrderBy(static item => item.Symbol, StringComparer.OrdinalIgnoreCase))
		{
			if (sent >= count)
				break;
			if (!query.IsEmpty() &&
				!specification.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) &&
				specification.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) != true)
				continue;
			var security = specification.ToSecurityMessage(lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			sent++;
		}
		this.AddDebugLog("MetaApi security lookup returned {0} symbols.", sent);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToSymbol();
		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
		{
			var price = await Rest.GetPriceAsync(PortfolioName, symbol,
				cancellationToken);
			if (price is not null && IsInRange(price.Time, mdMsg.From, mdMsg.To))
				await SendPriceAsync(mdMsg.TransactionId, mdMsg.SecurityId, price,
					cancellationToken);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscriptionAsync(new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Symbol = symbol,
			Kind = MetaApiSubscriptionKind.Quotes,
		}, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToSymbol();
		var remaining = (int)Math.Clamp(mdMsg.Count ?? 1000, 1, 1000);
		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
		{
			var ticks = await Rest.GetHistoricalTicksAsync(PortfolioName, symbol,
				mdMsg.From?.ToUniversalTime(), 0, remaining, cancellationToken) ?? [];
			foreach (var tick in ticks.OrderBy(static item => item.Time))
			{
				if (!IsInRange(tick.Time, mdMsg.From, mdMsg.To))
					continue;
				await SendTickAsync(mdMsg.TransactionId, mdMsg.SecurityId, tick,
					cancellationToken);
				if (--remaining == 0)
					break;
			}
		}
		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscriptionAsync(new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Symbol = symbol,
			Kind = MetaApiSubscriptionKind.Ticks,
		}, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToSymbol();
		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
		{
			var book = await Rest.GetBookAsync(PortfolioName, symbol, cancellationToken);
			if (book is not null && IsInRange(book.Time, mdMsg.From, mdMsg.To))
				await SendBookAsync(mdMsg.TransactionId, mdMsg.SecurityId, book,
					cancellationToken);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscriptionAsync(new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Symbol = symbol,
			Kind = MetaApiSubscriptionKind.MarketDepth,
		}, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscriptionAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.ToSymbol();
		var timeFrame = mdMsg.GetTimeFrame();
		var nativeTimeFrame = timeFrame.ToMetaApiTimeFrame();
		var remaining = (int)Math.Clamp(mdMsg.Count ?? 1000, 1, 1000);
		if (mdMsg.From is not null || mdMsg.To is not null || mdMsg.IsHistoryOnly())
		{
			var candles = await Rest.GetHistoricalCandlesAsync(PortfolioName, symbol,
				nativeTimeFrame, mdMsg.To?.ToUniversalTime(), remaining,
				cancellationToken) ?? [];
			foreach (var candle in candles.OrderBy(static item => item.Time))
			{
				if (!IsInRange(candle.Time, mdMsg.From, mdMsg.To))
					continue;
				await SendCandleAsync(mdMsg.TransactionId, mdMsg.SecurityId, candle,
					timeFrame, cancellationToken);
				if (--remaining == 0)
					break;
			}
		}
		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscriptionAsync(new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Symbol = symbol,
			Kind = MetaApiSubscriptionKind.Candles,
			TimeFrame = timeFrame,
		}, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask AddLiveSubscriptionAsync(MarketSubscription subscription,
		CancellationToken cancellationToken)
	{
		bool isFirst;
		using (_sync.EnterScope())
		{
			isFirst = !_marketSubscriptions.Values.Any(item =>
				SameNativeSubscription(item, subscription));
			_marketSubscriptions.Add(subscription.TransactionId, subscription);
		}
		if (!isFirst)
			return;

		try
		{
			await Stream.SubscribeMarketDataAsync(subscription.Symbol,
				ToNativeSubscription(subscription), cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_marketSubscriptions.Remove(subscription.TransactionId);
			throw;
		}
	}

	private async ValueTask RemoveLiveSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription;
		bool isLast;
		using (_sync.EnterScope())
		{
			if (!_marketSubscriptions.Remove(transactionId, out subscription))
				return;
			isLast = !_marketSubscriptions.Values.Any(item =>
				SameNativeSubscription(item, subscription));
		}
		if (!isLast)
			return;
		try
		{
			await Stream.UnsubscribeMarketDataAsync(subscription.Symbol,
				ToNativeSubscription(subscription), cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_marketSubscriptions[transactionId] = subscription;
			throw;
		}
	}

	private async ValueTask ProcessPricesPacketAsync(
		MetaApiSynchronizationPacket packet,
		CancellationToken cancellationToken)
	{
		await UpdateAccountMetricsAsync(packet.Equity, packet.Margin,
			packet.FreeMargin, packet.MarginLevel, cancellationToken);

		foreach (var price in packet.Prices ?? [])
		{
			foreach (var subscription in GetMarketSubscriptions(price.Symbol,
				MetaApiSubscriptionKind.Quotes))
				await SendPriceAsync(subscription.TransactionId,
					subscription.SecurityId, price, cancellationToken);
		}
		foreach (var tick in packet.Ticks ?? [])
		{
			foreach (var subscription in GetMarketSubscriptions(tick.Symbol,
				MetaApiSubscriptionKind.Ticks))
				await SendTickAsync(subscription.TransactionId,
					subscription.SecurityId, tick, cancellationToken);
		}
		foreach (var book in packet.Books ?? [])
		{
			foreach (var subscription in GetMarketSubscriptions(book.Symbol,
				MetaApiSubscriptionKind.MarketDepth))
				await SendBookAsync(subscription.TransactionId,
					subscription.SecurityId, book, cancellationToken);
		}
		foreach (var candle in packet.Candles ?? [])
		{
			foreach (var subscription in GetMarketSubscriptions(candle.Symbol,
				MetaApiSubscriptionKind.Candles))
			{
				if (subscription.TimeFrame is not { } timeFrame ||
					!timeFrame.ToMetaApiTimeFrame().EqualsIgnoreCase(candle.Timeframe))
					continue;
				await SendCandleAsync(subscription.TransactionId,
					subscription.SecurityId, candle, timeFrame, cancellationToken);
			}
		}
	}

	private MarketSubscription[] GetMarketSubscriptions(string symbol,
		MetaApiSubscriptionKind kind)
	{
		using (_sync.EnterScope())
			return [.. _marketSubscriptions.Values.Where(item => item.Kind == kind &&
				item.Symbol.EqualsIgnoreCase(symbol))];
	}

	private ValueTask SendPriceAsync(long transactionId, SecurityId securityId,
		MetaApiSymbolPrice price, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = NormalizeTime(price.Time),
		}
		.TryAdd(Level1Fields.BestBidPrice, Positive(price.Bid))
		.TryAdd(Level1Fields.BestAskPrice, Positive(price.Ask)), cancellationToken);

	private ValueTask SendTickAsync(long transactionId, SecurityId securityId,
		MetaApiTick tick, CancellationToken cancellationToken)
	{
		var serverTime = NormalizeTime(tick.Time);
		var price = tick.Last is > 0 ? tick.Last.Value
			: tick.Bid > 0 && tick.Ask > 0 ? (tick.Bid + tick.Ask) / 2
			: Math.Max(tick.Bid, tick.Ask);
		if (price <= 0)
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
			TradeStringId = $"{tick.Symbol}:{serverTime.Ticks}:" +
				price.ToString(CultureInfo.InvariantCulture),
			TradePrice = price,
			TradeVolume = tick.Volume ?? 0,
			OriginSide = tick.Side?.ToLowerInvariant() switch
			{
				"buy" => Sides.Buy,
				"sell" => Sides.Sell,
				_ => null,
			},
		}, cancellationToken);
	}

	private ValueTask SendBookAsync(long transactionId, SecurityId securityId,
		MetaApiBook book, CancellationToken cancellationToken)
	{
		var entries = book.Book ?? [];
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = NormalizeTime(book.Time),
			Bids = [.. entries.Where(static item =>
				item.Type?.Contains("BUY", StringComparison.OrdinalIgnoreCase) == true)
				.OrderByDescending(static item => item.Price)
				.Select(static item => new QuoteChange(item.Price, item.Volume))],
			Asks = [.. entries.Where(static item =>
				item.Type?.Contains("SELL", StringComparison.OrdinalIgnoreCase) == true)
				.OrderBy(static item => item.Price)
				.Select(static item => new QuoteChange(item.Price, item.Volume))],
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(long transactionId, SecurityId securityId,
		MetaApiCandle candle, TimeSpan timeFrame, CancellationToken cancellationToken)
	{
		var openTime = NormalizeTime(candle.Time);
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume ?? candle.TickVolume,
			TotalTicks = candle.TickVolume > 0
				? (int)Math.Min(candle.TickVolume, int.MaxValue) : null,
			TypedArg = timeFrame,
			State = closeTime <= DateTime.UtcNow
				? CandleStates.Finished : CandleStates.Active,
		}, cancellationToken);
	}

	private static MetaApiMarketDataSubscription ToNativeSubscription(
		MarketSubscription subscription)
		=> new()
		{
			Type = subscription.Kind switch
			{
				MetaApiSubscriptionKind.Quotes => "quotes",
				MetaApiSubscriptionKind.Ticks => "ticks",
				MetaApiSubscriptionKind.MarketDepth => "marketDepth",
				MetaApiSubscriptionKind.Candles => "candles",
				_ => throw new ArgumentOutOfRangeException(nameof(subscription)),
			},
			Timeframe = subscription.TimeFrame?.ToMetaApiTimeFrame(),
		};

	private static bool SameNativeSubscription(MarketSubscription left,
		MarketSubscription right)
		=> left.Kind == right.Kind && left.Symbol.EqualsIgnoreCase(right.Symbol) &&
			left.TimeFrame == right.TimeFrame;

	private static DateTime NormalizeTime(DateTime time)
		=> time == default ? DateTime.UtcNow : time.ToUniversalTime();

	private static bool IsInRange(DateTime time, DateTime? from, DateTime? to)
	{
		time = NormalizeTime(time);
		return (from is null || time >= from.Value.ToUniversalTime()) &&
			(to is null || time <= to.Value.ToUniversalTime());
	}

	private static decimal? Positive(decimal value) => value > 0 ? value : null;
}
