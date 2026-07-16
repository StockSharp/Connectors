namespace StockSharp.CQG;

public partial class CqgMessageAdapter
{
	private readonly SynchronizedDictionary<uint, DateTime> _lastQuoteTimes = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var symbol = lookupMsg.SecurityId.SecurityCode;
		if (symbol.IsEmpty())
			throw new InvalidOperationException("CQG security lookup requires an exact CQG symbol.");
		var contract = await ResolveContract(symbol, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var security = ToSecurityMessage(contract.Metadata, lookupMsg.TransactionId);
		if (security.IsMatch(lookupMsg, securityTypes))
			await SendOutMessageAsync(security, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeMarket(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		var contract = await ResolveContract(mdMsg.SecurityId.ToCqgSymbol(), cancellationToken);
		var subscription = CreateSubscription(mdMsg, DataType.Ticks, contract.Metadata.ContractId);
		_subscriptions[mdMsg.TransactionId] = subscription;
		try
		{
			if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
				await RequestTimeAndSales(subscription, cancellationToken);
			if (!mdMsg.IsHistoryOnly())
				await RefreshMarketSubscription(subscription.Symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			_subscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_subscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var old))
			{
				var drop = new ClientMsg();
				drop.TimeBarRequests.Add(new TimeBarRequest { RequestId = old.RequestId, RequestType = 3 });
				await _client.Send(drop, cancellationToken);
			}
			return;
		}
		var contract = await ResolveContract(mdMsg.SecurityId.ToCqgSymbol(), cancellationToken);
		var subscription = CreateSubscription(mdMsg, mdMsg.DataType2, contract.Metadata.ContractId);
		subscription.RequestId = NextRequestId();
		_subscriptions[mdMsg.TransactionId] = subscription;
		_requestTransactions[subscription.RequestId] = mdMsg.TransactionId;
		try
		{
			await SendTimeBarRequest(subscription, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			_subscriptions.Remove(mdMsg.TransactionId);
			_requestTransactions.Remove(subscription.RequestId);
			throw;
		}
	}

	private async ValueTask ProcessMarketSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeMarket(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		var contract = await ResolveContract(mdMsg.SecurityId.ToCqgSymbol(), cancellationToken);
		var subscription = CreateSubscription(mdMsg, dataType, contract.Metadata.ContractId);
		_subscriptions[mdMsg.TransactionId] = subscription;
		try
		{
			await RefreshMarketSubscription(subscription.Symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			_subscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
	}

	private MarketSubscription CreateSubscription(MarketDataMessage message, DataType dataType, uint contractId)
		=> new()
		{
			TransactionId = message.TransactionId,
			Symbol = message.SecurityId.ToCqgSymbol(),
			SecurityId = message.SecurityId,
			DataType = dataType,
			TimeFrame = dataType.IsTFCandles ? message.GetTimeFrame() : default,
			IsHistoryOnly = message.IsHistoryOnly(),
			From = message.From?.ToUniversalTime(),
			To = message.To?.ToUniversalTime(),
			Count = message.Count,
			ContractId = contractId,
		};

	private async ValueTask UnsubscribeMarket(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (!_subscriptions.TryGetAndRemove(originalTransactionId, out var subscription))
			return;
		if (subscription.RequestId != 0)
			_requestTransactions.Remove(subscription.RequestId);
		if (subscription.DataType == DataType.Ticks && subscription.HistoryRequestId != 0)
		{
			var drop = new ClientMsg();
			drop.TimeAndSalesRequests.Add(new TimeAndSalesRequest
			{
				RequestId = subscription.HistoryRequestId,
				RequestType = 3,
			});
			await _client.Send(drop, cancellationToken);
		}
		await RefreshMarketSubscription(subscription.Symbol, cancellationToken);
	}

	private async Task<ContractRecord> ResolveContract(string symbol, CancellationToken cancellationToken)
	{
		if (_contractsBySymbol.TryGetValue(symbol, out var existing))
			return existing;
		var report = await SendInformationRequest(new InformationRequest
		{
			Id = NextRequestId(),
			Subscribe = true,
			SymbolResolutionRequest = new() { Symbol = symbol },
		}, cancellationToken);
		if (report.StatusCode >= 100 || report.SymbolResolutionReport?.ContractMetadata == null)
			throw new InvalidOperationException($"CQG could not resolve '{symbol}' ({report.StatusCode}): {report.TextMessage}");
		return CacheContract(report.SymbolResolutionReport.ContractMetadata);
	}

	private async Task<InformationReport> SendInformationRequest(InformationRequest request,
		CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<InformationReport>(TaskCreationOptions.RunContinuationsAsynchronously);
		_informationRequests[request.Id] = completion;
		try
		{
			var message = new ClientMsg();
			message.InformationRequests.Add(request);
			await _client.Send(message, cancellationToken);
			return await completion.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			_informationRequests.Remove(request.Id);
		}
	}

	private async ValueTask SendSymbolResolution(string symbol, long? transactionId,
		CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		if (transactionId != null)
			_requestTransactions[id] = transactionId.Value;
		var message = new ClientMsg();
		message.InformationRequests.Add(new InformationRequest
		{
			Id = id,
			Subscribe = true,
			SymbolResolutionRequest = new() { Symbol = symbol },
		});
		await _client.Send(message, cancellationToken);
	}

	private async ValueTask ProcessInformationReport(InformationReport report, CancellationToken cancellationToken)
	{
		if (report.SymbolResolutionReport?.ContractMetadata is { } metadata)
		{
			var contract = CacheContract(metadata);
			foreach (var subscription in _subscriptions.CachedValues.Where(s => s.Symbol.EqualsIgnoreCase(metadata.ContractSymbol)))
				subscription.ContractId = metadata.ContractId;
			if (_subscriptions.CachedValues.Any(s => s.Symbol.EqualsIgnoreCase(metadata.ContractSymbol)))
				await RestoreSymbolSubscriptions(contract, cancellationToken);
		}
		if (report.AccountsReport != null)
			CacheAccounts(report.AccountsReport);
		if (report.HistoricalOrdersReport != null &&
			_historicalOrderRequests.TryGetValue(report.Id, out var historicalTransactionId))
		{
			foreach (var order in report.HistoricalOrdersReport.OrderStatuses)
				await ProcessOrderStatus(order, cancellationToken, historicalTransactionId);
		}
		if (report.StatusCode >= 100 && _historicalOrderRequests.ContainsKey(report.Id))
			await SendOutErrorAsync(new InvalidOperationException(
				$"CQG historical orders failed ({report.StatusCode}): {report.TextMessage}"), cancellationToken);
		if (report.IsReportComplete &&
			_historicalOrderRequests.TryGetAndRemove(report.Id, out historicalTransactionId) &&
			_historyOnlyInformationRequests.Contains(report.Id))
		{
			_historyOnlyInformationRequests.Remove(report.Id);
			await SendSubscriptionFinishedAsync(historicalTransactionId, cancellationToken);
		}
		if (report.IsReportComplete && _informationRequests.TryGetValue(report.Id, out var completion))
			completion.TrySetResult(report);
	}

	private ContractRecord CacheContract(ContractMetadata metadata)
	{
		var record = new ContractRecord { Metadata = metadata, SecurityId = metadata.ToSecurityId() };
		_contractsById[metadata.ContractId] = record;
		_contractsBySymbol[metadata.ContractSymbol] = record;
		if (!metadata.DialectContractSymbol.IsEmpty())
			_contractsBySymbol[metadata.DialectContractSymbol] = record;
		return record;
	}

	private async ValueTask RestoreSymbolSubscriptions(ContractRecord contract, CancellationToken cancellationToken)
	{
		var subscriptions = _subscriptions.CachedValues
			.Where(s => s.Symbol.EqualsIgnoreCase(contract.Metadata.ContractSymbol) ||
				s.Symbol.EqualsIgnoreCase(contract.Metadata.DialectContractSymbol)).ToArray();
		foreach (var subscription in subscriptions)
			subscription.ContractId = contract.Metadata.ContractId;
		if (subscriptions.Any(s => !s.DataType.IsTFCandles &&
			(!s.IsHistoryOnly || s.DataType == DataType.Level1 || s.DataType == DataType.MarketDepth)))
			await RefreshMarketSubscription(contract.Metadata.ContractSymbol, cancellationToken);
		foreach (var subscription in subscriptions.Where(s => s.DataType.IsTFCandles))
		{
			subscription.ContractId = contract.Metadata.ContractId;
			subscription.RequestId = NextRequestId();
			_requestTransactions[subscription.RequestId] = subscription.TransactionId;
			await SendTimeBarRequest(subscription, cancellationToken);
		}
		foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.Ticks &&
			s.HistoryRequestId != 0))
			await RequestTimeAndSales(subscription, cancellationToken);
	}

	private async ValueTask RefreshMarketSubscription(string symbol, CancellationToken cancellationToken)
	{
		if (!_contractsBySymbol.TryGetValue(symbol, out var contract))
			return;
		var subscriptions = _subscriptions.CachedValues.Where(s =>
			(s.ContractId == contract.Metadata.ContractId || s.Symbol.EqualsIgnoreCase(symbol)) &&
			!s.DataType.IsTFCandles &&
			(!s.IsHistoryOnly || s.DataType == DataType.Level1 || s.DataType == DataType.MarketDepth)).ToArray();
		var level = subscriptions.Any(s => s.DataType == DataType.MarketDepth) ? 4u :
			subscriptions.Length > 0 ? 3u : 0u;
		var requestId = NextRequestId();
		if (subscriptions.Length > 0)
		{
			foreach (var subscription in subscriptions)
			{
				subscription.RequestId = requestId;
				subscription.ContractId = contract.Metadata.ContractId;
			}
			_requestTransactions[requestId] = subscriptions[0].TransactionId;
		}
		var request = new MarketDataSubscription
		{
			ContractId = contract.Metadata.ContractId,
			RequestId = requestId,
			Level = level,
			IncludePastQuotes = true,
			IncludeTradeAttributes = true,
		};
		var message = new ClientMsg();
		message.MarketDataSubscriptions.Add(request);
		await _client.Send(message, cancellationToken);
	}

	private async ValueTask ProcessMarketStatus(MarketDataSubscriptionStatus status,
		CancellationToken cancellationToken)
	{
		if (status.StatusCode < 100)
		{
			if (status.StatusCode == 0)
				_requestTransactions.Remove(status.RequestId);
			return;
		}
		_requestTransactions.TryGetValue(status.RequestId, out var transactionId);
		await SendOutErrorAsync(new InvalidOperationException(
			$"CQG market-data subscription failed ({status.StatusCode}): {status.TextMessage}"), cancellationToken);
		if (transactionId != 0)
			_subscriptions.Remove(transactionId);
	}

	private async ValueTask ProcessMarketData(RealTimeMarketData data, CancellationToken cancellationToken)
	{
		if (!_contractsById.TryGetValue(data.ContractId, out var contract))
			return;
		if (data.HasCorrectPriceScale)
			contract.Metadata.CorrectPriceScale = data.CorrectPriceScale;
		var scale = contract.Metadata.CorrectPriceScale;
		var subscriptions = _subscriptions.CachedValues.Where(s => s.ContractId == data.ContractId).ToArray();
		if (subscriptions.Length == 0)
			return;
		var quoteTime = _lastQuoteTimes.TryGetValue(data.ContractId, out var previous) ? previous : DateTime.UtcNow;
		var level1 = new Level1ChangeMessage { SecurityId = contract.SecurityId, ServerTime = quoteTime };
		var hasLevel1 = false;
		var depthChanged = false;
		if (data.IsSnapshot)
		{
			_bids[data.ContractId] = [];
			_asks[data.ContractId] = [];
			depthChanged = true;
		}
		foreach (var quote in data.Quotes)
		{
			if (quote.HasQuoteUtcTime)
				quoteTime = ToServerTime(quote.QuoteUtcTime);
			var price = quote.ScaledPrice.ToPrice(scale);
			var volume = quote.Volume.ToDecimal();
			switch (quote.Type)
			{
				case 0:
					level1.TryAdd(Level1Fields.LastTradePrice, price)
						.TryAdd(Level1Fields.LastTradeVolume, volume);
					hasLevel1 = true;
					foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.Ticks && !s.IsHistoryOnly))
						await SendTick(subscription, quote, quoteTime, scale, cancellationToken);
					break;
				case 1:
					level1.TryAdd(Level1Fields.BestBidPrice, price).TryAdd(Level1Fields.BestBidVolume, volume);
					hasLevel1 = true;
					break;
				case 2:
					level1.TryAdd(Level1Fields.BestAskPrice, price).TryAdd(Level1Fields.BestAskVolume, volume);
					hasLevel1 = true;
					break;
				case 3:
					UpdateDepth(_bids.SafeAdd(data.ContractId, _ => []), price, volume);
					depthChanged = true;
					break;
				case 4:
					UpdateDepth(_asks.SafeAdd(data.ContractId, _ => []), price, volume);
					depthChanged = true;
					break;
				case 5:
					level1.TryAdd(Level1Fields.SettlementPrice, price);
					hasLevel1 = true;
					break;
			}
		}
		foreach (var values in data.MarketValues)
		{
			level1
				.TryAdd(Level1Fields.OpenPrice, values.HasScaledOpenPrice ? values.ScaledOpenPrice.ToPrice(scale) : null)
				.TryAdd(Level1Fields.HighPrice, values.HasScaledHighPrice ? values.ScaledHighPrice.ToPrice(scale) : null)
				.TryAdd(Level1Fields.LowPrice, values.HasScaledLowPrice ? values.ScaledLowPrice.ToPrice(scale) : null)
				.TryAdd(Level1Fields.ClosePrice, values.HasScaledClosePrice ? values.ScaledClosePrice.ToPrice(scale) : null)
				.TryAdd(Level1Fields.Volume, values.TotalVolume?.ToDecimal())
				.TryAdd(Level1Fields.OpenInterest, values.OpenInterest?.ToDecimal());
			hasLevel1 = true;
		}
		_lastQuoteTimes[data.ContractId] = quoteTime;
		if (hasLevel1)
		{
			foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.Level1))
			{
				var output = (Level1ChangeMessage)level1.Clone();
				output.OriginalTransactionId = subscription.TransactionId;
				output.SecurityId = subscription.SecurityId;
				output.ServerTime = quoteTime;
				await SendOutMessageAsync(output, cancellationToken);
			}
		}
		if (depthChanged)
		{
			var bids = _bids.SafeAdd(data.ContractId, _ => []);
			var asks = _asks.SafeAdd(data.ContractId, _ => []);
			foreach (var subscription in subscriptions.Where(s => s.DataType == DataType.MarketDepth))
				await SendOutMessageAsync(new QuoteChangeMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = quoteTime,
					Bids = [.. bids.Values.OrderByDescending(q => q.Price)],
					Asks = [.. asks.Values.OrderBy(q => q.Price)],
					State = QuoteChangeStates.SnapshotComplete,
				}, cancellationToken);
		}
		if (data.IsSnapshot)
		{
			var oneShots = subscriptions.Where(s => s.IsHistoryOnly &&
				(s.DataType == DataType.Level1 || s.DataType == DataType.MarketDepth)).ToArray();
			foreach (var subscription in oneShots)
			{
				_subscriptions.Remove(subscription.TransactionId);
				await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
			}
			if (oneShots.Length > 0)
				await RefreshMarketSubscription(contract.Metadata.ContractSymbol, cancellationToken);
		}
	}

	private static void UpdateDepth(SortedDictionary<decimal, QuoteChange> book, decimal price, decimal volume)
	{
		if (volume == 0)
			book.Remove(price);
		else
			book[price] = new(price, volume);
	}

	private ValueTask SendTick(MarketSubscription subscription, Quote quote, DateTime time, double scale,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			ServerTime = time,
			TradePrice = quote.ScaledPrice.ToPrice(scale),
			TradeVolume = quote.Volume.ToDecimal(),
			OriginSide = quote.SalesCondition switch { 4 => Sides.Buy, 5 => Sides.Sell, _ => null },
		}, cancellationToken);

	private async ValueTask RequestTimeAndSales(MarketSubscription subscription, CancellationToken cancellationToken)
	{
		subscription.HistoryRequestId = NextRequestId();
		_requestTransactions[subscription.HistoryRequestId] = subscription.TransactionId;
		var request = new TimeAndSalesRequest
		{
			RequestId = subscription.HistoryRequestId,
			RequestType = 1,
			TimeAndSalesParameters = new()
			{
				ContractId = subscription.ContractId,
				Level = 1,
				FromUtcTime = (long)((subscription.From ?? DateTime.UtcNow.AddDays(-1)) - _client.BaseTime).TotalMilliseconds,
				IncludeTradeAttributes = true,
			},
		};
		if (subscription.To != null)
			request.TimeAndSalesParameters.ToUtcTime = (long)(subscription.To.Value - _client.BaseTime).TotalMilliseconds;
		var message = new ClientMsg();
		message.TimeAndSalesRequests.Add(request);
		await _client.Send(message, cancellationToken);
	}

	private async ValueTask ProcessTimeAndSales(TimeAndSalesReport report, CancellationToken cancellationToken)
	{
		if (!_requestTransactions.TryGetValue(report.RequestId, out var transactionId) ||
			!_subscriptions.TryGetValue(transactionId, out var subscription) ||
			!_contractsById.TryGetValue(subscription.ContractId, out var contract))
			return;
		if (report.ResultCode >= 100)
		{
			await SendOutErrorAsync(new InvalidOperationException($"CQG time and sales failed ({report.ResultCode}): {report.TextMessage}"), cancellationToken);
			_requestTransactions.Remove(report.RequestId);
			subscription.HistoryRequestId = 0;
			if (subscription.IsHistoryOnly)
			{
				_subscriptions.Remove(subscription.TransactionId);
				await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
			}
			return;
		}
		var time = subscription.From ?? DateTime.UtcNow;
		foreach (var quote in report.Quotes.Where(q => q.Type == 0))
		{
			if (quote.HasQuoteUtcTime)
				time = ToServerTime(quote.QuoteUtcTime);
			await SendTick(subscription, quote, time, contract.Metadata.CorrectPriceScale, cancellationToken);
		}
		if (report.IsReportComplete)
		{
			_requestTransactions.Remove(report.RequestId);
			subscription.HistoryRequestId = 0;
			if (subscription.IsHistoryOnly)
			{
				_subscriptions.Remove(subscription.TransactionId);
				await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
			}
		}
	}

	private async ValueTask SendTimeBarRequest(MarketSubscription subscription, CancellationToken cancellationToken)
	{
		var (unit, count) = subscription.TimeFrame.ToBarUnit();
		var parameters = new TimeBarParameters
		{
			ContractId = subscription.ContractId,
			BarUnit = unit,
			UnitNumber = count,
		};
		if (subscription.From != null)
			parameters.FromUtcTime = (long)(subscription.From.Value - _client.BaseTime).TotalMilliseconds;
		else
			parameters.BarCount = (uint)Math.Clamp(subscription.Count ?? 1000, 1, 100000);
		if (subscription.IsHistoryOnly && subscription.To != null)
			parameters.ToUtcTime = (long)(subscription.To.Value - _client.BaseTime).TotalMilliseconds;
		var request = new TimeBarRequest
		{
			RequestId = subscription.RequestId,
			RequestType = subscription.IsHistoryOnly ? 1u : 2u,
			TimeBarParameters = parameters,
		};
		var message = new ClientMsg();
		message.TimeBarRequests.Add(request);
		await _client.Send(message, cancellationToken);
	}

	private async ValueTask ProcessTimeBars(TimeBarReport report, CancellationToken cancellationToken)
	{
		if (!_requestTransactions.TryGetValue(report.RequestId, out var transactionId) ||
			!_subscriptions.TryGetValue(transactionId, out var subscription) ||
			!_contractsById.TryGetValue(subscription.ContractId, out var contract))
			return;
		if (report.StatusCode >= 100)
		{
			await SendOutErrorAsync(new InvalidOperationException($"CQG time bars failed ({report.StatusCode}): {report.TextMessage}"), cancellationToken);
			_requestTransactions.Remove(report.RequestId);
			_subscriptions.Remove(subscription.TransactionId);
			await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
			return;
		}
		foreach (var bar in report.TimeBars.OrderBy(b => b.BarUtcTime))
		{
			var scale = contract.Metadata.CorrectPriceScale;
			if (!bar.HasScaledOpenPrice || !bar.HasScaledHighPrice || !bar.HasScaledLowPrice || !bar.HasScaledClosePrice)
				continue;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				TypedArg = subscription.TimeFrame,
				OpenTime = ToServerTime(bar.BarUtcTime),
				OpenPrice = bar.ScaledOpenPrice.ToPrice(scale),
				HighPrice = bar.ScaledHighPrice.ToPrice(scale),
				LowPrice = bar.ScaledLowPrice.ToPrice(scale),
				ClosePrice = bar.ScaledClosePrice.ToPrice(scale),
				TotalVolume = bar.Volume?.ToDecimal() ?? 0,
				OpenInterest = bar.OpenInterest?.ToDecimal(),
				State = report.StatusCode == 3 && report.TimeBars.Last() == bar ? CandleStates.Active : CandleStates.Finished,
			}, cancellationToken);
		}
		if (report.IsReportComplete && subscription.IsHistoryOnly)
		{
			_requestTransactions.Remove(report.RequestId);
			_subscriptions.Remove(subscription.TransactionId);
			await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
		}
	}

	private SecurityMessage ToSecurityMessage(ContractMetadata metadata, long transactionId)
	{
		var securityType = metadata.ToSecurityType();
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = metadata.ToSecurityId(),
			SecurityType = securityType,
			Name = metadata.Description.IsEmpty(metadata.Title),
			ShortName = metadata.Title.IsEmpty(metadata.ContractSymbol),
			Currency = System.Enum.TryParse<CurrencyTypes>(metadata.Currency, true, out var currency) ? currency : null,
			PriceStep = (decimal)metadata.TickSize,
			Multiplier = metadata.TickSize > 0 && metadata.TickValue > 0
				? (decimal)(metadata.TickValue / metadata.TickSize) : null,
			ExpiryDate = metadata.HasExpirationDate ? ToServerTime(metadata.ExpirationDate) : null,
			Strike = metadata.HasStrikePrice ? (decimal)metadata.StrikePrice : null,
			OptionType = securityType == SecurityTypes.Option
				? metadata.CfiCode.Length > 1 && char.ToUpperInvariant(metadata.CfiCode[1]) == 'C' ? OptionTypes.Call : OptionTypes.Put
				: null,
			UnderlyingSecurityId = metadata.UnderlyingContractSymbol.IsEmpty()
				? default : new() { SecurityCode = metadata.UnderlyingContractSymbol, BoardCode = metadata.Mic.IsEmpty("CQG") },
		};
	}
}
