namespace StockSharp.Oanda;

partial class OandaMessageAdapter
{
	private string _defaultAccount;
	private bool _isPositionsStreaming;

	private readonly SynchronizedDictionary<string, SynchronizedDictionary<SecurityId, RefPair<decimal, decimal?>>> _currentPositions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<long, decimal> _orderBalance = new();
	private readonly SynchronizedSet<string> _pfSubs = new(StringComparer.InvariantCultureIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		await _restClient.SendOrderCommand(regMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		//if (message.OrderId == null)
		//	throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OrderTransactionId));

		await _restClient.CancelOrderAsync(cancelMsg.TransactionId, cancelMsg.PortfolioName, cancelMsg.OrderId.CreateOrderSpecifier(cancelMsg.OriginalTransactionId), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		//if (message.OldOrderId == null)
		//	throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OldTransactionId));

		await _restClient.SendOrderCommand(replaceMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		var accountId = statusMsg.PortfolioName ?? GetDefaultAccount();
		var pages = await _restClient.GetTransactionPagesAsync(accountId, DateTime.Today.UtcKind().ToUnixStr(), null, cancellationToken);

		foreach (var page in pages)
		{
			foreach (var transaction in await _restClient.GetTransactionsAsync(page.To<Uri>(), cancellationToken))
			{
				await ProcessTransaction(transaction, statusMsg.TransactionId, cancellationToken);
			}
		}

		_isPositionsStreaming = true;

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
		{
			foreach (var accountId in _pfSubs.CopyAndClear())
				_streamigClient.UnSubscribeTransactionsStreaming(accountId);

			return;
		}

		foreach (var account in await _restClient.GetAccountsAsync(cancellationToken))
		{
			//_accountIds[account.Name] = account.Id;

			//_defaultAccount = account.Id;

			var details = (await _restClient.GetAccountDetailsAsync(account.Id, cancellationToken)).Account;

			if (details == null)
				continue;

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = account.Id,
				Currency = details.Currency.FromMicexCurrencyName(ex => this.AddErrorLog(ex)),
			}, cancellationToken);

			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = SecurityId.Money,
				PortfolioName = account.Id,
				ServerTime = CurrentTime
			}
			.TryAdd(PositionChangeTypes.RealizedPnL, (decimal)details.PnL, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal)details.UnrealizedPnL, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, (decimal)details.Balance, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal)details.MarginUsed, true)
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal)details.MarginAvailable, true), cancellationToken);

			var currentPositions = _currentPositions.SafeAdd(account.Id);

			foreach (var position in await _restClient.GetPositionsAsync(account.Id, cancellationToken))
			{
				var side = position.Long?.Units > 0 ? position.Long : position.Short;

				var secId = position.Instrument.ToStockSharp();

				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = account.Id,
					SecurityId = secId,
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, (decimal)side.Units, true)
				.TryAdd(PositionChangeTypes.Commission, (decimal?)position.Commission, true)
				.TryAdd(PositionChangeTypes.RealizedPnL, (decimal?)side.PnL, true)
				.TryAdd(PositionChangeTypes.AveragePrice, (decimal?)side.AveragePrice, true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)side.UnrealizedPnL, true), cancellationToken);

				currentPositions[secId] = RefTuple.Create((decimal)side.Units, (decimal?)position.Commission);
			}

			if (!lookupMsg.IsHistoryOnly())
			{
				if (_pfSubs.TryAdd(account.Id))
					_streamigClient.SubscribeTransactionsStreaming(account.Id);
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private ValueTask SessionOnNewTransaction(StreamingTransactionResponse response, CancellationToken cancellationToken)
	{
		return ProcessTransaction(response, null, cancellationToken);
	}

	private async ValueTask ProcessTransaction(Transaction transaction, long? originalTransactionId, CancellationToken cancellationToken)
	{
		var time = transaction.Time.FromUnixStr() ?? CurrentTime;
		long.TryParse(transaction.ClientExtensions?.Id, out var transactionId);
		long.TryParse(transaction.ClientRequestId, out var requestId);
		long.TryParse(transaction.ClientOrderId, out var clientOrderId);

		var isLookup = originalTransactionId != null;

		ExecutionMessage FillOrderInfo(ExecutionMessage execMsg, bool fill)
		{
			if (fill)
			{
				execMsg.SecurityId = transaction.Instrument.ToStockSharp();
				execMsg.Side = transaction.Units?.ToSide() ?? default;
				execMsg.OrderPrice = transaction.Price.ToPrice() ?? default;
				execMsg.TimeInForce = transaction.TimeInForce.ToTimeInForce();
				execMsg.PortfolioName = transaction.AccountId;
				execMsg.PositionEffect = transaction.PositionFill.ToPositionEffect();
				execMsg.Comment = transaction.ClientExtensions?.Comment;
				execMsg.Condition = new OandaOrderCondition
				{
					LowerBound = (decimal?)transaction.LowerBound,
					UpperBound = (decimal?)transaction.UpperBound,
					StopLossOffset = (decimal?)transaction.StopLoss,
					TakeProfitOffset = (decimal?)transaction.TakeProfit,
				};
				execMsg.Commission = (decimal?)transaction.Commission;

				var vol = (decimal)transaction.Units.Value.Abs();

				execMsg.OrderVolume = vol;
				execMsg.Balance = vol;
			}

			return execMsg;
		}

		// http://developer.oanda.com/rest-live/transaction-history/#transactionTypes
		switch (transaction.Type?.ToUpperInvariant())
		{
			case "LIMIT_ORDER_REJECT":
			{
				// {"type":"LIMIT_ORDER_REJECT","instrument":"AUD_JPY","units":"1","price":"82.99599999999999","timeInForce":"GTC",
				// "triggerCondition":"DEFAULT","partialFill":"DEFAULT","positionFill":"DEFAULT","reason":"CLIENT_ORDER",
				// "clientExtensions":{"id":"53766314"},"rejectReason":"PRICE_PRECISION_EXCEEDED","id":"4","userID":6072173,
				// "accountID":"101-004-6072173-001","batchID":"4","requestID":"24294760523913383","time":"1497354987.521583767"}

				//if (originalTransactionId != null)
				//	break;

				_orderBalance.Remove(transactionId);
			
				await SendOutMessageAsync(FillOrderInfo(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					OrderType = OrderTypes.Limit,
					OrderState = OrderStates.Failed,
					Error = new InvalidOperationException(transaction.RejectReason),
					ServerTime = time,
					TransactionId = !isLookup ? 0 : transactionId,
					OriginalTransactionId = originalTransactionId ?? transactionId,
				}, isLookup), cancellationToken);
				break;
			}

			case "MARKET_ORDER_REJECT":
			{
				// {"type":"MARKET_ORDER_REJECT","rejectReason":"TIME_IN_FORCE_INVALID","instrument":"EUR_CAD","units":"-1",
				// "timeInForce":"GTC","positionFill":"DEFAULT","reason":"CLIENT_ORDER","clientExtensions":{"id":"70360798"},
				// "id":"10","userID":6072173,"accountID":"101-004-6072173-001","batchID":"10","requestID":"24295192715414088",
				// "time":"1497458030.991606587"}

				//if (originalTransactionId != null)
				//	break;

				_orderBalance.Remove(transactionId);

				await SendOutMessageAsync(FillOrderInfo(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					OrderType = OrderTypes.Market,
					OrderState = OrderStates.Failed,
					Error = new InvalidOperationException(transaction.RejectReason),
					ServerTime = time,
					TransactionId = !isLookup ? 0 : transactionId,
					OriginalTransactionId = originalTransactionId ?? transactionId,
				}, isLookup), cancellationToken);
				break;
			}

			case "LIMIT_ORDER":
			{
				// {"type":"LIMIT_ORDER","instrument":"EUR_CAD","units":"1","price":"1.47866","timeInForce":"GTC",
				// "triggerCondition":"DEFAULT","partialFill":"DEFAULT","positionFill":"DEFAULT","reason":"CLIENT_ORDER",
				// "clientExtensions":{"id":"46394944"},"id":"6","userID":6072173,"accountID":"101-004-6072173-001","batchID":"6",
				// "requestID":"42309490573337839","time":"1497434033.358926187"}

				var vol = transaction.Units.Value.Abs();

				_orderBalance[transactionId] = (decimal)vol;

				await SendOutMessageAsync(FillOrderInfo(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					OrderState = OrderStates.Active,
					OrderType = OrderTypes.Limit,
					ServerTime = time,
					TransactionId = !isLookup ? 0 : transactionId,
					OriginalTransactionId = originalTransactionId ?? transactionId,
				}, true), cancellationToken);
				break;
			}

			case "MARKET_ORDER":
			{
				// {"type":"MARKET_ORDER","instrument":"EUR_CAD","units":"-1","timeInForce":"FOK","positionFill":"DEFAULT",
				// "reason":"CLIENT_ORDER","clientExtensions":{"id":"41976458"},"id":"18","userID":6072173,"accountID":"101-004-6072173-001",
				// "batchID":"18","requestID":"42309834544154917","time":"1497516042.338921688"}

				var vol = transaction.Units.Value.Abs();

				_orderBalance[transactionId] = (decimal)vol;

				await SendOutMessageAsync(FillOrderInfo(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					OrderState = OrderStates.Active,
					OrderType = OrderTypes.Market,
					ServerTime = time,
					TransactionId = !isLookup ? 0 : transactionId,
					OriginalTransactionId = originalTransactionId ?? transactionId,
				}, true), cancellationToken);
				break;
			}

			case "ORDER_FILL":
			{
				// {"type":"ORDER_FILL","orderID":"7","clientOrderID":"46394946","instrument":"EUR_CAD","units":"1",
				// "price":"1.47889","pl":"0.0000","financing":"0.0000","commission":"0.0000","accountBalance":"100000.0000",
				// "reason":"LIMIT_ORDER","tradeOpened":{"tradeID":"8","units":"1"},"id":"8","userID":0,"accountID":"101-004-6072173-001",
				// "batchID":"8","time":"1497434171.362253851"}

				// {"type":"ORDER_FILL","orderID":"18","clientOrderID":"41976458","instrument":"EUR_CAD","units":"-1","price":"1.48210",
				// "pl":"0.0022","financing":"0.0000","commission":"0.0000","accountBalance":"100000.0022","reason":"MARKET_ORDER",
				// "tradesClosed":[{"tradeID":"8","units":"-1","realizedPL":"0.0022","financing":"0.0000"}],"id":"19",
				// "userID":6072173,"accountID":"101-004-6072173-001","batchID":"18","requestID":"42309834544154917","time":"1497516042.338921688"}

				var trades = new List<TradeData>();
				
				if (transaction.TradesClosed != null)
					trades.AddRange(transaction.TradesClosed);

				if (transaction.TradeOpened != null)
					trades.Add(transaction.TradeOpened);

				if (transaction.TradeReduced != null)
					trades.Add(transaction.TradeReduced);

				var secId = transaction.Instrument.ToStockSharp();
				var balance = _orderBalance.TryGetValue2(clientOrderId);

				var price = transaction.Price.ToPrice() ?? 0;

				foreach (var trade in trades)
				{
					var tradeVolume = (decimal)trade.Units.Value.Abs();

					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Transactions,
						//HasOrderInfo = true,
						ServerTime = time,
						OriginalTransactionId = clientOrderId,
						TradeId = trade.TradeId,
						TradeVolume = tradeVolume,
						TradePrice = price,
						PnL = (decimal?)(trade.RealizedPnL ?? transaction.PnL),
						Commission = (decimal?)transaction.Commission,
					}, cancellationToken);

					if (balance != null)
						balance -= tradeVolume;

					if (!_isPositionsStreaming)
						continue;
					
					if (!_currentPositions.TryGetValue(transaction.AccountId, out var currentPositions))
						continue;

					var currPos = currentPositions.SafeAdd(secId);
					currPos.First += (decimal)trade.Units.Value;

					if (transaction.Commission != null)
					{
						if (currPos.Second is null)
							currPos.Second = 0;

						currPos.Second += (decimal)transaction.Commission.Value;
					}

					await SendOutMessageAsync(new PositionChangeMessage
					{
						SecurityId = secId,
						PortfolioName = transaction.AccountId,
						ServerTime = time,
					}
					.Add(PositionChangeTypes.CurrentValue, currPos.First)
					.TryAdd(PositionChangeTypes.Commission, currPos.Second, true), cancellationToken);
				}

				if (balance != null)
				{
					_orderBalance[clientOrderId] = balance.Value;

					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Transactions,
						HasOrderInfo = true,
						ServerTime = time,
						OrderId = transaction.OrderId,
						TransactionId = !isLookup ? 0 : clientOrderId,
						OriginalTransactionId = originalTransactionId ?? clientOrderId,
						Balance = balance.Value,
						OrderState = balance.Value > 0 ? OrderStates.Active : OrderStates.Done,
					}, cancellationToken);
				}
				
				break;
			}

			case "ORDER_CANCEL":
			{
				// {"type":"ORDER_CANCEL","orderID":"13","clientOrderID":"77022008","reason":"CLIENT_REQUEST","id":"14",
				// "userID":6072173,"accountID":"101-004-6072173-001","batchID":"14","requestID":"24295227122530858",
				// "time":"1497466233.049880709"}

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					OrderState = OrderStates.Done,
					Balance = _orderBalance.TryGetValue(clientOrderId),
					ServerTime = time,
					TransactionId = !isLookup ? 0 : clientOrderId,
					OriginalTransactionId = originalTransactionId ?? clientOrderId,
				}, cancellationToken);
				break;
			}

			case "ORDER_CANCEL_REJECT":
			{
				// {"type":"ORDER_CANCEL_REJECT","rejectReason":"ORDER_DOESNT_EXIST","orderID":"18","id":"22","userID":6072173,
				// "accountID":"101-004-6072173-001","batchID":"22","requestID":"42309835173662097","time":"1497516192.149614128"}

				if (isLookup)
					break;

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					OrderState = OrderStates.Failed,
					Error = new InvalidOperationException(transaction.RejectReason),
					ServerTime = time,
					OriginalTransactionId = originalTransactionId ?? requestId,
				}, cancellationToken);
				break;
			}

			case "CREATE":
			{
				// {"type":"CREATE","divisionID":4,"siteID":101,"accountUserID":6072173,"accountNumber":1,"homeCurrency":"EUR",
				// "id":"1","userID":6072173,"accountID":"101-004-6072173-001","batchID":"1","requestID":"1789699062121320491",
				// "time":"1495745106.275549063"}
				break;
			}

			case "CLIENT_CONFIGURE":
			{
				// {"type":"CLIENT_CONFIGURE","marginRate":"0.02","alias":"Primary","id":"2","userID":6072173,"accountID":"101-004-6072173-001",
				// "batchID":"1","requestID":"1789699062121320491","time":"1495745106.275549063"}
				break;
			}

			case "TRANSFER_FUNDS":
			{
				// {"accountBalance":"100000.0000","type":"TRANSFER_FUNDS","amount":"100000","fundingReason":"CLIENT_FUNDING","id":"3",
				// "userID":6072173,"accountID":"101-004-6072173-001","batchID":"3","requestID":"1789699062125514801","time":"1495745107.223021847"}
				break;
			}

			case "DAILY_FINANCING":
			{
				// {"type":"DAILY_FINANCING","financing":"0.0000","accountBalance":"100000.0000","accountFinancingMode":"SECOND_BY_SECOND",
				// "positionFinancings":[{"instrument":"EUR_CAD","financing":"0.0000","openTradeFinancings":[{"tradeID":"8","financing":"0.0000"},{"tradeID":"9","financing":"0.0000"}]}],
				// "id":"15","userID":0,"accountID":"101-004-6072173-001","batchID":"15","time":"1497470400.000000000"}
				break;
			}

			case "DIVIDEND_ADJUSTMENT":
			{
				break;
			}

			default:
				break;
		}
	}

	private string GetDefaultAccount()
	{
		if (_defaultAccount == null)
			throw new InvalidOperationException(LocalizedStrings.NoPortfoliosReceived);

		return _defaultAccount;
	}
}
