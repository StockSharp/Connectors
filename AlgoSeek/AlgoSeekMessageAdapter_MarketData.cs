namespace StockSharp.AlgoSeek;

public partial class AlgoSeekMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var types = lookupMsg.GetSecurityTypes();
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		var exact = AlgoSeekSecurityKey.TryParse(lookupMsg.SecurityId.Native as string,
			out var exactKey);
		var sources = GetLookupSources(lookupMsg, exact, exactKey);

		async ValueTask Emit(AlgoSeekSecurityKey key)
		{
			if (left <= 0 || exact && !exactKey.Matches(key))
				return;
			var native = key.ToNative();
			if (!emitted.Add(native))
				return;
			var message = key.ToSecurityMessage(lookupMsg.TransactionId);
			var criteria = lookupMsg;
			if (exact)
			{
				criteria = (SecurityLookupMessage)lookupMsg.Clone();
				criteria.SecurityId = message.SecurityId;
			}
			if (!message.IsMatch(criteria, types))
				return;
			if (skip > 0)
			{
				skip--;
				return;
			}
			await SendOutMessageAsync(message, cancellationToken);
			left--;
		}

		foreach (var source in sources)
		{
			if (left <= 0)
				break;
			await using var file = await SafeCatalog().OpenAsync(source, cancellationToken);
			if (file.Header == null || file.Header.Kind == AlgoSeekFileKinds.Unknown)
				continue;

			switch (file.Header.Kind)
			{
				case AlgoSeekFileKinds.EquityTick:
					await foreach (var row in file.Reader.ReadRowsAsync(cancellationToken))
					{
						await Emit(AlgoSeekRowParser.ParseEquityTick(file.Header, row,
							source.DisplayName).ToKey());
						break;
					}
					break;

				case AlgoSeekFileKinds.EquityMinute:
					await foreach (var row in file.Reader.ReadRowsAsync(cancellationToken))
					{
						await Emit(AlgoSeekRowParser.ParseEquityMinute(file.Header, row,
							source.DisplayName).ToKey());
						break;
					}
					break;

				case AlgoSeekFileKinds.EquityDaily:
					await foreach (var row in file.Reader.ReadRowsAsync(cancellationToken))
					{
						await Emit(AlgoSeekRowParser.ParseEquityDaily(file.Header, row,
							source.DisplayName).ToKey());
						if (left <= 0)
							break;
					}
					break;

				case AlgoSeekFileKinds.OptionTick:
				{
					var first = true;
					var isOpenInterestFile = false;
					await foreach (var row in file.Reader.ReadRowsAsync(cancellationToken))
					{
						var value = AlgoSeekRowParser.ParseOptionTick(file.Header, row,
							source.DisplayName);
						if (first)
						{
							first = false;
							isOpenInterestFile = value.IsOpenInterest;
						}
						await Emit(value.ToKey());
						if (left <= 0 || !isOpenInterestFile)
							break;
					}
					break;
				}

				case AlgoSeekFileKinds.OptionMinute:
					await foreach (var row in file.Reader.ReadRowsAsync(cancellationToken))
					{
						await Emit(AlgoSeekRowParser.ParseOptionMinute(file.Header, row,
							source.DisplayName).ToKey());
						if (left <= 0)
							break;
					}
					break;

				case AlgoSeekFileKinds.FuturesTick:
					await foreach (var row in file.Reader.ReadRowsAsync(cancellationToken))
					{
						await Emit(AlgoSeekRowParser.ParseFuturesTick(file.Header, row,
							source.DisplayName).ToKey());
						break;
					}
					break;
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
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
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var key = mdMsg.SecurityId.GetAlgoSeekKey();
		var securityId = mdMsg.SecurityId.NormalizeAlgoSeek(key);
		var left = mdMsg.Count ?? long.MaxValue;
		foreach (var source in GetRequestSources(mdMsg, key))
		{
			await using var file = await SafeCatalog().OpenAsync(source, cancellationToken);
			if (file.Header == null)
				continue;
			if (key.Market == AlgoSeekMarkets.Stocks &&
				file.Header.Kind == AlgoSeekFileKinds.EquityTick)
			{
				await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
				{
					var row = AlgoSeekRowParser.ParseEquityTick(file.Header, record,
						source.DisplayName);
					if (!key.Matches(row.ToKey()) || !row.IsTrade || row.Price <= 0 ||
						row.Quantity < 0)
					{
						continue;
					}
					var time = Extensions.CombineMarketTime(row.Date, row.Timestamp,
						_marketTimeZone);
					if (!Extensions.InRange(time, mdMsg))
						continue;
					await SendOutMessageAsync(new ExecutionMessage
					{
						OriginalTransactionId = mdMsg.TransactionId,
						SecurityId = securityId,
						DataTypeEx = DataType.Ticks,
						ServerTime = time,
						TradePrice = row.Price,
						TradeVolume = row.Quantity,
						TradeStatus = ParseHex(row.Conditions),
						IsCancellation = row.IsCancellation,
					}, cancellationToken);
					if (--left <= 0)
						break;
				}
			}
			else if (key.Market == AlgoSeekMarkets.Options &&
				file.Header.Kind == AlgoSeekFileKinds.OptionTick)
			{
				await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
				{
					var row = AlgoSeekRowParser.ParseOptionTick(file.Header, record,
						source.DisplayName);
					if (!key.Matches(row.ToKey()) || !row.IsTrade || row.Price <= 0 ||
						row.Quantity < 0)
					{
						continue;
					}
					var time = Extensions.CombineMarketTime(row.Date, row.Timestamp,
						_marketTimeZone);
					if (!Extensions.InRange(time, mdMsg))
						continue;
					await SendOutMessageAsync(new ExecutionMessage
					{
						OriginalTransactionId = mdMsg.TransactionId,
						SecurityId = securityId,
						DataTypeEx = DataType.Ticks,
						ServerTime = time,
						TradePrice = row.Price,
						TradeVolume = row.Quantity,
						IsCancellation = row.IsCancellation,
					}, cancellationToken);
					if (--left <= 0)
						break;
				}
			}
			else if (key.Market is AlgoSeekMarkets.Futures or
				AlgoSeekMarkets.FutureOptions &&
				file.Header.Kind == AlgoSeekFileKinds.FuturesTick)
			{
				await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
				{
					var row = AlgoSeekRowParser.ParseFuturesTick(file.Header, record,
						source.DisplayName);
					if (!key.Matches(row.ToKey()) || !row.IsTrade || row.Price <= 0 ||
						row.Quantity < 0 || (row.Flags & 8) != 0)
					{
						continue;
					}
					var time = Extensions.CombineUtcTime(row.UtcDate, row.UtcTime);
					if (!Extensions.InRange(time, mdMsg))
						continue;
					await SendOutMessageAsync(new ExecutionMessage
					{
						OriginalTransactionId = mdMsg.TransactionId,
						SecurityId = securityId,
						DataTypeEx = DataType.Ticks,
						ServerTime = time,
						TradePrice = row.Price,
						TradeVolume = row.Quantity,
						TradeStatus = row.Flags,
						OriginSide = row.OriginSide,
					}, cancellationToken);
					if (--left <= 0)
						break;
				}
			}
			if (left <= 0)
				break;
		}

		await Complete(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
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
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var key = mdMsg.SecurityId.GetAlgoSeekKey();
		var securityId = mdMsg.SecurityId.NormalizeAlgoSeek(key);
		var left = mdMsg.Count ?? long.MaxValue;
		foreach (var source in GetRequestSources(mdMsg, key))
		{
			await using var file = await SafeCatalog().OpenAsync(source, cancellationToken);
			if (file.Header == null)
				continue;
			await foreach (var message in ReadLevel1(file, source, key, securityId,
				mdMsg.TransactionId, cancellationToken))
			{
				if (!Extensions.InRange(message.ServerTime, mdMsg))
					continue;
				await SendOutMessageAsync(message, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
				break;
		}

		await Complete(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
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
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var key = mdMsg.SecurityId.GetAlgoSeekKey();
		var securityId = mdMsg.SecurityId.NormalizeAlgoSeek(key);
		var left = mdMsg.Count ?? long.MaxValue;
		var bid = default(AlgoSeekQuoteValue?);
		var ask = default(AlgoSeekQuoteValue?);
		var venueBook = new AlgoSeekVenueQuoteBook();
		DateTime? tradeDate = null;

		foreach (var source in GetRequestSources(mdMsg, key))
		{
			await using var file = await SafeCatalog().OpenAsync(source, cancellationToken);
			if (file.Header == null)
				continue;
			await foreach (var update in ReadDepthUpdates(file, source, key,
				cancellationToken))
			{
				if (!Extensions.InRange(update.Time, mdMsg))
					continue;
				if (tradeDate != update.TradeDate)
				{
					tradeDate = update.TradeDate;
					bid = ask = null;
					venueBook.Clear();
				}
				if (update.IsEmptyBook)
					bid = ask = null;
				else if (update.IsVenueQuote)
				{
					venueBook.Update(update);
					bid = venueBook.BestBid;
					ask = venueBook.BestAsk;
				}
				else if (update.Side == Sides.Buy)
					bid = update.Price > 0 ? new(update.Price, update.Volume) : null;
				else
					ask = update.Price > 0 ? new(update.Price, update.Volume) : null;

				await SendOutMessageAsync(new QuoteChangeMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					ServerTime = update.Time,
					Bids = bid is { } bestBid
						? [new QuoteChange(bestBid.Price, bestBid.Volume)] : [],
					Asks = ask is { } bestAsk
						? [new QuoteChange(bestAsk.Price, bestAsk.Volume)] : [],
					State = QuoteChangeStates.SnapshotComplete,
				}, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
				break;
		}

		await Complete(mdMsg, cancellationToken);
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
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (!Extensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException("AlgoSeek supports one-minute and one-day delivery bars.");
		var key = mdMsg.SecurityId.GetAlgoSeekKey();
		if (key.Market is AlgoSeekMarkets.Futures or AlgoSeekMarkets.FutureOptions ||
			timeFrame == TimeSpan.FromDays(1) && key.Market != AlgoSeekMarkets.Stocks)
		{
			throw new NotSupportedException(
				"The supported AlgoSeek file schemas provide minute bars for stocks and options, and daily bars for stocks.");
		}
		var securityId = mdMsg.SecurityId.NormalizeAlgoSeek(key);
		var left = mdMsg.Count ?? long.MaxValue;
		foreach (var source in GetRequestSources(mdMsg, key))
		{
			await using var file = await SafeCatalog().OpenAsync(source, cancellationToken);
			if (file.Header == null)
				continue;
			await foreach (var candle in ReadCandles(file, source, key, securityId,
				mdMsg, timeFrame, cancellationToken))
			{
				if (!Extensions.InRange(candle.OpenTime, mdMsg))
					continue;
				await SendOutMessageAsync(candle, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
				break;
		}

		await Complete(mdMsg, cancellationToken);
	}

	private IEnumerable<AlgoSeekDataSource> GetLookupSources(SecurityLookupMessage message,
		bool exact, AlgoSeekSecurityKey exactKey)
	{
		if (exact)
			return SafeCatalog().GetCandidates(exactKey, null, null);
		var code = message.SecurityId.SecurityCode.IsEmpty(message.Name)?.Trim();
		if (code.IsEmpty())
			return SafeCatalog().Sources;
		var market = message.SecurityId.BoardCode.ToAlgoSeekMarket();
		if (market is AlgoSeekMarkets.Options or AlgoSeekMarkets.FutureOptions)
			code = code.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
		return SafeCatalog().GetCandidates(new(market, code, null, null, null, null),
			null, null);
	}

	private IEnumerable<AlgoSeekDataSource> GetRequestSources(MarketDataMessage message,
		AlgoSeekSecurityKey key)
	{
		DateTime? from;
		DateTime? to;
		if (key.Market is AlgoSeekMarkets.Futures or AlgoSeekMarkets.FutureOptions)
		{
			from = message.From?.ToUtc().Date;
			to = message.To?.ToUtc().Date;
		}
		else
		{
			from = message.From?.ToMarketDate(_marketTimeZone);
			to = message.To?.ToMarketDate(_marketTimeZone);
		}
		if (from != null && to != null && from > to)
			throw new ArgumentOutOfRangeException(nameof(message.From), message.From,
				"AlgoSeek history start time is after its end time.");
		return SafeCatalog().GetCandidates(key, from, to);
	}

	private async IAsyncEnumerable<Level1ChangeMessage> ReadLevel1(
		AlgoSeekDataFile file, AlgoSeekDataSource source, AlgoSeekSecurityKey key,
		SecurityId securityId, long transactionId,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (key.Market == AlgoSeekMarkets.Stocks &&
			file.Header.Kind == AlgoSeekFileKinds.EquityTick)
		{
			var venueBook = new AlgoSeekVenueQuoteBook();
			DateTime? tradeDate = null;
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseEquityTick(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()))
					continue;
				var isQuote = IsEligibleEquityQuote(row);
				if (!row.IsTrade && !isQuote)
					continue;
				var time = Extensions.CombineMarketTime(row.Date, row.Timestamp,
					_marketTimeZone);
				var message = new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					ServerTime = time,
				};
				if (row.IsTrade && !row.IsCancellation && row.Price > 0)
				{
					message
						.TryAdd(Level1Fields.LastTradePrice, row.Price)
						.TryAdd(Level1Fields.LastTradeVolume, NonNegative(row.Quantity))
						.TryAdd(Level1Fields.LastTradeTime, time);
				}
				else if (!IsNationalBestQuotesOnly)
				{
					if (tradeDate != row.Date)
					{
						tradeDate = row.Date;
						venueBook.Clear();
					}
					venueBook.Update(new(row.Date, time,
						row.IsBid ? Sides.Buy : Sides.Sell, row.Price,
						Math.Max(0, row.Quantity), row.Exchange, true, false));
					if (venueBook.BestBid is { } bestBid)
					{
						message
							.TryAdd(Level1Fields.BestBidPrice, bestBid.Price)
							.TryAdd(Level1Fields.BestBidVolume, bestBid.Volume)
							.TryAdd(Level1Fields.BestBidTime, time);
					}
					if (venueBook.BestAsk is { } bestAsk)
					{
						message
							.TryAdd(Level1Fields.BestAskPrice, bestAsk.Price)
							.TryAdd(Level1Fields.BestAskVolume, bestAsk.Volume)
							.TryAdd(Level1Fields.BestAskTime, time);
					}
				}
				else if (row.IsBid)
				{
					message
						.TryAdd(Level1Fields.BestBidPrice, NonNegative(row.Price))
						.TryAdd(Level1Fields.BestBidVolume, NonNegative(row.Quantity))
						.TryAdd(Level1Fields.BestBidTime, time);
				}
				else if (row.IsAsk)
				{
					message
						.TryAdd(Level1Fields.BestAskPrice, NonNegative(row.Price))
						.TryAdd(Level1Fields.BestAskVolume, NonNegative(row.Quantity))
						.TryAdd(Level1Fields.BestAskTime, time);
				}
				if (message.Changes.Count > 0)
					yield return message;
			}
		}
		else if (key.Market == AlgoSeekMarkets.Options &&
			file.Header.Kind == AlgoSeekFileKinds.OptionTick)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseOptionTick(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()))
					continue;
				var time = Extensions.CombineMarketTime(row.Date, row.Timestamp,
					_marketTimeZone);
				var message = new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					ServerTime = time,
				};
				if (row.IsTrade && !row.IsCancellation && row.Price > 0)
				{
					message
						.TryAdd(Level1Fields.LastTradePrice, row.Price)
						.TryAdd(Level1Fields.LastTradeVolume, NonNegative(row.Quantity))
						.TryAdd(Level1Fields.LastTradeTime, time);
				}
				else if (row.IsOpenInterest)
					message.TryAdd(Level1Fields.OpenInterest, NonNegative(row.Quantity));
				else if (row.IsQuote && row.IsBid)
				{
					message
						.TryAdd(Level1Fields.BestBidPrice, NonNegative(row.Price))
						.TryAdd(Level1Fields.BestBidVolume, NonNegative(row.Quantity))
						.TryAdd(Level1Fields.BestBidTime, time);
				}
				else if (row.IsQuote && row.IsAsk)
				{
					message
						.TryAdd(Level1Fields.BestAskPrice, NonNegative(row.Price))
						.TryAdd(Level1Fields.BestAskVolume, NonNegative(row.Quantity))
						.TryAdd(Level1Fields.BestAskTime, time);
				}
				if (message.Changes.Count > 0)
					yield return message;
			}
		}
		else if (key.Market == AlgoSeekMarkets.Options &&
			file.Header.Kind == AlgoSeekFileKinds.OptionMinute)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseOptionMinute(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()))
					continue;
				var time = Extensions.CombineMarketTime(row.Date,
					row.CloseAskTime.IsEmpty(row.CloseBidTime).IsEmpty(row.TimeBarStart),
					_marketTimeZone);
				var message = new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					ServerTime = time,
				}
				.TryAdd(Level1Fields.BestBidPrice, Positive(row.CloseBidPrice))
				.TryAdd(Level1Fields.BestBidVolume, NonNegative(row.CloseBidSize))
				.TryAdd(Level1Fields.BestAskPrice, Positive(row.CloseAskPrice))
				.TryAdd(Level1Fields.BestAskVolume, NonNegative(row.CloseAskSize))
				.TryAdd(Level1Fields.LastTradePrice, Positive(row.CloseTradePrice))
				.TryAdd(Level1Fields.Volume, NonNegative(row.Volume));
				if (message.Changes.Count > 0)
					yield return message;
			}
		}
		else if (key.Market is AlgoSeekMarkets.Futures or
			AlgoSeekMarkets.FutureOptions &&
			file.Header.Kind == AlgoSeekFileKinds.FuturesTick)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseFuturesTick(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()))
					continue;
				var time = Extensions.CombineUtcTime(row.UtcDate, row.UtcTime);
				var message = new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					ServerTime = time,
				};
				if (row.IsTrade && (row.Flags & 8) == 0 && row.Price > 0)
				{
					message
						.TryAdd(Level1Fields.LastTradePrice, row.Price)
						.TryAdd(Level1Fields.LastTradeVolume, NonNegative(row.Quantity))
						.TryAdd(Level1Fields.LastTradeTime, time);
				}
				else if (row.IsOpenInterest)
					message.TryAdd(Level1Fields.OpenInterest, NonNegative(row.Quantity));
				else if (row.IsBid)
				{
					message
						.TryAdd(Level1Fields.BestBidPrice, NonNegative(row.Price))
						.TryAdd(Level1Fields.BestBidVolume, NonNegative(row.Quantity))
						.TryAdd(Level1Fields.BestBidTime, time);
				}
				else if (row.IsAsk)
				{
					message
						.TryAdd(Level1Fields.BestAskPrice, NonNegative(row.Price))
						.TryAdd(Level1Fields.BestAskVolume, NonNegative(row.Quantity))
						.TryAdd(Level1Fields.BestAskTime, time);
				}
				if (message.Changes.Count > 0)
					yield return message;
			}
		}
		else if (key.Market == AlgoSeekMarkets.Stocks &&
			file.Header.Kind == AlgoSeekFileKinds.EquityMinute)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseEquityMinute(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()))
					continue;
				var time = Extensions.CombineMarketTime(row.Date, row.TimeBarStart,
					_marketTimeZone) + TimeSpan.FromMinutes(1);
				yield return new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					ServerTime = time,
				}
				.TryAdd(Level1Fields.OpenPrice, row.Open)
				.TryAdd(Level1Fields.HighPrice, row.High)
				.TryAdd(Level1Fields.LowPrice, row.Low)
				.TryAdd(Level1Fields.ClosePrice, row.Close)
				.TryAdd(Level1Fields.LastTradePrice, row.Close)
				.TryAdd(Level1Fields.Volume, NonNegative(row.Volume));
			}
		}
		else if (key.Market == AlgoSeekMarkets.Stocks &&
			file.Header.Kind == AlgoSeekFileKinds.EquityDaily)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseEquityDaily(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()))
					continue;
				var time = Extensions.CombineMarketTime(row.Date, "16:00:00",
					_marketTimeZone);
				yield return new Level1ChangeMessage
				{
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					ServerTime = time,
				}
				.TryAdd(Level1Fields.OpenPrice, row.Open)
				.TryAdd(Level1Fields.HighPrice, row.High)
				.TryAdd(Level1Fields.LowPrice, row.Low)
				.TryAdd(Level1Fields.ClosePrice, row.Close)
				.TryAdd(Level1Fields.LastTradePrice, row.Close)
				.TryAdd(Level1Fields.Volume, NonNegative(row.Volume));
			}
		}
	}

	private async IAsyncEnumerable<AlgoSeekDepthUpdate> ReadDepthUpdates(
		AlgoSeekDataFile file, AlgoSeekDataSource source, AlgoSeekSecurityKey key,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (key.Market == AlgoSeekMarkets.Stocks &&
			file.Header.Kind == AlgoSeekFileKinds.EquityTick)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseEquityTick(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()) || !IsEligibleEquityQuote(row))
					continue;
				yield return new(row.Date,
					Extensions.CombineMarketTime(row.Date, row.Timestamp, _marketTimeZone),
					row.IsBid ? Sides.Buy : Sides.Sell, row.Price,
					Math.Max(0, row.Quantity), row.Exchange,
					!IsNationalBestQuotesOnly, false);
			}
		}
		else if (key.Market == AlgoSeekMarkets.Options &&
			file.Header.Kind == AlgoSeekFileKinds.OptionTick)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseOptionTick(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()) || !row.IsQuote || !row.IsBid && !row.IsAsk)
					continue;
				yield return new(row.Date,
					Extensions.CombineMarketTime(row.Date, row.Timestamp, _marketTimeZone),
					row.IsBid ? Sides.Buy : Sides.Sell, row.Price,
					Math.Max(0, row.Quantity), row.Exchange, false, false);
			}
		}
		else if (key.Market == AlgoSeekMarkets.Options &&
			file.Header.Kind == AlgoSeekFileKinds.OptionMinute)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseOptionMinute(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()))
					continue;
				if (row.CloseBidPrice != null)
				{
					yield return new(row.Date,
						Extensions.CombineMarketTime(row.Date,
							row.CloseBidTime.IsEmpty(row.TimeBarStart), _marketTimeZone),
						Sides.Buy, row.CloseBidPrice.Value,
						Math.Max(0, row.CloseBidSize ?? 0), null, false, false);
				}
				if (row.CloseAskPrice != null)
				{
					yield return new(row.Date,
						Extensions.CombineMarketTime(row.Date,
							row.CloseAskTime.IsEmpty(row.TimeBarStart), _marketTimeZone),
						Sides.Sell, row.CloseAskPrice.Value,
						Math.Max(0, row.CloseAskSize ?? 0), null, false, false);
				}
			}
		}
		else if (key.Market is AlgoSeekMarkets.Futures or
			AlgoSeekMarkets.FutureOptions &&
			file.Header.Kind == AlgoSeekFileKinds.FuturesTick)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseFuturesTick(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()) || !row.IsQuote && !row.IsEmptyBook)
					continue;
				yield return new(row.UtcDate,
					Extensions.CombineUtcTime(row.UtcDate, row.UtcTime),
					row.IsBid ? Sides.Buy : Sides.Sell, row.Price,
					Math.Max(0, row.Quantity), null, false, row.IsEmptyBook);
			}
		}
	}

	private async IAsyncEnumerable<TimeFrameCandleMessage> ReadCandles(
		AlgoSeekDataFile file, AlgoSeekDataSource source, AlgoSeekSecurityKey key,
		SecurityId securityId, MarketDataMessage request, TimeSpan timeFrame,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (timeFrame == TimeSpan.FromMinutes(1) && key.Market == AlgoSeekMarkets.Stocks &&
			file.Header.Kind == AlgoSeekFileKinds.EquityMinute)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseEquityMinute(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()))
					continue;
				var openTime = Extensions.CombineMarketTime(row.Date, row.TimeBarStart,
					_marketTimeZone);
				yield return new TimeFrameCandleMessage
				{
					OriginalTransactionId = request.TransactionId,
					SecurityId = securityId,
					DataType = request.DataType2,
					OpenTime = openTime,
					CloseTime = openTime + timeFrame,
					OpenPrice = row.Open,
					HighPrice = row.High,
					LowPrice = row.Low,
					ClosePrice = row.Close,
					TotalVolume = Math.Max(0, row.Volume),
					TotalTicks = ToInt(row.TotalTrades),
					State = CandleStates.Finished,
				};
			}
		}
		else if (timeFrame == TimeSpan.FromMinutes(1) &&
			key.Market == AlgoSeekMarkets.Options &&
			file.Header.Kind == AlgoSeekFileKinds.OptionMinute)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseOptionMinute(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()) || row.OpenTradePrice is not > 0 ||
					row.HighTradePrice is not > 0 || row.LowTradePrice is not > 0 ||
					row.CloseTradePrice is not > 0)
				{
					continue;
				}
				var openTime = Extensions.CombineMarketTime(row.Date, row.TimeBarStart,
					_marketTimeZone);
				yield return new TimeFrameCandleMessage
				{
					OriginalTransactionId = request.TransactionId,
					SecurityId = securityId,
					DataType = request.DataType2,
					OpenTime = openTime,
					CloseTime = openTime + timeFrame,
					OpenPrice = row.OpenTradePrice.Value,
					HighPrice = row.HighTradePrice.Value,
					LowPrice = row.LowTradePrice.Value,
					ClosePrice = row.CloseTradePrice.Value,
					TotalVolume = Math.Max(0, row.Volume),
					TotalTicks = ToInt(row.TotalTrades),
					State = CandleStates.Finished,
				};
			}
		}
		else if (timeFrame == TimeSpan.FromDays(1) &&
			key.Market == AlgoSeekMarkets.Stocks &&
			file.Header.Kind == AlgoSeekFileKinds.EquityDaily)
		{
			await foreach (var record in file.Reader.ReadRowsAsync(cancellationToken))
			{
				var row = AlgoSeekRowParser.ParseEquityDaily(file.Header, record,
					source.DisplayName);
				if (!key.Matches(row.ToKey()))
					continue;
				var openTime = Extensions.CombineMarketTime(row.Date, "09:30:00",
					_marketTimeZone);
				yield return new TimeFrameCandleMessage
				{
					OriginalTransactionId = request.TransactionId,
					SecurityId = securityId,
					DataType = request.DataType2,
					OpenTime = openTime,
					CloseTime = Extensions.CombineMarketTime(row.Date, "16:00:00",
						_marketTimeZone),
					OpenPrice = row.Open,
					HighPrice = row.High,
					LowPrice = row.Low,
					ClosePrice = row.Close,
					TotalVolume = Math.Max(0, row.Volume),
					State = CandleStates.Finished,
				};
			}
		}
	}

	private bool IsEligibleEquityQuote(AlgoSeekEquityTickRow row)
		=> (row.IsBid || row.IsAsk) &&
			(IsNationalBestQuotesOnly ? row.IsNationalBest : !row.IsNationalBest);

	private async ValueTask Complete(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}

	private static long? ParseHex(string value)
		=> long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
			out var result) ? result : null;

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static decimal? NonNegative(decimal? value)
		=> value is >= 0 ? value : null;

	private static decimal? NonNegative(long? value)
		=> value is >= 0 ? value.Value : null;

	private static int? ToInt(long? value)
		=> value is >= 0 and <= int.MaxValue ? (int)value.Value : null;
}

readonly record struct AlgoSeekQuoteValue(decimal Price, decimal Volume);

readonly record struct AlgoSeekDepthUpdate(
	DateTime TradeDate,
	DateTime Time,
	Sides Side,
	decimal Price,
	decimal Volume,
	string Venue,
	bool IsVenueQuote,
	bool IsEmptyBook);

sealed class AlgoSeekVenueQuoteBook
{
	private readonly Dictionary<string, AlgoSeekQuoteValue> _bids =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, AlgoSeekQuoteValue> _asks =
		new(StringComparer.OrdinalIgnoreCase);

	public AlgoSeekQuoteValue? BestBid => _bids.Count == 0 ? null :
		_bids.Values.MaxBy(value => value.Price);
	public AlgoSeekQuoteValue? BestAsk => _asks.Count == 0 ? null :
		_asks.Values.MinBy(value => value.Price);

	public void Update(AlgoSeekDepthUpdate update)
	{
		var venue = update.Venue.IsEmpty("UNKNOWN");
		var quotes = update.Side == Sides.Buy ? _bids : _asks;
		if (update.Price > 0)
			quotes[venue] = new(update.Price, update.Volume);
		else
			quotes.Remove(venue);
	}

	public void Clear()
	{
		_bids.Clear();
		_asks.Clear();
	}
}
