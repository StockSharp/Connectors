namespace StockSharp.Anchorage;

public partial class AnchorageMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!lookupMsg.IsSubscribe)
		{
			RemovePortfolioSubscription(lookupMsg.OriginalTransactionId);
			return;
		}

		await RefreshReferenceDataAsync(cancellationToken);
		var selected = SelectPortfolios(GetPortfolios(),
			lookupMsg.PortfolioName);
		foreach (var portfolio in selected)
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = portfolio.Name,
				BoardCode = BoardCodes.Anchorage,
				ClientCode = portfolio.Id,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true, selected,
			cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_portfolioSubscriptions[lookupMsg.TransactionId] = new()
			{
				PortfolioName = lookupMsg.PortfolioName,
			};
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!statusMsg.IsSubscribe)
		{
			RemoveOrderSubscription(statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		if (!statusMsg.PortfolioName.IsEmpty())
			_ = GetPortfolio(statusMsg.PortfolioName);
		if (statusMsg.OrderId is not null)
			throw new NotSupportedException(
				"Anchorage exchange identifiers are strings.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Anchorage order history has no exchange-side user filter.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Anchorage history.");

		var subscription = new OrderSubscription
		{
			NativeId = statusMsg.OrderStringId,
			PortfolioName = statusMsg.PortfolioName,
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			Volume = statusMsg.Volume,
			States = statusMsg.States,
			From = statusMsg.From?.EnsureUtc(),
			To = statusMsg.To?.EnsureUtc(),
			Skip = (statusMsg.Skip ?? 0).Max(0).Min(int.MaxValue).To<int>(),
			Maximum = (statusMsg.Count ?? HistoryLimit).Min(HistoryLimit)
				.Max(1).To<int>(),
		};
		await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId, true,
			cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions[statusMsg.TransactionId] = subscription;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private static PortfolioReference[] SelectPortfolios(
		IEnumerable<PortfolioReference> portfolios, string portfolioName)
	{
		var selected = portfolios.Where(portfolio => portfolio is not null &&
			(portfolioName.IsEmpty() ||
				portfolio.Name.EqualsIgnoreCase(portfolioName))).ToArray();
		if (!portfolioName.IsEmpty() && selected.Length == 0)
			throw new InvalidOperationException(
				$"Unknown Anchorage portfolio '{portfolioName}'.");
		return selected;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, IEnumerable<PortfolioReference> portfolios,
		CancellationToken cancellationToken)
	{
		AnchorageWallet[] wallets;
		using (_sync.EnterScope())
			wallets = [.. _wallets.Values];
		foreach (var portfolio in portfolios)
		{
			if (portfolio.Kind == PortfolioKinds.Trading)
			{
				var balances = await RestClient.GetTradingBalancesAsync(portfolio.Id,
					cancellationToken);
				foreach (var group in balances.Where(static item =>
					item?.Balance?.AssetType.IsEmpty() == false).GroupBy(item =>
						item.Balance.AssetType, StringComparer.OrdinalIgnoreCase))
				{
					var current = group.Sum(static item =>
						item.Balance.Quantity.ParseAnchorageAmount());
					await SendBalanceAsync(target, isForced, portfolio.Name,
						group.Key, current, 0m, CurrentTime.EnsureUtc(),
						cancellationToken);
				}
			}
			else
			{
				var assets = wallets.Where(wallet => wallet is not null &&
					!wallet.IsArchived &&
					wallet.VaultId.EqualsIgnoreCase(portfolio.Id))
					.SelectMany(static wallet => wallet.Assets ?? [])
					.Where(static asset => asset?.AssetType.IsEmpty() == false)
					.GroupBy(static asset => asset.AssetType,
						StringComparer.OrdinalIgnoreCase);
				foreach (var group in assets)
				{
					var current = group.Sum(static asset =>
						asset.TotalBalance?.Quantity.ParseAnchorageAmount() ?? 0m);
					var available = group.Sum(static asset =>
						asset.AvailableBalance?.Quantity.ParseAnchorageAmount() ?? 0m);
					await SendBalanceAsync(target, isForced, portfolio.Name,
						group.Key, current, (current - available).Max(0m),
						CurrentTime.EnsureUtc(), cancellationToken);
				}
			}
		}
	}

	private async ValueTask SendBalanceAsync(long target, bool isForced,
		string portfolioName, string assetType, decimal current, decimal blocked,
		DateTime serverTime, CancellationToken cancellationToken)
	{
		var fingerprint = new BalanceFingerprint(current, blocked);
		var key = $"{target}:{portfolioName}:{assetType}";
		using (_sync.EnterScope())
		{
			if (!isForced && _balanceFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_balanceFingerprints[key] = fingerprint;
		}
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = portfolioName,
			SecurityId = new()
			{
				SecurityCode = assetType,
				BoardCode = BoardCodes.Anchorage,
				Native = assetType,
			},
			ServerTime = serverTime.EnsureUtc(),
			OriginalTransactionId = target,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, current, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true),
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		var requested = (subscription.Skip + subscription.Maximum)
			.Min(HistoryLimit).Max(1);
		var portfolio = subscription.PortfolioName.IsEmpty()
			? null
			: GetPortfolio(subscription.PortfolioName);
		var tracked = subscription.NativeId.IsEmpty()
			? null
			: GetTrackedOperation(subscription.NativeId, null);

		AnchorageTradingOrder[] orders = [];
		AnchorageTransfer[] transfers = [];
		AnchorageTransaction[] transactions = [];
		if (tracked?.Kind == NativeOperationKinds.TradingOrder)
			orders = [await RestClient.GetTradingOrderAsync(subscription.NativeId,
				cancellationToken)];
		else if (tracked?.Kind == NativeOperationKinds.Transfer)
			transfers = [await RestClient.GetTransferAsync(subscription.NativeId,
				cancellationToken)];
		else if (tracked?.Kind == NativeOperationKinds.Transaction)
			transactions = [await RestClient.GetTransactionAsync(
				subscription.NativeId, cancellationToken)];
		else
		{
			var productFilter = subscription.SecurityId.SecurityCode.IsEmpty()
				? null
				: GetProduct(subscription.SecurityId.SecurityCode);
			var isTradingRequested = portfolio?.Kind != PortfolioKinds.Vault &&
				(productFilter is not null ||
					subscription.SecurityId.SecurityCode.IsEmpty());
			var isCustodyRequested = portfolio?.Kind != PortfolioKinds.Trading &&
				productFilter is null;
			if (isTradingRequested)
				orders = await TryReadDomainAsync(
					ct => RestClient.GetTradingOrdersAsync(subscription.From,
						subscription.To,
						portfolio?.Kind == PortfolioKinds.Trading
							? portfolio.Id
							: null,
						requested, ct), Array.Empty<AnchorageTradingOrder>(),
					"trading-order history", cancellationToken);
			if (isCustodyRequested)
			{
				transfers = await TryReadDomainAsync(
					ct => RestClient.GetTransfersAsync(subscription.From,
						subscription.To,
						portfolio?.Kind == PortfolioKinds.Vault
							? portfolio.Id
							: null,
						PageSize, requested, ct), Array.Empty<AnchorageTransfer>(),
					"transfer history", cancellationToken);
				transactions = await TryReadDomainAsync(
					ct => RestClient.GetTransactionsAsync(subscription.From,
						subscription.To,
						portfolio?.Kind == PortfolioKinds.Vault
							? portfolio.Id
							: null,
						PageSize, requested, ct),
					Array.Empty<AnchorageTransaction>(), "transaction history",
					cancellationToken);
			}
		}

		var skipped = 0;
		var delivered = 0;
		foreach (var order in orders.Where(item => Matches(subscription, item))
			.OrderBy(static item => item.SubmitTime.ToAnchorageTime(
				DateTime.UnixEpoch)))
		{
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			var localId = GetLocalTransactionId(order.OrderId,
				order.ClientOrderId, 0);
			TrackTradingOrder(order, localId, subscription.PortfolioName);
			await SendTradingOrderAsync(order, target, isForced,
				cancellationToken, subscription.PortfolioName);
			var trades = await RestClient.GetTradesAsync(subscription.From,
				subscription.To, order.AccountId, order.Symbol, order.OrderId,
				HistoryLimit, cancellationToken);
			foreach (var trade in trades.OrderBy(static item =>
				item.Timestamp.ToAnchorageTime(DateTime.UnixEpoch)))
				await SendTradeAsync(trade, target, order, cancellationToken);
		}
		if (delivered < subscription.Maximum)
		{
			foreach (var transfer in transfers.Where(item =>
				Matches(subscription, item)).OrderBy(static item =>
					item.CreatedAt.ToAnchorageTime(DateTime.UnixEpoch)))
			{
				if (skipped++ < subscription.Skip)
					continue;
				if (delivered++ >= subscription.Maximum)
					break;
				var operation = GetTrackedOperation(transfer.Id, null);
				TrackTransfer(transfer, operation?.TransactionId ?? 0,
					subscription.PortfolioName, operation?.ClientOrderId);
				await SendTransferAsync(transfer, target, isForced,
					cancellationToken, subscription.PortfolioName);
			}
		}
		if (delivered < subscription.Maximum)
		{
			foreach (var transaction in transactions.Where(item =>
				Matches(subscription, item)).OrderBy(static item =>
					item.Timestamp.ToAnchorageTime(DateTime.UnixEpoch)))
			{
				if (skipped++ < subscription.Skip)
					continue;
				if (delivered++ >= subscription.Maximum)
					break;
				var operation = GetTrackedOperation(transaction.Id, null);
				var kind = operation?.Operation ?? ToOperation(transaction.Type);
				TrackTransaction(transaction, operation?.TransactionId ?? 0,
					subscription.PortfolioName, kind, operation?.ClientOrderId);
				await SendTransactionAsync(transaction, target, isForced,
					cancellationToken, subscription.PortfolioName, kind);
			}
		}
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, PortfolioSubscription>[] portfolioTargets;
		KeyValuePair<long, OrderSubscription>[] orderTargets;
		KeyValuePair<string, long>[] activeOrders;
		KeyValuePair<string, long>[] activeTransfers;
		KeyValuePair<string, long>[] activeTransactions;
		using (_sync.EnterScope())
		{
			portfolioTargets = [.. _portfolioSubscriptions];
			orderTargets = [.. _orderSubscriptions];
			activeOrders = [.. _activeTradingOrders];
			activeTransfers = [.. _activeTransfers];
			activeTransactions = [.. _activeTransactions];
		}

		if (portfolioTargets.Length > 0)
		{
			await RefreshReferenceDataAsync(cancellationToken);
			var portfolios = GetPortfolios();
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target.Key, false,
					SelectPortfolios(portfolios, target.Value.PortfolioName),
					cancellationToken);
		}
		foreach (var (id, localId) in activeOrders)
		{
			var order = await RestClient.GetTradingOrderAsync(id,
				cancellationToken);
			TrackTradingOrder(order, localId, null);
			await SendTradingOrderAsync(order, localId, false, cancellationToken,
				null);
		}
		foreach (var (id, localId) in activeTransfers)
		{
			var transfer = await RestClient.GetTransferAsync(id,
				cancellationToken);
			TrackTransfer(transfer, localId, null, null);
			await SendTransferAsync(transfer, localId, false, cancellationToken,
				null);
		}
		foreach (var (id, localId) in activeTransactions)
		{
			var transaction = await RestClient.GetTransactionAsync(id,
				cancellationToken);
			var operation = GetTrackedOperation(id, null)?.Operation ??
				ToOperation(transaction.Type);
			TrackTransaction(transaction, localId, null, operation, null);
			await SendTransactionAsync(transaction, localId, false,
				cancellationToken, null, operation);
		}
		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);
	}

	private void RemovePortfolioSubscription(long target)
	{
		using (_sync.EnterScope())
		{
			_portfolioSubscriptions.Remove(target);
			RemoveFingerprintPrefix(_balanceFingerprints, target);
		}
	}

	private void RemoveOrderSubscription(long target)
	{
		using (_sync.EnterScope())
		{
			_orderSubscriptions.Remove(target);
			RemoveFingerprintPrefix(_tradingOrderFingerprints, target);
			RemoveFingerprintPrefix(_transferFingerprints, target);
			RemoveFingerprintPrefix(_transactionFingerprints, target);
			var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
			_seenTrades.RemoveWhere(key => key.StartsWith(prefix,
				StringComparison.Ordinal));
		}
	}

	private static void RemoveFingerprintPrefix<T>(
		Dictionary<string, T> fingerprints, long target)
	{
		var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
		foreach (var key in fingerprints.Keys.Where(key => key.StartsWith(prefix,
			StringComparison.Ordinal)).ToArray())
			fingerprints.Remove(key);
	}

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
