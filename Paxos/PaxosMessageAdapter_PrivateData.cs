namespace StockSharp.Paxos;

using StockSharp.Paxos.Native.Model;

public partial class PaxosMessageAdapter
{
	private sealed class HistoryRecord
	{
		public DateTime Time { get; init; }
		public PaxosOrder Order { get; init; }
		public PaxosTransfer Transfer { get; init; }
		public PaxosStablecoinConversion Conversion { get; init; }
		public string PortfolioName { get; init; }
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);
		EnsureAuthenticated();
		if (!message.IsSubscribe)
		{
			RemovePortfolioSubscription(message.OriginalTransactionId);
			return;
		}
		await RefreshProfilesAsync(cancellationToken);
		var selected = SelectPortfolios(GetPortfolios(), message.PortfolioName);
		foreach (var portfolio in selected)
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = portfolio.Name,
				BoardCode = BoardCodes.Paxos,
				ClientCode = portfolio.Profile.Id,
				OriginalTransactionId = message.TransactionId,
			}, cancellationToken);
		await SendPortfolioSnapshotAsync(message.TransactionId, true, selected,
			cancellationToken);
		if (message.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			await SendSubscriptionFinishedAsync(message.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_portfolioSubscriptions[message.TransactionId] = new()
			{
				PortfolioName = message.PortfolioName,
			};
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);
		EnsureAuthenticated();
		if (!message.IsSubscribe)
		{
			RemoveOrderSubscription(message.OriginalTransactionId);
			return;
		}
		if (message.Count is <= 0)
		{
			await CompleteOrderStatusAsync(message, cancellationToken);
			return;
		}
		if (message.OrderId is not null)
			throw new NotSupportedException(
				"Paxos exchange identifiers are strings.");
		if (!message.UserId.IsEmpty())
			throw new NotSupportedException(
				"Paxos order history has no exchange-side user filter.");
		if (message.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Paxos history.");
		await RefreshProfilesAsync(cancellationToken);
		if (!message.PortfolioName.IsEmpty())
			_ = await GetPortfolioAsync(message.PortfolioName,
				cancellationToken);
		var subscription = new OrderSubscription
		{
			NativeId = message.OrderStringId,
			PortfolioName = message.PortfolioName,
			SecurityId = message.SecurityId,
			Side = message.Side,
			Volume = message.Volume,
			States = message.States,
			From = message.From?.EnsureUtc(),
			To = message.To?.EnsureUtc(),
			Skip = (message.Skip ?? 0).Max(0).Min(int.MaxValue).To<int>(),
			Maximum = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};
		await SendOrderSnapshotAsync(subscription, message.TransactionId, true,
			cancellationToken);
		if (message.IsHistoryOnly())
		{
			await CompleteOrderStatusAsync(message, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions[message.TransactionId] = subscription;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private static PortfolioReference[] SelectPortfolios(
		IEnumerable<PortfolioReference> portfolios, string portfolioName)
	{
		var selected = portfolios.Where(portfolio => portfolio is not null &&
			(portfolioName.IsEmpty() ||
				portfolio.Name.EqualsIgnoreCase(portfolioName) ||
				portfolio.Profile.Id.EqualsIgnoreCase(portfolioName) ||
				portfolio.Profile.Nickname.EqualsIgnoreCase(portfolioName))).ToArray();
		if (!portfolioName.IsEmpty() && selected.Length == 0)
			throw new InvalidOperationException(
				$"Unknown Paxos portfolio '{portfolioName}'.");
		return selected;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, IEnumerable<PortfolioReference> portfolios,
		CancellationToken cancellationToken)
	{
		foreach (var portfolio in portfolios)
		{
			var balances = await RestClient.GetBalancesAsync(portfolio.Profile.Id,
				cancellationToken);
			foreach (var balance in balances.Where(static balance =>
				balance?.Asset.IsEmpty() == false))
			{
				var available = balance.Available.ParsePaxosAmount();
				var trading = balance.Trading.ParsePaxosAmount();
				await SendBalanceAsync(target, isForced, portfolio.Name,
					balance.Asset, available + trading, trading,
					CurrentTime.EnsureUtc(), cancellationToken);
			}
		}
	}

	private async ValueTask SendBalanceAsync(long target, bool isForced,
		string portfolioName, string asset, decimal current, decimal blocked,
		DateTime serverTime, CancellationToken cancellationToken)
	{
		var fingerprint = new BalanceFingerprint(current, blocked);
		var key = $"{target}:{portfolioName}:{asset}";
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
			SecurityId = ToAssetSecurityId(asset),
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
		var selectedProfiles = SelectPortfolios(GetPortfolios(),
			subscription.PortfolioName);
		var tracked = subscription.NativeId.IsEmpty()
			? null
			: GetTrackedOperation(subscription.NativeId, null);
		var records = new List<HistoryRecord>();
		var executions = new List<PaxosPrivateExecution>();

		if (tracked is not null)
		{
			switch (tracked.Kind)
			{
				case NativeOperationKinds.Order:
				{
					var order = await RestClient.GetOrderAsync(tracked.ProfileId,
						tracked.NativeId, cancellationToken);
					records.Add(CreateHistoryRecord(order,
						tracked.PortfolioName));
					executions.AddRange(await RestClient.GetExecutionsAsync(
						tracked.ProfileId, tracked.NativeId, subscription.From,
						subscription.To, PageSize, requested, cancellationToken));
					break;
				}
				case NativeOperationKinds.Transfer:
				{
					var transfer = await RestClient.GetTransferAsync(
						tracked.NativeId, cancellationToken);
					records.Add(CreateHistoryRecord(transfer,
						tracked.PortfolioName));
					break;
				}
				case NativeOperationKinds.Conversion:
				{
					var conversion = await RestClient.GetStablecoinConversionAsync(
						tracked.NativeId, cancellationToken);
					records.Add(CreateHistoryRecord(conversion,
						tracked.PortfolioName));
					break;
				}
			}
		}
		else
		{
			foreach (var portfolio in selectedProfiles)
			{
				var profileId = portfolio.Profile.Id;
				var orders = await TryReadDomainAsync(ct =>
					RestClient.GetOrdersAsync(profileId, null, subscription.From,
						subscription.To, PageSize, requested, ct),
					Array.Empty<PaxosOrder>(), "order history", cancellationToken);
				var transfers = await TryReadDomainAsync(ct =>
					RestClient.GetTransfersAsync(profileId, null, subscription.From,
						subscription.To, PageSize, requested, ct),
					Array.Empty<PaxosTransfer>(), "transfer history",
					cancellationToken);
				var conversions = await TryReadDomainAsync(ct =>
					RestClient.GetStablecoinConversionsAsync(profileId, null,
						subscription.From, subscription.To, PageSize, requested, ct),
					Array.Empty<PaxosStablecoinConversion>(),
					"stablecoin conversion history", cancellationToken);
				records.AddRange(orders.Select(order =>
					CreateHistoryRecord(order, portfolio.Name)));
				records.AddRange(transfers.Select(transfer =>
					CreateHistoryRecord(transfer, portfolio.Name)));
				records.AddRange(conversions.Select(conversion =>
					CreateHistoryRecord(conversion, portfolio.Name)));
				executions.AddRange(await TryReadDomainAsync(ct =>
					RestClient.GetExecutionsAsync(profileId, null, subscription.From,
						subscription.To, PageSize, requested, ct),
					Array.Empty<PaxosPrivateExecution>(), "execution history",
					cancellationToken));
			}
		}

		var deliveredOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var selectedRecords = records.Where(record =>
			Matches(subscription, record)).OrderBy(static record => record.Time)
			.Skip(subscription.Skip).Take(subscription.Maximum).ToArray();
		foreach (var record in selectedRecords)
		{
			if (record.Order is not null)
			{
				var localId = GetTrackedOperation(record.Order.Id,
					record.Order.RefId)?.TransactionId ??
					ParseTransactionId(record.Order.RefId);
				TrackOrder(record.Order, localId, record.PortfolioName);
				await SendOrderAsync(record.Order, target, isForced,
					record.PortfolioName, cancellationToken);
				deliveredOrders.Add(record.Order.Id);
			}
			else if (record.Transfer is not null)
			{
				var previous = GetTrackedOperation(record.Transfer.Id,
					record.Transfer.RefId);
				TrackTransfer(record.Transfer, previous?.TransactionId ??
					ParseTransactionId(record.Transfer.RefId), record.PortfolioName,
					previous?.Operation ?? ToOperation(record.Transfer),
					record.Transfer.RefId);
				await SendTransferAsync(record.Transfer, target, isForced,
					record.PortfolioName, cancellationToken);
			}
			else if (record.Conversion is not null)
			{
				var previous = GetTrackedOperation(record.Conversion.Id,
					record.Conversion.RefId);
				TrackConversion(record.Conversion, previous?.TransactionId ??
					ParseTransactionId(record.Conversion.RefId), record.PortfolioName,
					record.Conversion.RefId);
				await SendConversionAsync(record.Conversion, target, isForced,
					record.PortfolioName, cancellationToken);
			}
		}

		foreach (var execution in executions.Where(execution => execution is not null &&
			deliveredOrders.Contains(execution.OrderId)).OrderBy(static execution =>
				execution.ExecutedAt.ToPaxosTime(DateTime.UnixEpoch)))
			await SendPrivateExecutionAsync(execution, target, cancellationToken);
	}

	private static HistoryRecord CreateHistoryRecord(PaxosOrder order,
		string portfolioName)
		=> new()
		{
			Time = order?.CreatedAt.ToPaxosTime(DateTime.UnixEpoch) ??
				DateTime.UnixEpoch,
			Order = order,
			PortfolioName = portfolioName,
		};

	private static HistoryRecord CreateHistoryRecord(PaxosTransfer transfer,
		string portfolioName)
		=> new()
		{
			Time = transfer?.CreatedAt.ToPaxosTime(DateTime.UnixEpoch) ??
				DateTime.UnixEpoch,
			Transfer = transfer,
			PortfolioName = portfolioName,
		};

	private static HistoryRecord CreateHistoryRecord(
		PaxosStablecoinConversion conversion, string portfolioName)
		=> new()
		{
			Time = conversion?.CreatedAt.ToPaxosTime(DateTime.UnixEpoch) ??
				DateTime.UnixEpoch,
			Conversion = conversion,
			PortfolioName = portfolioName,
		};

	private static bool Matches(OrderSubscription subscription,
		HistoryRecord record)
	{
		if (record is null)
			return false;
		string id;
		string security;
		Sides side;
		decimal volume;
		OrderStates state;
		if (record.Order is not null)
		{
			id = record.Order.Id;
			security = record.Order.Market;
			side = record.Order.Side == PaxosSides.Sell ? Sides.Sell : Sides.Buy;
			volume = GetOrderVolume(record.Order);
			state = record.Order.Status.ToOrderState();
		}
		else if (record.Transfer is not null)
		{
			id = record.Transfer.Id;
			security = record.Transfer.Asset;
			side = record.Transfer.Direction == PaxosTransferDirections.Credit
				? Sides.Buy
				: Sides.Sell;
			volume = record.Transfer.Amount.ParsePaxosAmount();
			state = record.Transfer.Status.ToOrderState();
		}
		else if (record.Conversion is not null)
		{
			id = record.Conversion.Id;
			security = record.Conversion.SourceAsset;
			side = Sides.Sell;
			volume = record.Conversion.Amount.ParsePaxosAmount();
			state = record.Conversion.Status.ToOrderState();
		}
		else
			return false;
		return (subscription.NativeId.IsEmpty() ||
				id.EqualsIgnoreCase(subscription.NativeId)) &&
			(subscription.SecurityId.SecurityCode.IsEmpty() ||
				security.EqualsIgnoreCase(subscription.SecurityId.SecurityCode)) &&
			(subscription.Side is null || subscription.Side == side) &&
			(subscription.Volume is null || subscription.Volume == volume) &&
			(subscription.States.Length == 0 ||
				subscription.States.Contains(state)) &&
			(subscription.From is null || record.Time >= subscription.From) &&
			(subscription.To is null || record.Time <= subscription.To);
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, PortfolioSubscription>[] portfolioTargets;
		KeyValuePair<long, OrderSubscription>[] orderTargets;
		KeyValuePair<string, long>[] activeOperations;
		using (_sync.EnterScope())
		{
			portfolioTargets = [.. _portfolioSubscriptions];
			orderTargets = [.. _orderSubscriptions];
			activeOperations = [.. _activeOperations];
		}
		if (portfolioTargets.Length > 0)
		{
			await RefreshProfilesAsync(cancellationToken);
			var portfolios = GetPortfolios();
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target.Key, false,
					SelectPortfolios(portfolios, target.Value.PortfolioName),
					cancellationToken);
		}
		foreach (var (id, localId) in activeOperations)
		{
			var tracked = GetTrackedOperation(id, null);
			if (tracked is null)
				continue;
			switch (tracked.Kind)
			{
				case NativeOperationKinds.Order:
				{
					var order = await RestClient.GetOrderAsync(tracked.ProfileId, id,
						cancellationToken);
					TrackOrder(order, localId, tracked.PortfolioName);
					await SendOrderAsync(order, localId, false,
						tracked.PortfolioName, cancellationToken);
					var executions = await RestClient.GetExecutionsAsync(
						tracked.ProfileId, id, null, null, PageSize, HistoryLimit,
						cancellationToken);
					foreach (var execution in executions.OrderBy(static item =>
						item.ExecutedAt.ToPaxosTime(DateTime.UnixEpoch)))
						await SendPrivateExecutionAsync(execution, localId,
							cancellationToken);
					break;
				}
				case NativeOperationKinds.Transfer:
				{
					var transfer = await RestClient.GetTransferAsync(id,
						cancellationToken);
					TrackTransfer(transfer, localId, tracked.PortfolioName,
						tracked.Operation, tracked.RefId);
					await SendTransferAsync(transfer, localId, false,
						tracked.PortfolioName, cancellationToken);
					break;
				}
				case NativeOperationKinds.Conversion:
				{
					var conversion = await RestClient.GetStablecoinConversionAsync(id,
						cancellationToken);
					TrackConversion(conversion, localId, tracked.PortfolioName,
						tracked.RefId);
					await SendConversionAsync(conversion, localId, false,
						tracked.PortfolioName, cancellationToken);
					break;
				}
			}
		}
		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);
	}

	private void TrackOrder(PaxosOrder order, long localTransactionId,
		string portfolioName)
	{
		if (order?.Id.IsEmpty() != false)
			return;
		var previous = GetTrackedOperation(order.Id, order.RefId);
		if (localTransactionId == 0)
			localTransactionId = previous?.TransactionId ??
				ParseTransactionId(order.RefId);
		portfolioName = portfolioName.IsEmpty()
			? previous?.PortfolioName ?? GetPortfolioName(order.ProfileId)
			: portfolioName;
		TrackOperation(new()
		{
			NativeId = order.Id,
			RefId = order.RefId,
			ProfileId = order.ProfileId,
			PortfolioName = portfolioName,
			SecurityId = ToSecurityId(order.Market),
			TransactionId = localTransactionId,
			Kind = NativeOperationKinds.Order,
			Operation = PaxosOperations.Trade,
		}, !order.Status.IsFinal());
	}

	private void TrackTransfer(PaxosTransfer transfer, long localTransactionId,
		string portfolioName, PaxosOperations operation, string refId)
	{
		if (transfer?.Id.IsEmpty() != false)
			return;
		var previous = GetTrackedOperation(transfer.Id, transfer.RefId);
		if (localTransactionId == 0)
			localTransactionId = previous?.TransactionId ??
				ParseTransactionId(transfer.RefId);
		portfolioName = portfolioName.IsEmpty()
			? previous?.PortfolioName ?? GetPortfolioName(transfer.ProfileId)
			: portfolioName;
		TrackOperation(new()
		{
			NativeId = transfer.Id,
			RefId = transfer.RefId.IsEmpty() ? refId : transfer.RefId,
			ProfileId = transfer.ProfileId,
			PortfolioName = portfolioName,
			SecurityId = ToAssetSecurityId(transfer.Asset),
			TransactionId = localTransactionId,
			Kind = NativeOperationKinds.Transfer,
			Operation = operation,
		}, !transfer.Status.IsFinal());
	}

	private void TrackConversion(PaxosStablecoinConversion conversion,
		long localTransactionId, string portfolioName, string refId)
	{
		if (conversion?.Id.IsEmpty() != false)
			return;
		var previous = GetTrackedOperation(conversion.Id, conversion.RefId);
		if (localTransactionId == 0)
			localTransactionId = previous?.TransactionId ??
				ParseTransactionId(conversion.RefId);
		portfolioName = portfolioName.IsEmpty()
			? previous?.PortfolioName ?? GetPortfolioName(conversion.ProfileId)
			: portfolioName;
		TrackOperation(new()
		{
			NativeId = conversion.Id,
			RefId = conversion.RefId.IsEmpty() ? refId : conversion.RefId,
			ProfileId = conversion.ProfileId,
			PortfolioName = portfolioName,
			SecurityId = ToAssetSecurityId(conversion.SourceAsset),
			TransactionId = localTransactionId,
			Kind = NativeOperationKinds.Conversion,
			Operation = PaxosOperations.StablecoinConversion,
		}, !conversion.Status.IsFinal());
	}

	private async ValueTask SendOrderAsync(PaxosOrder order, long target,
		bool isForced, string portfolioName,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(order);
		var id = order.Id.ThrowIfEmpty(nameof(order.Id));
		var fingerprint = new OrderFingerprint(order.Status, order.AmountFilled,
			order.ModifiedAt);
		var key = $"{target}:{id}";
		using (_sync.EnterScope())
		{
			if (!isForced && _orderFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_orderFingerprints[key] = fingerprint;
		}
		var tracked = GetTrackedOperation(id, order.RefId);
		var state = order.Status.ToOrderState();
		var volume = GetOrderVolume(order);
		var filled = order.AmountFilled.ParsePaxosAmount();
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = ToSecurityId(order.Market),
			ServerTime = order.ModifiedAt.ToPaxosTime(
				order.CreatedAt.ToPaxosTime(CurrentTime.EnsureUtc())),
			PortfolioName = portfolioName.IsEmpty()
				? tracked?.PortfolioName ?? GetPortfolioName(order.ProfileId)
				: portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = state is OrderStates.Active or OrderStates.Pending &&
				!order.BaseAmount.IsEmpty()
					? (volume - filled).Max(0m)
					: state is OrderStates.Active or OrderStates.Pending
						? null
						: 0m,
			OrderPrice = order.Price.ParsePaxosAmount(),
			OrderType = order.Type.ToOrderType(),
			OrderState = state,
			OrderStringId = id,
			OrderBoardId = order.RecipientProfileId,
			UserOrderId = order.RefId,
			TransactionId = tracked?.TransactionId ??
				ParseTransactionId(order.RefId),
			OriginalTransactionId = target,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			ExpiryDate = order.ExpirationDate.IsEmpty()
				? null
				: order.ExpirationDate.ToPaxosTime(default),
			Comment = order.IsTriggered ? "Stop order triggered." : null,
		};
		if (state == OrderStates.Failed)
			message.Error = new InvalidOperationException(
				$"Paxos rejected order {id}.");
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask SendTransferAsync(PaxosTransfer transfer,
		long target, bool isForced, string portfolioName,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(transfer);
		var id = transfer.Id.ThrowIfEmpty(nameof(transfer.Id));
		var fingerprint = new TransferFingerprint(transfer.Status,
			transfer.UpdatedAt, transfer.CryptoTransactionHash);
		var key = $"{target}:{id}";
		using (_sync.EnterScope())
		{
			if (!isForced && _transferFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_transferFingerprints[key] = fingerprint;
		}
		var tracked = GetTrackedOperation(id, transfer.RefId);
		var state = transfer.Status.ToOrderState();
		var volume = transfer.Amount.ParsePaxosAmount();
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = ToAssetSecurityId(transfer.Asset),
			ServerTime = transfer.UpdatedAt.ToPaxosTime(
				transfer.CreatedAt.ToPaxosTime(CurrentTime.EnsureUtc())),
			PortfolioName = portfolioName.IsEmpty()
				? tracked?.PortfolioName ?? GetPortfolioName(transfer.ProfileId)
				: portfolioName,
			Side = transfer.Direction == PaxosTransferDirections.Credit
				? Sides.Buy
				: Sides.Sell,
			OrderVolume = volume,
			Balance = state is OrderStates.Active or OrderStates.Pending
				? volume
				: 0m,
			OrderPrice = 0m,
			OrderType = OrderTypes.Conditional,
			OrderState = state,
			OrderStringId = id,
			OrderBoardId = transfer.CryptoTransactionHash.IsEmpty()
				? transfer.GroupId
				: transfer.CryptoTransactionHash,
			UserOrderId = transfer.RefId,
			TransactionId = tracked?.TransactionId ??
				ParseTransactionId(transfer.RefId),
			OriginalTransactionId = target,
			Commission = transfer.Fee.ParsePaxosAmount(),
			CommissionCurrency = transfer.Asset,
			Comment = transfer.Memo,
		};
		if (state == OrderStates.Failed)
			message.Error = new InvalidOperationException(
				$"Paxos transfer {id} failed.");
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask SendConversionAsync(
		PaxosStablecoinConversion conversion, long target, bool isForced,
		string portfolioName, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(conversion);
		var id = conversion.Id.ThrowIfEmpty(nameof(conversion.Id));
		var fingerprint = new ConversionFingerprint(conversion.Status,
			conversion.UpdatedAt);
		var key = $"{target}:{id}";
		using (_sync.EnterScope())
		{
			if (!isForced && _conversionFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_conversionFingerprints[key] = fingerprint;
		}
		var tracked = GetTrackedOperation(id, conversion.RefId);
		var state = conversion.Status.ToOrderState();
		var volume = conversion.Amount.ParsePaxosAmount();
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = ToAssetSecurityId(conversion.SourceAsset),
			ServerTime = conversion.UpdatedAt.ToPaxosTime(
				conversion.CreatedAt.ToPaxosTime(CurrentTime.EnsureUtc())),
			PortfolioName = portfolioName.IsEmpty()
				? tracked?.PortfolioName ?? GetPortfolioName(conversion.ProfileId)
				: portfolioName,
			Side = Sides.Sell,
			OrderVolume = volume,
			Balance = state is OrderStates.Active or OrderStates.Pending
				? volume
				: 0m,
			OrderPrice = 1m,
			OrderType = OrderTypes.Conditional,
			OrderState = state,
			OrderStringId = id,
			OrderBoardId = conversion.TargetAsset,
			UserOrderId = conversion.RefId,
			TransactionId = tracked?.TransactionId ??
				ParseTransactionId(conversion.RefId),
			OriginalTransactionId = target,
			Comment = $"{conversion.SourceAsset} -> {conversion.TargetAsset}",
		};
		if (state == OrderStates.Failed)
			message.Error = new InvalidOperationException(
				$"Paxos stablecoin conversion {id} failed.");
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask SendPrivateExecutionAsync(
		PaxosPrivateExecution execution, long target,
		CancellationToken cancellationToken)
	{
		if (execution?.ExecutionId.IsEmpty() != false)
			return;
		var key = $"{target}:{execution.ExecutionId}";
		using (_sync.EnterScope())
			if (!_seenPrivateExecutions.Add(key))
				return;
		var tracked = GetTrackedOperation(execution.OrderId, null);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = ToSecurityId(execution.Market),
			ServerTime = execution.ExecutedAt.ToPaxosTime(
				CurrentTime.EnsureUtc()),
			PortfolioName = tracked?.PortfolioName,
			Side = execution.Side.ToStockSharp(),
			TradeStringId = execution.ExecutionId,
			TradePrice = execution.Price.ParsePaxosAmount(),
			TradeVolume = execution.Amount.ParsePaxosAmount(),
			OrderStringId = execution.OrderId,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = target,
			Commission = execution.Commission.ParsePaxosAmount(),
			CommissionCurrency = execution.CommissionAsset,
		}, cancellationToken);
	}

	private string GetPortfolioName(string profileId)
	{
		if (profileId.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _portfolios.Values.FirstOrDefault(item =>
				item.Profile.Id.EqualsIgnoreCase(profileId))?.Name ??
				"Paxos_" + profileId;
	}

	private static decimal GetOrderVolume(PaxosOrder order)
	{
		var baseAmount = order.BaseAmount.ParsePaxosAmount();
		return baseAmount > 0
			? baseAmount
			: order.QuoteAmount.ParsePaxosAmount();
	}

	private static PaxosOperations ToOperation(PaxosTransfer transfer)
		=> transfer.Type switch
		{
			PaxosTransferTypes.InternalTransferDebit or
			PaxosTransferTypes.InternalTransferCredit =>
				PaxosOperations.InternalTransfer,
			PaxosTransferTypes.PaxosTransferDebit or
			PaxosTransferTypes.PaxosTransferCredit =>
				PaxosOperations.PaxosTransfer,
			_ => PaxosOperations.CryptoWithdrawal,
		};

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
			RemoveFingerprintPrefix(_orderFingerprints, target);
			RemoveFingerprintPrefix(_transferFingerprints, target);
			RemoveFingerprintPrefix(_conversionFingerprints, target);
			var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
			_seenPrivateExecutions.RemoveWhere(key => key.StartsWith(prefix,
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
