namespace StockSharp.TwelveData;

public partial class TwelveDataMessageAdapter
{
	private readonly record struct ParsedCandle(TwelveDataCandle Value, DateTime OpenTime);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var board = lookupMsg.SecurityId.BoardCode;
		var value = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name);
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async Task Emit(TwelveDataReferenceItem item, TwelveDataMarkets market,
			string cryptoExchange = null)
		{
			if (item == null || left <= 0 || !item.Matches(value))
				return;
			var security = item.ToSecurityMessage(market,
				cryptoExchange.IsEmpty(CryptoExchange),
				lookupMsg.TransactionId);
			if (security == null || !security.IsMatch(lookupMsg, securityTypes) ||
				!sent.Add(security.SecurityId.Native as string))
			{
				return;
			}
			if (skip > 0)
			{
				skip--;
				return;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}

		var noBoard = board.IsEmpty();
		var noTypes = securityTypes.Count == 0;
		bool Requested(TwelveDataMarkets market)
		{
			if (!noBoard)
				return board.EqualsIgnoreCase(market.ToBoard());
			if (noTypes)
				return true;
			return market switch
			{
				TwelveDataMarkets.Etfs => securityTypes.Contains(SecurityTypes.Etf),
				TwelveDataMarkets.Forex => securityTypes.Contains(SecurityTypes.Currency),
				TwelveDataMarkets.Crypto => securityTypes.Contains(SecurityTypes.CryptoCurrency),
				TwelveDataMarkets.Commodities => securityTypes.Contains(SecurityTypes.Commodity),
				_ => securityTypes.Any(type => type is not SecurityTypes.Etf and
					not SecurityTypes.Currency and not SecurityTypes.CryptoCurrency and
					not SecurityTypes.Commodity),
			};
		}

		if (!value.IsEmpty())
		{
			var outputSize = checked((int)Math.Clamp(Math.Min(skip, 120) +
				Math.Min(left, 120), 1, 120));
			var response = await SafeRest().Search(value, outputSize, cancellationToken);
			foreach (var item in response?.Data ?? [])
			{
				var market = item.GetMarket();
				if (Requested(market))
					await Emit(item, market);
				if (left <= 0)
					break;
			}
		}
		else
		{
			async Task EmitAll(Task<TwelveDataReferenceResponse> task, TwelveDataMarkets market)
			{
				foreach (var item in (await task)?.Data ?? [])
				{
					if (market == TwelveDataMarkets.Crypto && CryptoExchange.IsEmpty() &&
						item?.AvailableExchanges?.Length > 0)
					{
						foreach (var exchange in item.AvailableExchanges
							.Where(exchange => !exchange.IsEmpty())
							.Distinct(StringComparer.OrdinalIgnoreCase))
						{
							await Emit(item, market, exchange);
							if (left <= 0)
								break;
						}
					}
					else
					{
						await Emit(item, market);
					}
					if (left <= 0)
						break;
				}
			}

			if (Requested(TwelveDataMarkets.Stocks) && left > 0)
				await EmitAll(SafeRest().GetStocks(null, Country, StockExchange, StockMic,
					cancellationToken), TwelveDataMarkets.Stocks);
			if (Requested(TwelveDataMarkets.Etfs) && left > 0)
				await EmitAll(SafeRest().GetEtfs(null, Country, StockExchange, StockMic,
					cancellationToken), TwelveDataMarkets.Etfs);
			if (Requested(TwelveDataMarkets.Forex) && left > 0)
				await EmitAll(SafeRest().GetForexPairs(null, cancellationToken),
					TwelveDataMarkets.Forex);
			if (Requested(TwelveDataMarkets.Crypto) && left > 0)
				await EmitAll(SafeRest().GetCryptocurrencies(null, CryptoExchange,
					cancellationToken), TwelveDataMarkets.Crypto);
			if (Requested(TwelveDataMarkets.Commodities) && left > 0)
				await EmitAll(SafeRest().GetCommodities(null, cancellationToken),
					TwelveDataMarkets.Commodities);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscription(mdMsg.OriginalTransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (mdMsg.From != null || mdMsg.To != null)
			throw new NotSupportedException(
				"Twelve Data does not expose historical Level1 event sequences.");

		var key = mdMsg.SecurityId.GetTwelveDataKey(StockExchange, StockMic, CryptoExchange);
		var securityId = mdMsg.SecurityId.NormalizeTwelveData(key);
		var remaining = mdMsg.Count;
		var snapshotSent = await SendLevel1Snapshot(mdMsg.TransactionId, securityId, key,
			cancellationToken);
		if (snapshotSent && remaining is > 0)
			remaining--;

		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscription(mdMsg, securityId, key, remaining, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (!Extensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException($"Twelve Data does not support {timeFrame} candles.");
		var key = mdMsg.SecurityId.GetTwelveDataKey(StockExchange, StockMic, CryptoExchange);
		var securityId = mdMsg.SecurityId.NormalizeTwelveData(key);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = (mdMsg.From ?? Extensions.EstimateFrom(to, timeFrame, mdMsg.Count)).ToUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The candle-history start time is after its end time.");

		var left = mdMsg.Count ?? long.MaxValue;
		var emitted = new HashSet<long>();
		var current = from;
		while (current <= to && left > 0)
		{
			var outputSize = checked((int)Math.Min(5000, left));
			var response = await SafeRest().GetTimeSeries(key, timeFrame.ToNativeInterval(),
				current, to, outputSize, Adjustment, IsPrePost, cancellationToken);
			var values = response?.Values ?? [];
			if (values.Length == 0)
				break;

			var timeZone = response.Meta?.ExchangeTimezone;
			var parsed = values.Where(value => value?.DateTime.IsEmpty() == false)
				.Select(value => new ParsedCandle(value,
					Extensions.ParseCandleTime(value.DateTime, timeFrame, timeZone)))
				.OrderBy(item => item.OpenTime).ToArray();
			DateTime? next = null;
			foreach (var item in parsed)
			{
				var closeTime = Extensions.GetCandleCloseTime(item.Value.DateTime,
					item.OpenTime, timeFrame, timeZone);
				if (next == null || closeTime > next)
					next = closeTime;
				if (item.OpenTime < from || item.OpenTime > to ||
					!emitted.Add(item.OpenTime.Ticks) || item.Value.Open == null ||
					item.Value.High == null || item.Value.Low == null || item.Value.Close == null)
				{
					continue;
				}

				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					DataType = mdMsg.DataType2,
					OpenTime = item.OpenTime,
					CloseTime = closeTime,
					OpenPrice = item.Value.Open.Value,
					HighPrice = item.Value.High.Value,
					LowPrice = item.Value.Low.Value,
					ClosePrice = item.Value.Close.Value,
					TotalVolume = item.Value.Volume.GetValueOrDefault(),
					State = CandleStates.Finished,
				}, cancellationToken);
				if (--left <= 0)
					break;
			}

			if (left <= 0 || values.Length < outputSize || next == null || next <= current)
				break;
			current = next.Value;
			await IterationInterval.Delay(cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<bool> SendLevel1Snapshot(long transactionId, SecurityId securityId,
		TwelveDataSecurityKey key, CancellationToken cancellationToken)
	{
		var quote = await SafeRest().GetQuote(key, IsPrePost, cancellationToken);
		if (quote == null)
			return false;

		var regularTime = quote.LastQuoteAt is > 0
			? Extensions.FromUnixSeconds(quote.LastQuoteAt.Value) :
			quote.Timestamp is > 0 ? Extensions.FromUnixSeconds(quote.Timestamp.Value) : (DateTime?)null;
		var extendedTime = quote.ExtendedTimestamp is > 0
			? Extensions.FromUnixSeconds(quote.ExtendedTimestamp.Value) : (DateTime?)null;
		var useExtended = IsPrePost && Positive(quote.ExtendedPrice) != null &&
			extendedTime != null && (regularTime == null || extendedTime > regularTime);
		var lastPrice = useExtended ? quote.ExtendedPrice : quote.Close;
		var lastTime = useExtended ? extendedTime : regularTime;
		var change = useExtended ? quote.ExtendedPercentChange : quote.PercentChange;
		var serverTime = lastTime ?? regularTime ?? extendedTime;
		if (serverTime == null)
			return false;

		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime.Value,
		}
		.TryAdd(Level1Fields.LastTradePrice, Positive(lastPrice))
		.TryAdd(Level1Fields.LastTradeTime, Positive(lastPrice) != null ? lastTime : null)
		.TryAdd(Level1Fields.OpenPrice, Positive(quote.Open))
		.TryAdd(Level1Fields.HighPrice, Positive(quote.High))
		.TryAdd(Level1Fields.LowPrice, Positive(quote.Low))
		.TryAdd(Level1Fields.SettlementPrice, Positive(quote.PreviousClose))
		.TryAdd(Level1Fields.Volume, Positive(quote.Volume))
		.TryAdd(Level1Fields.HighPrice52Week, Positive(quote.FiftyTwoWeek?.High))
		.TryAdd(Level1Fields.LowPrice52Week, Positive(quote.FiftyTwoWeek?.Low))
		.TryAdd(Level1Fields.Change, change);
		if (message.Changes.Count == 0)
			return false;
		await SendOutMessageAsync(message, cancellationToken);
		return true;
	}
}
