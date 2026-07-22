namespace StockSharp.Deriv;

public partial class DerivMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureAuthenticated();
		ValidatePortfolio(regMsg.PortfolioName);
		if (regMsg.Volume <= 0)
			throw new InvalidOperationException("Deriv contract amount must be positive.");
		var orderType = regMsg.OrderType ?? OrderTypes.Market;
		if (orderType != OrderTypes.Market)
			throw new NotSupportedException(
				"Deriv contract purchases are supported only as market orders.");

		var condition = regMsg.Condition as DerivOrderCondition
			?? throw new InvalidOperationException(
				"DerivOrderCondition with a native contract type is required.");
		var symbol = ResolveSymbol(regMsg.SecurityId).Symbol;
		DerivProposal proposal = null;
		var proposalId = condition.ProposalId;
		if (proposalId.IsEmpty())
		{
			var proposalResponse = await WebSocketClient.RequestAsync(
				condition.CreateProposalRequest(symbol, regMsg.Volume), cancellationToken);
			proposal = proposalResponse.Get<DerivProposal>("proposal")
				?? throw new InvalidDataException("Deriv did not return a price proposal.");
			proposalId = proposal.Id.ThrowIfEmpty(nameof(DerivProposal.Id));
			this.AddDebugLog("Deriv proposal received for {0}, contract {1}, stake {2}.",
				symbol, condition.ContractType, regMsg.Volume);
		}
		else if (regMsg.Price <= 0)
		{
			throw new InvalidOperationException(
				"A positive maximum purchase price is required with an external Deriv proposal ID.");
		}

		var maximumPrice = regMsg.Price > 0 ? regMsg.Price : proposal.AskPrice;
		var buyResponse = await WebSocketClient.RequestAsync(new JObject
		{
			["buy"] = proposalId,
			["price"] = maximumPrice,
		}, cancellationToken);
		var buy = buyResponse.Get<DerivBuy>("buy")
			?? throw new InvalidDataException("Deriv did not return a contract purchase receipt.");
		if (buy.ContractId <= 0)
			throw new InvalidDataException("Deriv returned an invalid contract identifier.");

		var securityId = symbol.ToSecurityId();
		var tracker = new DerivOrderTracker
		{
			TransactionId = regMsg.TransactionId,
			ContractId = buy.ContractId,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			Volume = regMsg.Volume,
			BuyPrice = buy.BuyPrice,
			PurchaseTime = buy.PurchaseTime.FromDerivEpoch(),
			Condition = condition,
		};
		TrackOrder(tracker);
		using (_sync.EnterScope())
			_seenTransactions.Add(buy.TransactionId);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderId = buy.ContractId,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = OrderTypes.Market,
			OrderPrice = buy.BuyPrice,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Active,
			ServerTime = tracker.PurchaseTime,
			Condition = condition,
		}, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = regMsg.TransactionId,
			OrderId = buy.ContractId,
			TradeId = buy.TransactionId,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			TradePrice = buy.BuyPrice,
			TradeVolume = regMsg.Volume,
			ServerTime = tracker.PurchaseTime,
		}, cancellationToken);

		this.AddInfoLog("Deriv purchased contract {0} for {1} {2} at {3}.",
			buy.ContractId, regMsg.Volume, condition.Basis, buy.BuyPrice);
		try
		{
			await EnsureContractSubscriptionAsync(DerivSubscriptionKinds.ContractOrder,
				regMsg.TransactionId, buy.ContractId, symbol, cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(new InvalidOperationException(
				$"Deriv contract {buy.ContractId} was purchased, but its update stream " +
				"could not be started.", error), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureAuthenticated();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var contractId = ResolveContractId(cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId);
		if (contractId <= 0)
			throw new InvalidOperationException(
				"A Deriv contract identifier is required to close a contract.");

		var tracker = GetOrder(contractId);
		var isCancellation = tracker?.Condition?.Cancellation.IsEmpty() == false;
		var response = await WebSocketClient.RequestAsync(isCancellation
			? new JObject { ["cancel"] = contractId }
			: new JObject { ["sell"] = contractId, ["price"] = 0 },
			cancellationToken);
		var close = response.Get<DerivClose>(isCancellation ? "cancel" : "sell")
			?? throw new InvalidDataException("Deriv did not return a contract close receipt.");

		if (tracker is not null)
			tracker.IsClosed = true;
		using (_sync.EnterScope())
			_seenTransactions.Add(close.TransactionId);
		var securityId = tracker?.SecurityId ?? default;
		var portfolio = tracker?.PortfolioName ?? PortfolioName;
		var volume = tracker?.Volume ?? 1m;
		var serverTime = DateTime.UtcNow;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = cancelMsg.TransactionId,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderId = contractId,
			SecurityId = securityId,
			PortfolioName = portfolio,
			Side = tracker?.Side ?? Sides.Buy,
			OrderType = OrderTypes.Market,
			OrderPrice = tracker?.BuyPrice ?? 0,
			OrderVolume = volume,
			Balance = 0,
			OrderState = OrderStates.Done,
			ServerTime = serverTime,
			Condition = tracker?.Condition,
		}, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderId = contractId,
			TradeId = close.TransactionId,
			SecurityId = securityId,
			PortfolioName = portfolio,
			Side = tracker?.Side ?? Sides.Buy,
			TradePrice = close.SoldFor,
			TradeVolume = volume,
			ServerTime = serverTime,
		}, cancellationToken);
		await SendBalanceValueAsync(cancelMsg.TransactionId, close.BalanceAfter,
			_account.Currency, serverTime, cancellationToken);

		foreach (var subscription in GetSubscriptions(
			tracker?.TransactionId ?? cancelMsg.OriginalTransactionId,
			DerivSubscriptionKinds.ContractOrder))
		{
			if (subscription.ContractId == contractId &&
				TryRemoveSubscription(subscription.NativeKey, out _))
				await WebSocketClient.UnsubscribeAsync(subscription.NativeKey,
					cancellationToken);
		}
		this.AddInfoLog("Deriv contract {0} closed through {1} for {2}.",
			contractId, isCancellation ? "cancel" : "sell", close.SoldFor);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		EnsureAuthenticated();
		if (!statusMsg.IsSubscribe)
		{
			await RemoveAccountSubscriptionsAsync(statusMsg.OriginalTransactionId,
				cancellationToken, DerivSubscriptionKinds.Transaction,
				DerivSubscriptionKinds.ContractOrderStatus);
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		ValidatePortfolio(statusMsg.PortfolioName);
		if (_orderStatusSubscriptionId != 0 &&
			_orderStatusSubscriptionId != statusMsg.TransactionId)
			await RemoveAccountSubscriptionsAsync(_orderStatusSubscriptionId,
				cancellationToken, DerivSubscriptionKinds.Transaction,
				DerivSubscriptionKinds.ContractOrderStatus);

		_orderStatusSubscriptionId = statusMsg.IsHistoryOnly()
			? 0 : statusMsg.TransactionId;
		var portfolio = await LoadPortfolioAsync(cancellationToken);
		await SendPortfolioOrdersAsync(statusMsg.TransactionId, portfolio,
			!statusMsg.IsHistoryOnly(), cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}

		var transactionSubscription = new DerivSubscription
		{
			NativeKey = $"transactions:{statusMsg.TransactionId}",
			Kind = DerivSubscriptionKinds.Transaction,
			TransactionId = statusMsg.TransactionId,
		};
		AddSubscription(transactionSubscription);
		try
		{
			await WebSocketClient.SubscribeAsync(transactionSubscription.NativeKey,
				new JObject { ["transaction"] = 1, ["subscribe"] = 1 }, false,
				cancellationToken);
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		catch
		{
			TryRemoveSubscription(transactionSubscription.NativeKey, out _);
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureAuthenticated();
		if (!lookupMsg.IsSubscribe)
		{
			await RemoveAccountSubscriptionsAsync(lookupMsg.OriginalTransactionId,
				cancellationToken, DerivSubscriptionKinds.Balance,
				DerivSubscriptionKinds.ContractPortfolio);
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		ValidatePortfolio(lookupMsg.PortfolioName);
		if (_portfolioSubscriptionId != 0 &&
			_portfolioSubscriptionId != lookupMsg.TransactionId)
			await RemoveAccountSubscriptionsAsync(_portfolioSubscriptionId,
				cancellationToken, DerivSubscriptionKinds.Balance,
				DerivSubscriptionKinds.ContractPortfolio);

		_portfolioSubscriptionId = lookupMsg.IsHistoryOnly()
			? 0 : lookupMsg.TransactionId;
		await SendPortfolioDefinitionAsync(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			var balanceResponse = await WebSocketClient.RequestAsync(
				new JObject { ["balance"] = 1 }, cancellationToken);
			await ProcessBalanceResponseAsync(new()
			{
				Kind = DerivSubscriptionKinds.Balance,
				TransactionId = lookupMsg.TransactionId,
			}, balanceResponse, cancellationToken);
		}
		else
		{
			var balanceSubscription = new DerivSubscription
			{
				NativeKey = $"balance:{lookupMsg.TransactionId}",
				Kind = DerivSubscriptionKinds.Balance,
				TransactionId = lookupMsg.TransactionId,
			};
			AddSubscription(balanceSubscription);
			try
			{
				var response = await WebSocketClient.SubscribeAsync(
					balanceSubscription.NativeKey,
					new JObject { ["balance"] = 1, ["subscribe"] = 1 }, true,
					cancellationToken);
				await ProcessBalanceResponseAsync(balanceSubscription, response,
					cancellationToken);
			}
			catch
			{
				TryRemoveSubscription(balanceSubscription.NativeKey, out _);
				_portfolioSubscriptionId = 0;
				throw;
			}
		}

		var portfolio = await LoadPortfolioAsync(cancellationToken);
		await SendPortfolioPositionsAsync(lookupMsg.TransactionId, portfolio,
			!lookupMsg.IsHistoryOnly(), cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask RefreshAccountSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		var portfolio = await LoadPortfolioAsync(cancellationToken);
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioPositionsAsync(_portfolioSubscriptionId, portfolio, true,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendPortfolioOrdersAsync(_orderStatusSubscriptionId, portfolio, true,
				cancellationToken);
		this.AddDebugLog("Deriv refreshed account snapshots with {0} open contracts.",
			portfolio.Contracts?.Length ?? 0);
	}

	private async ValueTask<DerivPortfolio> LoadPortfolioAsync(
		CancellationToken cancellationToken)
	{
		var response = await WebSocketClient.RequestAsync(
			new JObject { ["portfolio"] = 1 }, cancellationToken);
		return response.Get<DerivPortfolio>("portfolio") ?? new() { Contracts = [] };
	}

	private async ValueTask SendPortfolioOrdersAsync(long transactionId,
		DerivPortfolio portfolio, bool isLive, CancellationToken cancellationToken)
	{
		foreach (var contract in portfolio.Contracts ?? [])
		{
			await SendPortfolioContractOrderAsync(transactionId, contract,
				cancellationToken);
			if (isLive)
				await EnsureContractSubscriptionAsync(
					DerivSubscriptionKinds.ContractOrderStatus, transactionId,
					contract.ContractId, contract.Symbol, cancellationToken);
		}
	}

	private async ValueTask SendPortfolioPositionsAsync(long transactionId,
		DerivPortfolio portfolio, bool isLive, CancellationToken cancellationToken)
	{
		foreach (var contract in portfolio.Contracts ?? [])
		{
			await SendPortfolioContractPositionAsync(transactionId, contract,
				cancellationToken);
			if (isLive)
				await EnsureContractSubscriptionAsync(
					DerivSubscriptionKinds.ContractPortfolio, transactionId,
					contract.ContractId, contract.Symbol, cancellationToken);
		}
	}

	private ValueTask SendPortfolioContractOrderAsync(long transactionId,
		DerivPortfolioContract contract, CancellationToken cancellationToken)
	{
		var tracker = GetOrder(contract.ContractId);
		var side = contract.ContractType.IsDownContract() ? Sides.Sell : Sides.Buy;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderId = contract.ContractId,
			SecurityId = contract.Symbol.ToSecurityId(),
			PortfolioName = PortfolioName,
			Side = tracker?.Side ?? side,
			OrderType = OrderTypes.Market,
			OrderPrice = contract.BuyPrice,
			OrderVolume = tracker?.Volume ?? 1m,
			Balance = tracker?.Volume ?? 1m,
			OrderState = OrderStates.Active,
			ServerTime = contract.PurchaseTime.FromDerivEpoch(),
			Condition = tracker?.Condition,
		}, cancellationToken);
	}

	private ValueTask SendPortfolioContractPositionAsync(long transactionId,
		DerivPortfolioContract contract, CancellationToken cancellationToken)
	{
		var side = contract.ContractType.IsDownContract() ? -1m : 1m;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = transactionId,
			PortfolioName = PortfolioName,
			SecurityId = contract.Symbol.ToSecurityId(),
			ServerTime = contract.PurchaseTime.FromDerivEpoch(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, side, true)
		.TryAdd(PositionChangeTypes.AveragePrice, contract.BuyPrice, true),
			cancellationToken);
	}

	private async ValueTask EnsureContractSubscriptionAsync(
		DerivSubscriptionKinds kind, long transactionId, long contractId, string symbol,
		CancellationToken cancellationToken)
	{
		var nativeKey = $"contract:{kind}:{transactionId}:{contractId}";
		if (TryGetSubscription(nativeKey, out _))
			return;
		var subscription = new DerivSubscription
		{
			NativeKey = nativeKey,
			Kind = kind,
			TransactionId = transactionId,
			ContractId = contractId,
			Symbol = symbol,
			SecurityId = symbol.IsEmpty() ? default : symbol.ToSecurityId(),
		};
		AddSubscription(subscription);
		try
		{
			var response = await WebSocketClient.SubscribeAsync(nativeKey, new JObject
			{
				["proposal_open_contract"] = 1,
				["contract_id"] = contractId,
				["subscribe"] = 1,
			}, true, cancellationToken);
			await ProcessOpenContractResponseAsync(subscription, response,
				cancellationToken);
		}
		catch
		{
			TryRemoveSubscription(nativeKey, out _);
			throw;
		}
	}

	private async ValueTask ProcessOpenContractResponseAsync(
		DerivSubscription subscription, DerivResponse response,
		CancellationToken cancellationToken)
	{
		var contract = response?.Get<DerivOpenContract>("proposal_open_contract");
		if (contract is null)
			return;
		if (subscription.Kind == DerivSubscriptionKinds.ContractPortfolio)
			await SendOpenContractPositionAsync(subscription.TransactionId, contract,
				cancellationToken);
		else
			await SendOpenContractOrderAsync(subscription.TransactionId, contract,
				cancellationToken);

		if (IsContractDone(contract))
		{
			TryRemoveSubscription(subscription.NativeKey, out _);
			WebSocketClient.DropSubscription(subscription.NativeKey);
		}
	}

	private async ValueTask SendOpenContractOrderAsync(long transactionId,
		DerivOpenContract contract, CancellationToken cancellationToken)
	{
		var tracker = GetOrder(contract.ContractId);
		var side = tracker?.Side ??
			(contract.ContractType.IsDownContract() ? Sides.Sell : Sides.Buy);
		var volume = tracker?.Volume ?? 1m;
		var isDone = IsContractDone(contract);
		var serverTime = GetContractTime(contract);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderId = contract.ContractId,
			SecurityId = GetContractSecurityId(contract, tracker),
			PortfolioName = PortfolioName,
			Side = side,
			OrderType = OrderTypes.Market,
			OrderPrice = contract.BuyPrice,
			OrderVolume = volume,
			Balance = isDone ? 0 : volume,
			AveragePrice = contract.BidPrice,
			OrderState = !contract.ValidationError.IsEmpty()
				? OrderStates.Failed
				: isDone ? OrderStates.Done : OrderStates.Active,
			ServerTime = serverTime,
			PnL = contract.Profit,
			Condition = tracker?.Condition,
		}, cancellationToken);

		var sellTransactionId = contract.TransactionIds?.Sell ?? 0;
		if (!isDone || sellTransactionId <= 0 || contract.SellPrice is null)
			return;
		bool shouldSend;
		using (_sync.EnterScope())
		{
			shouldSend = _seenTransactions.Add(sellTransactionId);
			if (tracker is not null)
			{
				tracker.IsClosed = true;
				tracker.IsCloseTradeSent = true;
			}
		}
		if (!shouldSend)
			return;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId,
			OrderId = contract.ContractId,
			TradeId = sellTransactionId,
			SecurityId = GetContractSecurityId(contract, tracker),
			PortfolioName = PortfolioName,
			Side = side,
			TradePrice = contract.SellPrice,
			TradeVolume = volume,
			ServerTime = serverTime,
			PnL = contract.Profit,
		}, cancellationToken);
	}

	private ValueTask SendOpenContractPositionAsync(long transactionId,
		DerivOpenContract contract, CancellationToken cancellationToken)
	{
		var tracker = GetOrder(contract.ContractId);
		var value = IsContractDone(contract) ? 0m :
			(contract.ContractType.IsDownContract() ? -1m : 1m);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = transactionId,
			PortfolioName = PortfolioName,
			SecurityId = GetContractSecurityId(contract, tracker),
			ServerTime = GetContractTime(contract),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, value, true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			contract.EntrySpot ?? contract.BuyPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			contract.CurrentSpot ?? contract.ExitSpot, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, contract.Profit, true),
			cancellationToken);
	}

	private async ValueTask ProcessBalanceResponseAsync(DerivSubscription subscription,
		DerivResponse response, CancellationToken cancellationToken)
	{
		var balance = response?.Get<DerivBalance>("balance");
		if (balance is null)
			return;
		await SendBalanceValueAsync(subscription.TransactionId, balance.Value,
			balance.Currency, DateTime.UtcNow, cancellationToken);
	}

	private async ValueTask ProcessTransactionResponseAsync(DerivSubscription subscription,
		DerivResponse response, CancellationToken cancellationToken)
	{
		var transaction = response?.Get<DerivTransaction>("transaction");
		if (transaction is null || transaction.TransactionId <= 0)
			return;
		using (_sync.EnterScope())
		{
			if (!_seenTransactions.Add(transaction.TransactionId))
				return;
		}

		var serverTime = transaction.TransactionTime.FromDerivEpoch();
		await SendBalanceValueAsync(subscription.TransactionId, transaction.Balance,
			transaction.Currency, serverTime, cancellationToken);
		if (transaction.ContractId is not long contractId || contractId <= 0 ||
			(!transaction.Action.EqualsIgnoreCase("buy") &&
			 !transaction.Action.EqualsIgnoreCase("sell")))
			return;

		var tracker = GetOrder(contractId);
		var securityId = tracker?.SecurityId ??
			(transaction.Symbol.IsEmpty() ? default : transaction.Symbol.ToSecurityId());
		var isSell = transaction.Action.EqualsIgnoreCase("sell");
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = subscription.TransactionId,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderId = contractId,
			TradeId = transaction.TransactionId,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = tracker?.Side ?? Sides.Buy,
			OrderType = OrderTypes.Market,
			OrderPrice = tracker?.BuyPrice ?? transaction.Amount.Abs(),
			OrderVolume = tracker?.Volume ?? 1m,
			Balance = isSell ? 0 : tracker?.Volume ?? 1m,
			OrderState = isSell ? OrderStates.Done : OrderStates.Active,
			TradePrice = transaction.Amount.Abs(),
			TradeVolume = tracker?.Volume ?? 1m,
			ServerTime = serverTime,
			Condition = tracker?.Condition,
		}, cancellationToken);
	}

	private ValueTask SendPortfolioDefinitionAsync(long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = transactionId,
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Deriv,
		}, cancellationToken);

	private ValueTask SendBalanceValueAsync(long transactionId, decimal balance,
		string currency, DateTime serverTime, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = transactionId,
			PortfolioName = PortfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = serverTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance, true)
		.TryAdd(PositionChangeTypes.Currency,
			Enum.TryParse<CurrencyTypes>(currency, true, out var value) ? value : null),
			cancellationToken);

	private async ValueTask RemoveAccountSubscriptionsAsync(long transactionId,
		CancellationToken cancellationToken, params DerivSubscriptionKinds[] kinds)
	{
		foreach (var subscription in GetSubscriptions(transactionId, kinds))
		{
			if (TryRemoveSubscription(subscription.NativeKey, out _))
				await WebSocketClient.UnsubscribeAsync(subscription.NativeKey,
					cancellationToken);
		}
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException(
				$"Deriv account '{portfolioName}' is unavailable in this session.");
	}

	private static bool IsContractDone(DerivOpenContract contract)
		=> contract.IsSold == 1 || contract.IsExpired == 1 ||
			contract.Status?.ToLowerInvariant() is "sold" or "won" or "lost" or
			"expired" or "cancelled";

	private static DateTime GetContractTime(DerivOpenContract contract)
		=> (contract.SellTime ?? contract.ExitSpotTime ??
			(contract.CurrentSpotTime > 0 ? contract.CurrentSpotTime : contract.PurchaseTime))
			.FromDerivEpoch();

	private static SecurityId GetContractSecurityId(DerivOpenContract contract,
		DerivOrderTracker tracker)
		=> tracker?.SecurityId ??
			(contract.Symbol.IsEmpty() ? default : contract.Symbol.ToSecurityId());
}
