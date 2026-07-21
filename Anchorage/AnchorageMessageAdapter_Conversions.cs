namespace StockSharp.Anchorage;

public partial class AnchorageMessageAdapter
{
	private void TrackTradingOrder(AnchorageTradingOrder order,
		long localTransactionId, string portfolioName)
	{
		if (order?.OrderId.IsEmpty() != false)
			return;
		var previous = GetTrackedOperation(order.OrderId, order.ClientOrderId);
		if (localTransactionId == 0)
			localTransactionId = previous?.TransactionId ??
				ParseLocalTransactionId(order.ClientOrderId);
		portfolioName = portfolioName.IsEmpty()
			? previous?.PortfolioName ?? GetTradingPortfolioName(order.AccountId)
			: portfolioName;
		var securityId = order.Symbol.IsEmpty()
			? previous?.SecurityId ?? default
			: ToSecurityId(order.Symbol);
		TrackOperation(new()
		{
			NativeId = order.OrderId,
			ClientOrderId = order.ClientOrderId,
			TransactionId = localTransactionId,
			Kind = NativeOperationKinds.TradingOrder,
			PortfolioName = portfolioName,
			SecurityId = securityId,
			Operation = AnchorageOperations.Trade,
		});
		using (_sync.EnterScope())
		{
			if (!order.Status.IsFinal() && localTransactionId != 0)
				_activeTradingOrders[order.OrderId] = localTransactionId;
			else
				_activeTradingOrders.Remove(order.OrderId);
		}
		if (order.Status.IsFinal())
			_socketClient?.StopWatchingOrder(order.OrderId);
	}

	private void TrackTransfer(AnchorageTransfer transfer,
		long localTransactionId, string portfolioName, string clientOrderId)
	{
		if (transfer?.Id.IsEmpty() != false)
			return;
		var previous = GetTrackedOperation(transfer.Id, null);
		if (localTransactionId == 0)
			localTransactionId = previous?.TransactionId ?? 0;
		portfolioName = portfolioName.IsEmpty()
			? previous?.PortfolioName ?? GetVaultPortfolioName(transfer)
			: portfolioName;
		clientOrderId = clientOrderId.IsEmpty()
			? previous?.ClientOrderId
			: clientOrderId;
		TrackOperation(new()
		{
			NativeId = transfer.Id,
			ClientOrderId = clientOrderId,
			TransactionId = localTransactionId,
			Kind = NativeOperationKinds.Transfer,
			PortfolioName = portfolioName,
			SecurityId = ToAssetSecurityId(transfer.AssetType),
			Operation = AnchorageOperations.Transfer,
		});
		using (_sync.EnterScope())
		{
			if (!transfer.Status.IsFinal() && localTransactionId != 0)
				_activeTransfers[transfer.Id] = localTransactionId;
			else
				_activeTransfers.Remove(transfer.Id);
		}
	}

	private void TrackTransaction(AnchorageTransaction transaction,
		long localTransactionId, string portfolioName,
		AnchorageOperations operation, string clientOrderId)
	{
		if (transaction?.Id.IsEmpty() != false)
			return;
		var previous = GetTrackedOperation(transaction.Id, null);
		if (localTransactionId == 0)
			localTransactionId = previous?.TransactionId ?? 0;
		portfolioName = portfolioName.IsEmpty()
			? previous?.PortfolioName ?? GetVaultPortfolioName(transaction.VaultId)
			: portfolioName;
		clientOrderId = clientOrderId.IsEmpty()
			? previous?.ClientOrderId
			: clientOrderId;
		TrackOperation(new()
		{
			NativeId = transaction.Id,
			ClientOrderId = clientOrderId,
			TransactionId = localTransactionId,
			Kind = NativeOperationKinds.Transaction,
			PortfolioName = portfolioName,
			SecurityId = ToAssetSecurityId(transaction.AssetType),
			Operation = operation,
		});
		using (_sync.EnterScope())
		{
			if (!transaction.Status.IsFinal() && localTransactionId != 0)
				_activeTransactions[transaction.Id] = localTransactionId;
			else
				_activeTransactions.Remove(transaction.Id);
		}
	}

	private async ValueTask SendTradingOrderAsync(AnchorageTradingOrder order,
		long target, bool isForced, CancellationToken cancellationToken,
		string portfolioName)
	{
		ArgumentNullException.ThrowIfNull(order);
		var id = order.OrderId.ThrowIfEmpty(nameof(order.OrderId));
		var fingerprint = new TradingOrderFingerprint(order.Status,
			order.CumulativeQuantity, order.TransactionTime);
		var key = $"{target}:{id}";
		using (_sync.EnterScope())
		{
			if (!isForced && _tradingOrderFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_tradingOrderFingerprints[key] = fingerprint;
		}

		var tracked = GetTrackedOperation(id, order.ClientOrderId);
		portfolioName = portfolioName.IsEmpty()
			? tracked?.PortfolioName ?? GetTradingPortfolioName(order.AccountId)
			: portfolioName;
		var state = order.Status.ToOrderState();
		var volume = order.OrderQuantity.ParseAnchorageAmount();
		var balance = order.LeavesQuantity.ParseAnchorageAmount();
		if (balance == 0 && state == OrderStates.Active)
			balance = (volume - order.CumulativeQuantity.ParseAnchorageAmount())
				.Max(0m);
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.IsEmpty()
				? tracked?.SecurityId ?? default
				: ToSecurityId(order.Symbol),
			ServerTime = order.TransactionTime.ToAnchorageTime(
				order.SubmitTime.ToAnchorageTime(CurrentTime.EnsureUtc())),
			PortfolioName = portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = state == OrderStates.Active ? balance : 0m,
			OrderPrice = order.LimitPrice.ParseAnchorageAmount(),
			OrderType = ToOrderType(order.OrderType),
			OrderState = state,
			OrderStringId = id,
			OrderBoardId = order.ExecutionId,
			UserOrderId = order.ClientOrderId,
			TransactionId = tracked?.TransactionId ??
				ParseLocalTransactionId(order.ClientOrderId),
			OriginalTransactionId = target,
			Commission = GetFee(order.Fee, order.TotalFee),
			CommissionCurrency = order.FeeCurrency,
			Comment = order.ReasonText,
		};
		if (state == OrderStates.Failed)
			message.Error = new InvalidOperationException(
				$"Anchorage rejected order {id}: " +
				(order.ReasonText.IsEmpty()
					? order.RejectReason.ToString()
					: order.ReasonText));
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask SendTransferAsync(AnchorageTransfer transfer,
		long target, bool isForced, CancellationToken cancellationToken,
		string portfolioName)
	{
		ArgumentNullException.ThrowIfNull(transfer);
		var id = transfer.Id.ThrowIfEmpty(nameof(transfer.Id));
		var fingerprint = new TransferFingerprint(transfer.Status,
			transfer.EndedAt, transfer.BlockchainTransactionId);
		var key = $"{target}:{id}";
		using (_sync.EnterScope())
		{
			if (!isForced && _transferFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_transferFingerprints[key] = fingerprint;
		}
		var tracked = GetTrackedOperation(id, null);
		var state = transfer.Status.ToOrderState();
		var volume = transfer.Amount?.Quantity.ParseAnchorageAmount() ?? 0m;
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = ToAssetSecurityId(transfer.AssetType),
			ServerTime = transfer.EndedAt.ToAnchorageTime(
				transfer.CreatedAt.ToAnchorageTime(CurrentTime.EnsureUtc())),
			PortfolioName = portfolioName.IsEmpty()
				? tracked?.PortfolioName ?? GetVaultPortfolioName(transfer)
				: portfolioName,
			Side = Sides.Sell,
			OrderVolume = volume,
			Balance = state == OrderStates.Active ? volume : 0m,
			OrderPrice = 0m,
			OrderType = OrderTypes.Conditional,
			OrderState = state,
			OrderStringId = id,
			OrderBoardId = transfer.BlockchainTransactionId,
			UserOrderId = tracked?.ClientOrderId,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = target,
			Commission = transfer.Fee?.Quantity.ParseAnchorageAmount(),
			CommissionCurrency = transfer.Fee?.AssetType,
			Comment = transfer.Memo,
		};
		if (state == OrderStates.Failed)
			message.Error = new InvalidOperationException(
				transfer.Error?.Message.IsEmpty() == false
					? transfer.Error.Message
					: $"Anchorage transfer {id} failed.");
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask SendTransactionAsync(
		AnchorageTransaction transaction, long target, bool isForced,
		CancellationToken cancellationToken, string portfolioName,
		AnchorageOperations operation)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		var id = transaction.Id.ThrowIfEmpty(nameof(transaction.Id));
		var fingerprint = new TransactionFingerprint(transaction.Status,
			transaction.Timestamp, transaction.BlockchainTransactionId);
		var key = $"{target}:{id}";
		using (_sync.EnterScope())
		{
			if (!isForced && _transactionFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_transactionFingerprints[key] = fingerprint;
		}
		var tracked = GetTrackedOperation(id, null);
		var state = transaction.Status.ToOrderState();
		var volume = transaction.Amount?.Quantity.ParseAnchorageAmount() ?? 0m;
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = ToAssetSecurityId(transaction.AssetType),
			ServerTime = transaction.Timestamp.ToAnchorageTime(
				CurrentTime.EnsureUtc()),
			PortfolioName = portfolioName.IsEmpty()
				? tracked?.PortfolioName ?? GetVaultPortfolioName(transaction.VaultId)
				: portfolioName,
			Side = IsIncoming(transaction.Type) ? Sides.Buy : Sides.Sell,
			OrderVolume = volume,
			Balance = state == OrderStates.Active ? volume : 0m,
			OrderPrice = 0m,
			OrderType = OrderTypes.Conditional,
			OrderState = state,
			OrderStringId = id,
			OrderBoardId = transaction.BlockchainTransactionId,
			UserOrderId = tracked?.ClientOrderId,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = target,
			Commission = transaction.Fee?.Quantity.ParseAnchorageAmount(),
			CommissionCurrency = transaction.Fee?.AssetType,
			Comment = transaction.Description.IsEmpty()
				? $"Anchorage {operation} ({transaction.Type})"
				: transaction.Description,
		};
		if (state == OrderStates.Failed)
			message.Error = new InvalidOperationException(
				$"Anchorage transaction {id} ended with status " +
				$"{transaction.Status}.");
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask SendTradeAsync(AnchorageTrade trade, long target,
		AnchorageTradingOrder order, CancellationToken cancellationToken)
	{
		if (trade?.Id.IsEmpty() != false)
			return;
		var key = $"{target}:{trade.Id}";
		using (_sync.EnterScope())
			if (!_seenTrades.Add(key))
				return;
		var product = GetProduct(trade.Symbol) ?? GetProduct(order.Symbol);
		var volume = GetTradeVolume(trade, product);
		var tracked = GetTrackedOperation(order.OrderId, order.ClientOrderId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = ToSecurityId(trade.Symbol.IsEmpty()
				? order.Symbol
				: trade.Symbol),
			ServerTime = trade.Timestamp.ToAnchorageTime(
				CurrentTime.EnsureUtc()),
			PortfolioName = tracked?.PortfolioName ??
				GetTradingPortfolioName(trade.Account?.AccountId ?? order.AccountId),
			Side = trade.Side.ToStockSharp(),
			TradeStringId = trade.Id,
			TradePrice = trade.Price.ParseAnchorageAmount(),
			TradeVolume = volume,
			OrderStringId = trade.OrderId,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = target,
			Commission = trade.Fee.ParseAnchorageAmount(),
			CommissionCurrency = trade.FeeCurrency,
		}, cancellationToken);
	}

	private async ValueTask OnExecutionReceivedAsync(
		AnchorageWebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message?.Payload ?? throw new InvalidDataException(
			"Anchorage returned an empty execution-report payload.");
		if (payload.OrderId.IsEmpty())
			throw new InvalidDataException(
				"Anchorage execution report has no order ID.");
		var tracked = GetTrackedOperation(payload.OrderId,
			payload.ClientOrderId);
		var order = new AnchorageTradingOrder
		{
			ClientOrderId = payload.ClientOrderId,
			OrderId = payload.OrderId,
			ExecutionId = payload.ExecutionId,
			AccountId = payload.AccountId,
			Symbol = payload.Symbol.IsEmpty()
				? tracked?.SecurityId.SecurityCode
				: payload.Symbol,
			Side = payload.Side,
			Currency = payload.Currency,
			OrderQuantity = payload.OrderQuantity,
			OrderType = payload.OrderType,
			LimitPrice = payload.LimitPrice,
			TimeInForce = payload.TimeInForce,
			Status = payload.OrderStatus,
			ExecutionType = payload.ExecutionType,
			AveragePrice = payload.AveragePrice,
			AllInAveragePrice = payload.AllInAveragePrice,
			LeavesQuantity = payload.LeavesQuantity,
			CanceledQuantity = payload.CanceledQuantity,
			CumulativeQuantity = payload.CumulativeQuantity,
			Fee = payload.Fee,
			FeeCurrency = payload.FeeCurrency,
			RejectReason = payload.RejectReason,
			ReasonText = payload.RejectReasonText,
			SubmitTime = payload.SubmitTime,
			TransactionTime = payload.TransactionTime.IsEmpty()
				? message.Timestamp
				: payload.TransactionTime,
		};
		var localId = tracked?.TransactionId ??
			ParseLocalTransactionId(payload.ClientOrderId);
		TrackTradingOrder(order, localId, tracked?.PortfolioName);

		var targets = new HashSet<long>();
		if (localId != 0)
			targets.Add(localId);
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var target in subscriptions)
			if (Matches(target.Value, order))
				targets.Add(target.Key);
		foreach (var target in targets)
		{
			await SendTradingOrderAsync(order, target, false, cancellationToken,
				tracked?.PortfolioName);
			if (payload.ExecutionType == AnchorageExecutionTypes.Fill)
				await SendExecutionFillAsync(payload, order, target,
					cancellationToken);
		}
	}

	private async ValueTask SendExecutionFillAsync(
		AnchorageWebSocketPayload payload, AnchorageTradingOrder order,
		long target, CancellationToken cancellationToken)
	{
		if (payload.ExecutionId.IsEmpty())
			return;
		var key = $"{target}:{payload.ExecutionId}";
		using (_sync.EnterScope())
			if (!_seenTrades.Add(key))
				return;
		var tracked = GetTrackedOperation(order.OrderId, order.ClientOrderId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Symbol.IsEmpty()
				? tracked?.SecurityId ?? default
				: ToSecurityId(order.Symbol),
			ServerTime = order.TransactionTime.ToAnchorageTime(
				CurrentTime.EnsureUtc()),
			PortfolioName = tracked?.PortfolioName ??
				GetTradingPortfolioName(order.AccountId),
			Side = order.Side.ToStockSharp(),
			TradeStringId = payload.ExecutionId,
			TradePrice = payload.FillPrice.ParseAnchorageAmount(),
			TradeVolume = payload.FillQuantity.ParseAnchorageAmount(),
			OrderStringId = order.OrderId,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = target,
			Commission = payload.Fee.ParseAnchorageAmount(),
			CommissionCurrency = payload.FeeCurrency,
		}, cancellationToken);
	}

	private bool Matches(OrderSubscription subscription,
		AnchorageTradingOrder order)
	{
		if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
			return false;
		if (!subscription.NativeId.IsEmpty() &&
			!subscription.NativeId.EqualsIgnoreCase(order.OrderId) &&
			!subscription.NativeId.EqualsIgnoreCase(order.ClientOrderId))
			return false;
		if (!MatchesSecurity(subscription.SecurityId, order.Symbol) ||
			subscription.Side is Sides side && order.Side.ToStockSharp() != side ||
			subscription.Volume is decimal volume &&
				order.OrderQuantity.ParseAnchorageAmount() != volume ||
			!MatchesState(subscription.States, order.Status.ToOrderState()))
			return false;
		if (!subscription.PortfolioName.IsEmpty() &&
			!GetPortfolio(subscription.PortfolioName).Id.EqualsIgnoreCase(
				order.AccountId))
			return false;
		return MatchesTime(subscription,
			order.SubmitTime.ToAnchorageTime(DateTime.UnixEpoch));
	}

	private bool Matches(OrderSubscription subscription,
		AnchorageTransfer transfer)
	{
		if (transfer?.Id.IsEmpty() != false || transfer.AssetType.IsEmpty())
			return false;
		if (!subscription.NativeId.IsEmpty() &&
			!subscription.NativeId.EqualsIgnoreCase(transfer.Id) ||
			!MatchesSecurity(subscription.SecurityId, transfer.AssetType) ||
			subscription.Side is Sides side && side != Sides.Sell ||
			subscription.Volume is decimal volume &&
				transfer.Amount?.Quantity.ParseAnchorageAmount() != volume ||
			!MatchesState(subscription.States, transfer.Status.ToOrderState()))
			return false;
		if (!subscription.PortfolioName.IsEmpty() &&
			!subscription.PortfolioName.EqualsIgnoreCase(
				GetVaultPortfolioName(transfer)))
			return false;
		return MatchesTime(subscription,
			transfer.CreatedAt.ToAnchorageTime(DateTime.UnixEpoch));
	}

	private bool Matches(OrderSubscription subscription,
		AnchorageTransaction transaction)
	{
		if (transaction?.Id.IsEmpty() != false || transaction.AssetType.IsEmpty())
			return false;
		var transactionSide = IsIncoming(transaction.Type)
			? Sides.Buy
			: Sides.Sell;
		if (!subscription.NativeId.IsEmpty() &&
			!subscription.NativeId.EqualsIgnoreCase(transaction.Id) ||
			!MatchesSecurity(subscription.SecurityId, transaction.AssetType) ||
			subscription.Side is Sides side && side != transactionSide ||
			subscription.Volume is decimal volume &&
				transaction.Amount?.Quantity.ParseAnchorageAmount() != volume ||
			!MatchesState(subscription.States,
				transaction.Status.ToOrderState()))
			return false;
		if (!subscription.PortfolioName.IsEmpty() &&
			!GetPortfolio(subscription.PortfolioName).Id.EqualsIgnoreCase(
				transaction.VaultId))
			return false;
		return MatchesTime(subscription,
			transaction.Timestamp.ToAnchorageTime(DateTime.UnixEpoch));
	}

	private static bool MatchesSecurity(SecurityId filter, string code)
		=> (filter.BoardCode.IsEmpty() ||
			filter.BoardCode.EqualsIgnoreCase(BoardCodes.Anchorage)) &&
			(filter.SecurityCode.IsEmpty() ||
				filter.SecurityCode.EqualsIgnoreCase(code) ||
				(filter.Native as string).EqualsIgnoreCase(code));

	private static bool MatchesState(OrderStates[] states, OrderStates state)
		=> states is not { Length: > 0 } || states.Contains(state);

	private static bool MatchesTime(OrderSubscription subscription,
		DateTime time)
		=> (subscription.From is null || time >= subscription.From) &&
			(subscription.To is null || time <= subscription.To);

	private static OrderTypes ToOrderType(AnchorageNativeOrderTypes type)
		=> type switch
		{
			AnchorageNativeOrderTypes.Market => OrderTypes.Market,
			AnchorageNativeOrderTypes.Limit => OrderTypes.Limit,
			_ => OrderTypes.Conditional,
		};

	private static decimal? GetFee(string fee, string totalFee)
	{
		var value = totalFee.IsEmpty() ? fee : totalFee;
		return value.IsEmpty() ? null : value.ParseAnchorageAmount();
	}

	private static decimal GetTradeVolume(AnchorageTrade trade,
		AnchorageTradePair product)
	{
		var baseAsset = product?.Reference?.BaseAssetType;
		if (!baseAsset.IsEmpty())
		{
			if (trade.CurrencyBought.EqualsIgnoreCase(baseAsset))
				return trade.QuantityBought.ParseAnchorageAmount();
			if (trade.CurrencySold.EqualsIgnoreCase(baseAsset))
				return trade.QuantitySold.ParseAnchorageAmount();
		}
		return trade.Side == AnchorageSides.Buy
			? trade.QuantityBought.ParseAnchorageAmount()
			: trade.QuantitySold.ParseAnchorageAmount();
	}

	private string GetTradingPortfolioName(string accountId)
	{
		using (_sync.EnterScope())
			return _portfolios.Values.FirstOrDefault(portfolio =>
				portfolio.Kind == PortfolioKinds.Trading &&
				portfolio.Id.EqualsIgnoreCase(accountId))?.Name ??
				(accountId.IsEmpty()
					? null
					: AnchorageExtensions.GetTradingPortfolioName(accountId));
	}

	private string GetVaultPortfolioName(AnchorageTransfer transfer)
	{
		var walletId = transfer?.Source?.Type == AnchorageResourceTypes.Wallet
			? transfer.Source.Id
			: transfer?.Destination?.Type == AnchorageResourceTypes.Wallet
				? transfer.Destination.Id
				: null;
		using (_sync.EnterScope())
			if (!walletId.IsEmpty() && _wallets.TryGetValue(walletId,
				out var wallet))
				return GetVaultPortfolioNameUnsafe(wallet.VaultId);
		return null;
	}

	private string GetVaultPortfolioName(string vaultId)
	{
		using (_sync.EnterScope())
			return GetVaultPortfolioNameUnsafe(vaultId);
	}

	private string GetVaultPortfolioNameUnsafe(string vaultId)
		=> _portfolios.Values.FirstOrDefault(portfolio =>
			portfolio.Kind == PortfolioKinds.Vault &&
			portfolio.Id.EqualsIgnoreCase(vaultId))?.Name ??
			(vaultId.IsEmpty()
				? null
				: AnchorageExtensions.GetVaultPortfolioName(vaultId));

	private static SecurityId ToAssetSecurityId(string assetType)
		=> new()
		{
			SecurityCode = assetType,
			BoardCode = BoardCodes.Anchorage,
			Native = assetType,
		};

	private static bool IsIncoming(AnchorageTransactionTypes type)
		=> type is AnchorageTransactionTypes.Deposit or
			AnchorageTransactionTypes.StakingReward or
			AnchorageTransactionTypes.RestakingReward or
			AnchorageTransactionTypes.AlluvialStakingReward or
			AnchorageTransactionTypes.DelegationReward or
			AnchorageTransactionTypes.MevReward or
			AnchorageTransactionTypes.PriorityFeeReward or
			AnchorageTransactionTypes.FiatInterest or
			AnchorageTransactionTypes.Mint;

	private static AnchorageOperations ToOperation(
		AnchorageTransactionTypes type)
		=> type == AnchorageTransactionTypes.Withdraw
			? AnchorageOperations.Withdrawal
			: AnchorageOperations.Stake;

	private long GetLocalTransactionId(string nativeId, string clientOrderId,
		long fallback)
		=> GetTrackedOperation(nativeId, clientOrderId)?.TransactionId is long id &&
			id != 0
				? id
				: ParseLocalTransactionId(clientOrderId) is long parsed && parsed != 0
					? parsed
					: fallback;

	private static long ParseLocalTransactionId(string clientOrderId)
	{
		const string prefix = "stocksharp-";
		if (clientOrderId.IsEmpty() || !clientOrderId.StartsWith(prefix,
			StringComparison.OrdinalIgnoreCase))
			return 0;
		return long.TryParse(clientOrderId[prefix.Length..], NumberStyles.None,
			CultureInfo.InvariantCulture, out var value)
			? value
			: 0;
	}
}
