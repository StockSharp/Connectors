namespace StockSharp.Shioaji;

public partial class ShioajiMessageAdapter
{
	private sealed class HistoricalTick
	{
		public DateTime Time { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
		public decimal? BidPrice { get; init; }
		public decimal? BidVolume { get; init; }
		public decimal? AskPrice { get; init; }
		public decimal? AskVolume { get; init; }
		public int? TickType { get; init; }
	}

	private sealed class HistoricalCandle
	{
		public DateTime Time { get; init; }
		public decimal Open { get; init; }
		public decimal High { get; init; }
		public decimal Low { get; init; }
		public decimal Close { get; init; }
		public decimal Volume { get; init; }
		public decimal Turnover { get; init; }
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var requestedTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		if (!lookupMsg.SecurityId.SecurityCode.IsEmpty())
		{
			var nativeType = lookupMsg.SecurityType?.ToShioajiSecurityType();
			var contract = await _rest.GetContract(lookupMsg.SecurityId.SecurityCode, nativeType, cancellationToken);
			if (contract != null)
			{
				CacheContract(contract);
				var info = contract.ToSecurityType() == SecurityTypes.Warrant
					? null
					: await _rest.GetContractInfo(contract, cancellationToken);
				var security = CreateSecurityMessage(contract, info, lookupMsg.TransactionId);
				if (security.IsMatch(lookupMsg, requestedTypes))
					await SendOutMessageAsync(security, cancellationToken);
			}
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		foreach (var nativeType in _nativeSecurityTypes)
		{
			var securityType = nativeType.ToSecurityType();
			if (requestedTypes?.Any() == true && !requestedTypes.Contains(securityType))
				continue;

			var response = await _rest.GetContracts(nativeType, cancellationToken);
			foreach (var contract in response.Contracts ?? [])
			{
				if (contract?.Code.IsEmpty() != false)
					continue;
				CacheContract(contract);
				var security = CreateSecurityMessage(contract, null, lookupMsg.TransactionId);
				if (!security.IsMatch(lookupMsg, requestedTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Level1, "Quote", cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Ticks, "Tick", cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var depth = mdMsg.MaxDepth ?? 5;
		if (depth is < 1 or > 5)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.MaxDepth), depth,
				"Shioaji provides five market-depth levels.");
		return ProcessMarketSubscription(mdMsg, DataType.MarketDepth, "BidAsk", cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.GetTimeFrame() != TimeSpan.FromMinutes(1))
			throw new NotSupportedException("Shioaji provides one-minute historical K-bars only.");

		var contract = await ResolveContract(mdMsg.SecurityId, mdMsg.SecurityType, cancellationToken);
		await SendCandleHistory(mdMsg, contract, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask ProcessMarketSubscription(MarketDataMessage mdMsg, DataType dataType,
		string quoteType, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (!_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var removed))
				return;
			if (!_marketSubscriptions.Values.Any(subscription => subscription.Request.Key.EqualsIgnoreCase(removed.Request.Key)))
				await _rest.Unsubscribe(removed.Request, cancellationToken);
			return;
		}

		var contract = await ResolveContract(mdMsg.SecurityId, mdMsg.SecurityType, cancellationToken);
		if (contract.ToSecurityType() == SecurityTypes.Index && dataType != DataType.Level1)
			throw new NotSupportedException("Shioaji index streaming provides Level1 quotes, not trades or order books.");

		if (dataType == DataType.Level1)
			await SendSnapshot(mdMsg.TransactionId, mdMsg.SecurityId, contract, cancellationToken);
		else if (dataType == DataType.Ticks &&
			(mdMsg.IsHistoryOnly() || mdMsg.From != null || mdMsg.To != null || mdMsg.Count != null))
			await SendTickHistory(mdMsg, contract, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var request = new ShioajiMarketSubscriptionRequest
		{
			SecurityType = contract.SecurityType,
			Exchange = contract.Exchange,
			Code = contract.Code,
			TargetCode = contract.TargetCode,
			QuoteType = quoteType,
			IsIntradayOdd = false,
		};
		var subscription = new MarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Contract = contract,
			Request = request,
			DataType = dataType,
		};

		var isFirst = !_marketSubscriptions.Values.Any(existing => existing.Request.Key.EqualsIgnoreCase(request.Key));
		if (isFirst)
			await _rest.Subscribe(request, cancellationToken);
		_marketSubscriptions[mdMsg.TransactionId] = subscription;
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask SendSnapshot(long transactionId, SecurityId securityId, ShioajiContract contract,
		CancellationToken cancellationToken)
	{
		var snapshot = (await _rest.GetSnapshots([contract], cancellationToken))
			.FirstOrDefault(item => item != null && item.Code.EqualsIgnoreCase(contract.Code));
		if (snapshot == null)
			return;

		var serverTime = snapshot.DateTime.ParseTaiwanTime() ?? CurrentTime;
		var level1 = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, snapshot.Close)
		.TryAdd(Level1Fields.LastTradeTime, snapshot.Close != null ? serverTime : null)
		.TryAdd(Level1Fields.OpenPrice, snapshot.Open)
		.TryAdd(Level1Fields.HighPrice, snapshot.High)
		.TryAdd(Level1Fields.LowPrice, snapshot.Low)
		.TryAdd(Level1Fields.AveragePrice, snapshot.AveragePrice)
		.TryAdd(Level1Fields.Volume, snapshot.TotalVolume)
		.TryAdd(Level1Fields.Turnover, snapshot.TotalAmount)
		.TryAdd(Level1Fields.LastTradeVolume, snapshot.Volume)
		.TryAdd(Level1Fields.BestBidPrice, snapshot.BuyPrice)
		.TryAdd(Level1Fields.BestBidVolume, snapshot.BuyVolume)
		.TryAdd(Level1Fields.BestAskPrice, snapshot.SellPrice)
		.TryAdd(Level1Fields.BestAskVolume, snapshot.SellVolume)
		.TryAdd(Level1Fields.Change, snapshot.ChangePrice);
		if (level1.Changes.Count > 0)
			await SendOutMessageAsync(level1, cancellationToken);
	}

	private async ValueTask SendTickHistory(MarketDataMessage mdMsg, ShioajiContract contract,
		CancellationToken cancellationToken)
	{
		var to = (mdMsg.To ?? CurrentTime).NormalizeUtc();
		var from = mdMsg.From?.NormalizeUtc() ??
			ShioajiExtensions.ParseTaiwanTime(to.ToTaiwanLocal().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "00:00:00").Value;
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from, "History start must not be after its end.");

		var fromLocal = from.ToTaiwanLocal();
		var toLocal = to.ToTaiwanLocal();
		var countOnly = mdMsg.From == null && mdMsg.To == null && mdMsg.Count is > 0;
		var ticks = new List<HistoricalTick>();
		for (var date = fromLocal.Date; date <= toLocal.Date; date = date.AddDays(1))
		{
			var response = await _rest.GetTicks(new()
			{
				Contract = contract,
				Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				QueryType = countOnly ? "LastCount" : "AllDay",
				LastCount = countOnly ? (int)Math.Min(mdMsg.Count.Value, int.MaxValue) : null,
			}, cancellationToken);

			var length = new[] { response.DateTimes?.Length ?? 0, response.Close?.Length ?? 0,
				response.Volume?.Length ?? 0 }.Min();
			for (var i = 0; i < length; i++)
			{
				if (response.DateTimes[i].ParseTaiwanTime() is not DateTime time || time < from || time > to)
					continue;
				ticks.Add(new()
				{
					Time = time,
					Price = response.Close[i],
					Volume = response.Volume[i],
					BidPrice = At(response.BidPrice, i),
					BidVolume = At(response.BidVolume, i),
					AskPrice = At(response.AskPrice, i),
					AskVolume = At(response.AskVolume, i),
					TickType = At(response.TickType, i),
				});
			}
			if (countOnly)
				break;
		}

		IEnumerable<HistoricalTick> selected = ticks.OrderBy(tick => tick.Time);
		if (mdMsg.Count is long count)
			selected = selected.TakeLast((int)Math.Min(Math.Max(0, count), int.MaxValue));

		var index = 0;
		foreach (var tick in selected)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TradeStringId = $"{contract.Code}:{tick.Time.Ticks}:{index++}",
				TradePrice = tick.Price,
				TradeVolume = tick.Volume,
				OriginSide = tick.TickType switch { 1 => Sides.Buy, 2 => Sides.Sell, _ => null },
				ServerTime = tick.Time,
			}, cancellationToken);
		}
	}

	private async ValueTask SendCandleHistory(MarketDataMessage mdMsg, ShioajiContract contract,
		CancellationToken cancellationToken)
	{
		var to = (mdMsg.To ?? CurrentTime).NormalizeUtc();
		var from = (mdMsg.From ?? to.AddDays(-29)).NormalizeUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from, "History start must not be after its end.");

		var fromLocal = from.ToTaiwanLocal().Date;
		var toLocal = to.ToTaiwanLocal().Date;
		var candles = new List<HistoricalCandle>();
		for (var chunkStart = fromLocal; chunkStart <= toLocal;)
		{
			var chunkEnd = chunkStart.AddDays(29);
			if (chunkEnd > toLocal)
				chunkEnd = toLocal;
			var response = await _rest.GetKBars(new()
			{
				Contract = contract,
				Start = chunkStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				End = chunkEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			}, cancellationToken);
			var length = new[] { response.DateTimes?.Length ?? 0, response.Open?.Length ?? 0,
				response.High?.Length ?? 0, response.Low?.Length ?? 0, response.Close?.Length ?? 0,
				response.Volume?.Length ?? 0 }.Min();
			for (var i = 0; i < length; i++)
			{
				if (response.DateTimes[i].ParseTaiwanTime() is not DateTime time || time < from || time > to)
					continue;
				candles.Add(new()
				{
					Time = time,
					Open = response.Open[i],
					High = response.High[i],
					Low = response.Low[i],
					Close = response.Close[i],
					Volume = response.Volume[i],
					Turnover = At(response.Amount, i) ?? 0,
				});
			}
			chunkStart = chunkEnd.AddDays(1);
		}

		IEnumerable<HistoricalCandle> selected = candles.OrderBy(candle => candle.Time);
		if (mdMsg.Count is long count)
			selected = selected.TakeLast((int)Math.Min(Math.Max(0, count), int.MaxValue));
		foreach (var candle in selected)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = TimeSpan.FromMinutes(1),
				OpenTime = candle.Time,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TotalPrice = candle.Turnover,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessMarketEvent(string eventName, ShioajiMarketEvent data,
		CancellationToken cancellationToken)
	{
		if (data?.Code.IsEmpty() != false)
			return;
		var dataType = eventName.StartsWith("tick_", StringComparison.OrdinalIgnoreCase)
			? DataType.Ticks
			: eventName.StartsWith("bidask_", StringComparison.OrdinalIgnoreCase)
				? DataType.MarketDepth
				: DataType.Level1;
		foreach (var subscription in _marketSubscriptions.Values.Where(subscription =>
			subscription.DataType == dataType && subscription.Contract.IsSameInstrument(data.Code, data.Exchange)))
		{
			var serverTime = ShioajiExtensions.ParseTaiwanTime(data.Date, data.Time) ?? CurrentTime;
			if (dataType == DataType.Ticks)
			{
				if (data.Close.ToDecimalValue() is not decimal price)
					continue;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					TradeStringId = $"{data.Code}:{serverTime.Ticks}:{data.TotalVolume}",
					TradePrice = price,
					TradeVolume = data.Volume,
					OriginSide = data.TickType switch { 1 => Sides.Buy, 2 => Sides.Sell, _ => null },
					ServerTime = serverTime,
				}, cancellationToken);
			}
			else if (dataType == DataType.MarketDepth)
			{
				await SendOutMessageAsync(new QuoteChangeMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = serverTime,
					Bids = ToQuotes(data.BidPrice, data.BidVolume, true),
					Asks = ToQuotes(data.AskPrice, data.AskVolume, false),
					State = QuoteChangeStates.SnapshotComplete,
				}, cancellationToken);
			}
			else
			{
				var level1 = new Level1ChangeMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = serverTime,
				}
				.TryAdd(Level1Fields.LastTradePrice, data.Close.ToDecimalValue())
				.TryAdd(Level1Fields.LastTradeTime, data.Close.IsEmpty() ? null : serverTime)
				.TryAdd(Level1Fields.LastTradeVolume, data.Volume)
				.TryAdd(Level1Fields.OpenPrice, data.Open.ToDecimalValue())
				.TryAdd(Level1Fields.HighPrice, data.High.ToDecimalValue())
				.TryAdd(Level1Fields.LowPrice, data.Low.ToDecimalValue())
				.TryAdd(Level1Fields.AveragePrice, data.AveragePrice.ToDecimalValue())
				.TryAdd(Level1Fields.Volume, data.TotalVolume)
				.TryAdd(Level1Fields.Turnover, data.TotalAmount.ToDecimalValue())
				.TryAdd(Level1Fields.Change, data.PriceChange.ToDecimalValue())
				.TryAdd(Level1Fields.BestBidPrice, FirstDecimal(data.BidPrice))
				.TryAdd(Level1Fields.BestBidVolume, At(data.BidVolume, 0))
				.TryAdd(Level1Fields.BestAskPrice, FirstDecimal(data.AskPrice))
				.TryAdd(Level1Fields.BestAskVolume, At(data.AskVolume, 0))
				.TryAdd(Level1Fields.BidsVolume, data.BidSideTotalVolume)
				.TryAdd(Level1Fields.AsksVolume, data.AskSideTotalVolume);
				if (level1.Changes.Count > 0)
					await SendOutMessageAsync(level1, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessIndexEvent(ShioajiIndexEvent data, CancellationToken cancellationToken)
	{
		if (data?.Code.IsEmpty() != false)
			return;
		foreach (var subscription in _marketSubscriptions.Values.Where(subscription =>
			subscription.DataType == DataType.Level1 && subscription.Contract.IsSameInstrument(data.Code, data.Exchange)))
		{
			var time = ShioajiExtensions.ParseTaiwanTime(data.Date, data.Time) ?? CurrentTime;
			var level1 = new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = time,
			}
			.TryAdd(Level1Fields.LastTradePrice, data.Close.ToDecimalValue())
			.TryAdd(Level1Fields.LastTradeTime, data.Close.IsEmpty() ? null : time)
			.TryAdd(Level1Fields.OpenPrice, data.Open.ToDecimalValue())
			.TryAdd(Level1Fields.HighPrice, data.High.ToDecimalValue())
			.TryAdd(Level1Fields.LowPrice, data.Low.ToDecimalValue())
			.TryAdd(Level1Fields.ClosePrice, data.Reference.ToDecimalValue())
			.TryAdd(Level1Fields.Volume, data.VolumeSum)
			.TryAdd(Level1Fields.Turnover, data.AmountSum.ToDecimalValue());
			if (level1.Changes.Count > 0)
				await SendOutMessageAsync(level1, cancellationToken);
		}
	}

	private static SecurityMessage CreateSecurityMessage(ShioajiContract contract, ShioajiContractInfo info,
		long transactionId)
	{
		var securityType = contract.ToSecurityType();
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = contract.ToSecurityId(),
			SecurityType = securityType,
			Name = info?.Name,
			ShortName = contract.Code,
			Class = info?.Category.IsEmpty(contract.Exchange),
			Currency = info?.Currency.ToCurrency() ?? CurrencyTypes.TWD,
			PriceStep = info?.Tick,
			VolumeStep = 1,
			Multiplier = info?.Multiplier ?? info?.Unit,
			ExpiryDate = info?.ToExpiry(),
			Strike = info?.StrikePrice,
			OptionType = info?.ToOptionType(),
			UnderlyingSecurityId = info?.UnderlyingCode.IsEmpty() == false
				? new SecurityId
				{
					SecurityCode = info.UnderlyingCode,
					BoardCode = securityType is SecurityTypes.Future or SecurityTypes.Option ? "TWSE" : contract.Exchange.ToBoardCode(),
				}
				: default,
		};
	}

	private static QuoteChange[] ToQuotes(string[] prices, decimal[] volumes, bool isBids)
	{
		var length = Math.Min(prices?.Length ?? 0, volumes?.Length ?? 0);
		var result = new List<QuoteChange>(length);
		for (var i = 0; i < length; i++)
		{
			if (prices[i].ToDecimalValue() is decimal price && price > 0)
				result.Add(new(price, volumes[i]));
		}
		return [.. (isBids ? result.OrderByDescending(quote => quote.Price) : result.OrderBy(quote => quote.Price))];
	}

	private static decimal? FirstDecimal(string[] values)
		=> values?.FirstOrDefault().ToDecimalValue();

	private static decimal? At(decimal[] values, int index)
		=> values != null && index >= 0 && index < values.Length ? values[index] : null;

	private static int? At(int[] values, int index)
		=> values != null && index >= 0 && index < values.Length ? values[index] : null;
}
