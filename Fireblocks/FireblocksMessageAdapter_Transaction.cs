namespace StockSharp.Fireblocks;

public partial class FireblocksMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		if (!ValidateSecurityId(regMsg.SecurityId))
			throw new InvalidOperationException(
				$"Security board '{regMsg.SecurityId.BoardCode}' is not " +
				"Fireblocks.");
		if (regMsg.OrderType != OrderTypes.Conditional ||
			regMsg.Condition is not FireblocksOrderCondition condition ||
			!condition.IsWithdraw)
			throw new NotSupportedException(
				"Fireblocks accepts only conditional withdrawal requests.");
		if (regMsg.Side != Sides.Sell)
			throw new NotSupportedException(
				"A Fireblocks outgoing transfer must use the sell side.");
		if (regMsg.PostOnly == true || regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Post-only and time-in-force are not applicable to transfers.");
		var vault = GetVault(regMsg.PortfolioName);
		var assetId = regMsg.SecurityId.SecurityCode.ThrowIfEmpty(
			nameof(regMsg.SecurityId)).Trim();
		var amount = regMsg.Volume.Abs();
		if (amount <= 0)
			throw new InvalidOperationException(
				"Fireblocks transfer amount must be positive.");
		var destination = CreateDestination(condition);
		var externalId = CreateExternalTransactionId(regMsg);
		var request = new FireblocksTransactionRequest
		{
			ExternalTransactionId = externalId,
			AssetId = assetId,
			Source = new()
			{
				Type = FireblocksPeerTypes.VaultAccount,
				Id = vault.Id,
			},
			Destination = destination,
			Amount = amount.ToString(CultureInfo.InvariantCulture),
			IsGrossAmount = condition.IsGrossAmount,
			FeeLevel = condition.FeeLevel,
			Note = condition.Note.IsEmpty() ? regMsg.Comment : condition.Note,
		};

		FireblocksTransaction transaction = null;
		FireblocksCreateTransactionResponse created = null;
		try
		{
			created = await RestClient.CreateTransactionAsync(request,
				cancellationToken);
		}
		catch (FireblocksApiException error) when (
			error.StatusCode is HttpStatusCode.BadRequest or
			HttpStatusCode.Conflict)
		{
			transaction = await RestClient.TryGetTransactionByExternalIdAsync(
				externalId, cancellationToken);
			if (transaction is null)
				throw;
		}
		catch (Exception error) when (
			!cancellationToken.IsCancellationRequested &&
			error is HttpRequestException or TaskCanceledException)
		{
			transaction = await RestClient.TryGetTransactionByExternalIdAsync(
				externalId, cancellationToken);
			if (transaction is null)
				throw;
		}

		if (transaction is null)
		{
			if (created is null)
				throw new InvalidDataException(
					"Fireblocks returned an empty create-transaction response.");
			var fireblocksId = created.Id.ThrowIfEmpty(
				nameof(FireblocksCreateTransactionResponse.Id));
			foreach (var message in created.SystemMessages ?? [])
				if (!message.Message.IsEmpty())
					this.AddWarningLog("Fireblocks {0}: {1}", message.Type,
						message.Message);
			try
			{
				transaction = await RestClient.GetTransactionAsync(fireblocksId,
					cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				this.AddWarningLog(
					"Fireblocks transaction {0} was created but its detailed " +
					"snapshot is not available yet: {1}", fireblocksId,
					error.Message);
				transaction = new()
				{
					Id = fireblocksId,
					ExternalTransactionId = externalId,
					Status = created.Status,
					Operation = FireblocksTransactionOperations.Transfer,
					AssetId = assetId,
					Source = new()
					{
						Type = FireblocksPeerTypes.VaultAccount,
						Id = vault.Id,
					},
					Destination = new()
					{
						Type = destination.Type,
						Id = destination.Id,
					},
					DestinationAddress = destination.OneTimeAddress?.Address,
					DestinationTag = destination.OneTimeAddress?.Tag,
					AmountInfo = new()
					{
						Amount = request.Amount,
						RequestedAmount = request.Amount,
					},
					CreatedAt = (CurrentTime.EnsureUtc() - DateTime.UnixEpoch)
						.TotalMilliseconds.To<decimal>(),
				};
			}
		}

		TrackTransaction(transaction, regMsg.TransactionId);
		await SendTransactionAsync(transaction, regMsg.TransactionId, true,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		var fireblocksId = cancelMsg.OrderStringId;
		if (fireblocksId.IsEmpty())
		{
			using (_sync.EnterScope())
				_fireblocksTransactionIds.TryGetValue(
					cancelMsg.OriginalTransactionId, out fireblocksId);
		}
		if (fireblocksId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					cancelMsg.OriginalTransactionId));
		await RestClient.CancelTransactionAsync(fireblocksId, cancellationToken);
		var transaction = await RestClient.GetTransactionAsync(fireblocksId,
			cancellationToken);
		var localId = GetLocalTransactionId(transaction,
			cancelMsg.OriginalTransactionId);
		TrackTransaction(transaction, localId);
		await SendTransactionAsync(transaction, cancelMsg.TransactionId, true,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Fireblocks transfers cannot be replaced through this endpoint.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Fireblocks has no atomic transfer-group cancellation endpoint.");
	}

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
		var accounts = await RefreshVaultsAsync(cancellationToken);
		var selected = SelectVaults(accounts, lookupMsg.PortfolioName);
		foreach (var account in selected)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = account.GetPortfolioName(),
				BoardCode = BoardCodes.Fireblocks,
				ClientCode = account.Name,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
			selected, cancellationToken);
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
			_ = GetVault(statusMsg.PortfolioName);
		if (statusMsg.OrderId is not null)
			throw new NotSupportedException(
				"Fireblocks transaction identifiers are strings.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Fireblocks order history has no exchange-side user filter.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Fireblocks history.");
		var subscription = new OrderSubscription
		{
			FireblocksId = statusMsg.OrderStringId,
			PortfolioName = statusMsg.PortfolioName,
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			Volume = statusMsg.Volume,
			States = statusMsg.States,
			From = statusMsg.From?.EnsureUtc(),
			To = statusMsg.To?.EnsureUtc(),
			Skip = Math.Max(0, statusMsg.Skip ?? 0).Min(int.MaxValue).To<int>(),
			Maximum = (statusMsg.Count ?? HistoryLimit).Min(HistoryLimit)
				.Max(1).To<int>(),
		};
		await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId,
			true, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions[statusMsg.TransactionId] = subscription;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private static FireblocksTransferPeer CreateDestination(
		FireblocksOrderCondition condition)
	{
		var type = condition.DestinationType;
		if (type is FireblocksPeerTypes.Unknown or
			FireblocksPeerTypes.Compound or
			FireblocksPeerTypes.ProgramCall or
			FireblocksPeerTypes.MultiDestination)
			throw new NotSupportedException(
				$"Fireblocks destination type '{type}' is not supported for a " +
				"single transfer.");
		if (type == FireblocksPeerTypes.OneTimeAddress)
		{
			if (condition.WithdrawInfo.Type != WithdrawTypes.Crypto)
				throw new NotSupportedException(
					"A Fireblocks one-time destination must be a crypto address.");
			return new()
			{
				Type = type,
				OneTimeAddress = new()
				{
					Address = condition.WithdrawInfo.CryptoAddress.ThrowIfEmpty(
						nameof(WithdrawInfo.CryptoAddress)).Trim(),
					Tag = condition.WithdrawInfo.PaymentId?.Trim(),
				},
			};
		}
		return new()
		{
			Type = type,
			Id = condition.DestinationId.ThrowIfEmpty(
				nameof(condition.DestinationId)).Trim(),
		};
	}

	private static string CreateExternalTransactionId(
		OrderRegisterMessage message)
	{
		var value = message.UserOrderId.IsEmpty()
			? "stocksharp-" + message.TransactionId.ToString(
				CultureInfo.InvariantCulture)
			: message.UserOrderId.Trim();
		if (value.Length > 255)
			throw new ArgumentOutOfRangeException(nameof(message.UserOrderId),
				value, "Fireblocks external transaction ID cannot exceed 255 " +
				"characters.");
		return value;
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, PortfolioSubscription>[] portfolioTargets;
		KeyValuePair<long, OrderSubscription>[] orderTargets;
		KeyValuePair<string, long>[] active;
		using (_sync.EnterScope())
		{
			portfolioTargets = [.. _portfolioSubscriptions];
			orderTargets = [.. _orderSubscriptions];
			active = [.. _activeTransactions];
		}

		if (portfolioTargets.Length > 0)
		{
			var accounts = await RefreshVaultsAsync(cancellationToken);
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target.Key, false,
					SelectVaults(accounts, target.Value.PortfolioName),
					cancellationToken);
		}

		foreach (var item in active)
		{
			var transaction = await RestClient.GetTransactionAsync(item.Key,
				cancellationToken);
			TrackTransaction(transaction, item.Value);
			await SendTransactionAsync(transaction, item.Value, false,
				cancellationToken);
		}

		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);
	}

	private static FireblocksVaultAccount[] SelectVaults(
		IEnumerable<FireblocksVaultAccount> accounts, string portfolioName)
	{
		var result = accounts.Where(account => account is not null &&
			(portfolioName.IsEmpty() || account.GetPortfolioName()
				.EqualsIgnoreCase(portfolioName))).ToArray();
		if (!portfolioName.IsEmpty() && result.Length == 0)
			throw new InvalidOperationException(
				$"Unknown Fireblocks portfolio '{portfolioName}'.");
		return result;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, IEnumerable<FireblocksVaultAccount> accounts,
		CancellationToken cancellationToken)
	{
		foreach (var account in accounts)
		{
			var portfolioName = account.GetPortfolioName();
			foreach (var asset in account.Assets ?? [])
			{
				if (asset.Id.IsEmpty())
					continue;
				var current = (asset.Total.IsEmpty()
					? asset.Balance
					: asset.Total).ParseFireblocksAmount();
				var available = asset.Available.IsEmpty()
					? current
					: asset.Available.ParseFireblocksAmount();
				var blocked = Math.Max(0m, current - available);
				var fingerprint = new BalanceFingerprint(current, blocked);
				var key = $"{target}:{portfolioName}:{asset.Id}";
				using (_sync.EnterScope())
				{
					if (!isForced && _balanceFingerprints.TryGetValue(key,
						out var previous) && previous == fingerprint)
						continue;
					_balanceFingerprints[key] = fingerprint;
				}
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = portfolioName,
					SecurityId = new()
					{
						SecurityCode = asset.Id,
						BoardCode = BoardCodes.Fireblocks,
					},
					ServerTime = CurrentTime.EnsureUtc(),
					OriginalTransactionId = target,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, current, true)
				.TryAdd(PositionChangeTypes.BlockedValue, blocked, true),
					cancellationToken);
			}
		}
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		FireblocksTransaction[] transactions;
		if (!subscription.FireblocksId.IsEmpty())
		{
			transactions = [await RestClient.GetTransactionAsync(
				subscription.FireblocksId, cancellationToken)];
		}
		else
		{
			var vaultId = subscription.PortfolioName.IsEmpty()
				? null
				: GetVault(subscription.PortfolioName).Id;
			var requested = (int)Math.Min(HistoryLimit,
				(long)subscription.Skip + subscription.Maximum);
			requested = Math.Max(1, requested);
			var outgoing = await RestClient.GetTransactionsAsync(
				subscription.From, subscription.To, vaultId, null,
				subscription.SecurityId.SecurityCode, requested,
				cancellationToken);
			if (vaultId.IsEmpty())
				transactions = outgoing;
			else
			{
				var incoming = await RestClient.GetTransactionsAsync(
					subscription.From, subscription.To, null, vaultId,
					subscription.SecurityId.SecurityCode, requested,
					cancellationToken);
				transactions = [.. outgoing.Concat(incoming)
					.Where(static item => item is not null && !item.Id.IsEmpty())
					.GroupBy(static item => item.Id,
						StringComparer.OrdinalIgnoreCase)
					.Select(static group => group.OrderByDescending(item =>
						item.LastUpdated ?? 0m).First())];
			}
		}

		var skipped = 0;
		var delivered = 0;
		foreach (var transaction in transactions
			.Where(transaction => Matches(subscription, transaction))
			.OrderBy(transaction => transaction.CreatedAt ?? 0m))
		{
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			var localId = GetLocalTransactionId(transaction, 0);
			TrackTransaction(transaction, localId);
			await SendTransactionAsync(transaction, target, isForced,
				cancellationToken, subscription.PortfolioName);
		}
	}

	private async ValueTask SendTransactionAsync(
		FireblocksTransaction transaction, long target, bool isForced,
		CancellationToken cancellationToken, string portfolioName = null)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		var key = $"{target}:{transaction.Id}";
		var fingerprint = new TransactionFingerprint(transaction.Status,
			transaction.LastUpdated, transaction.SubStatus,
			transaction.TransactionHash);
		using (_sync.EnterScope())
		{
			if (!isForced && _transactionFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_transactionFingerprints[key] = fingerprint;
		}

		var state = transaction.Status.ToOrderState();
		var amount = GetAmount(transaction);
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = new()
			{
				SecurityCode = transaction.AssetId,
				BoardCode = BoardCodes.Fireblocks,
			},
			ServerTime = transaction.LastUpdated.ToFireblocksTime(
				transaction.CreatedAt.ToFireblocksTime(
					CurrentTime.EnsureUtc())),
			PortfolioName = GetTransactionPortfolio(transaction, portfolioName),
			Side = GetTransactionSide(transaction, portfolioName),
			OrderVolume = amount,
			Balance = state == OrderStates.Active ? amount : 0m,
			OrderPrice = 0m,
			OrderType = OrderTypes.Conditional,
			OrderState = state,
			OrderStringId = transaction.Id,
			OrderBoardId = transaction.TransactionHash,
			UserOrderId = transaction.ExternalTransactionId,
			TransactionId = GetLocalTransactionId(transaction, 0),
			OriginalTransactionId = target,
			Comment = transaction.Note,
			Commission = GetCommission(transaction),
			CommissionCurrency = transaction.FeeCurrency,
		};
		if (state == OrderStates.Failed)
			message.Error = new InvalidOperationException(
				GetFailureMessage(transaction));
		await SendOutMessageAsync(message, cancellationToken);
	}

	private void TrackTransaction(FireblocksTransaction transaction,
		long localTransactionId)
	{
		if (transaction?.Id.IsEmpty() != false)
			return;
		if (localTransactionId == 0)
			localTransactionId = ParseLocalTransactionId(
				transaction.ExternalTransactionId);
		using (_sync.EnterScope())
		{
			if (localTransactionId != 0)
			{
				_localTransactionIds[transaction.Id] = localTransactionId;
				_fireblocksTransactionIds[localTransactionId] = transaction.Id;
			}
			if (!transaction.Status.IsFinal() && localTransactionId != 0)
				_activeTransactions[transaction.Id] = localTransactionId;
			else
				_activeTransactions.Remove(transaction.Id);
		}
	}

	private long GetLocalTransactionId(FireblocksTransaction transaction,
		long fallback)
	{
		if (transaction is null)
			return fallback;
		using (_sync.EnterScope())
			if (!transaction.Id.IsEmpty() &&
				_localTransactionIds.TryGetValue(transaction.Id, out var value))
				return value;
		var parsed = ParseLocalTransactionId(transaction.ExternalTransactionId);
		return parsed == 0 ? fallback : parsed;
	}

	private static long ParseLocalTransactionId(string externalId)
	{
		const string prefix = "stocksharp-";
		if (externalId.IsEmpty() ||
			!externalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return 0;
		return long.TryParse(externalId[prefix.Length..],
			NumberStyles.None, CultureInfo.InvariantCulture, out var value)
			? value
			: 0;
	}

	private static bool Matches(OrderSubscription subscription,
		FireblocksTransaction transaction)
	{
		if (transaction is null || transaction.Operation !=
			FireblocksTransactionOperations.Transfer || transaction.Id.IsEmpty() ||
			transaction.AssetId.IsEmpty())
			return false;
		if (transaction.Source?.Type != FireblocksPeerTypes.VaultAccount &&
			transaction.Destination?.Type != FireblocksPeerTypes.VaultAccount)
			return false;
		if (!subscription.FireblocksId.IsEmpty() &&
			!subscription.FireblocksId.EqualsIgnoreCase(transaction.Id))
			return false;
		if (!subscription.SecurityId.BoardCode.IsEmpty() &&
			!subscription.SecurityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.Fireblocks))
			return false;
		if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
			!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
				transaction.AssetId))
			return false;
		if (!subscription.PortfolioName.IsEmpty() &&
			!subscription.PortfolioName.EqualsIgnoreCase(
				GetTransactionPortfolio(transaction,
					subscription.PortfolioName)))
			return false;
		if (subscription.Side is Sides side &&
			GetTransactionSide(transaction, subscription.PortfolioName) != side)
			return false;
		if (subscription.Volume is decimal volume &&
			GetAmount(transaction) != volume)
			return false;
		var state = transaction.Status.ToOrderState();
		if (subscription.States is { Length: > 0 } states &&
			!states.Contains(state))
			return false;
		var time = transaction.CreatedAt.ToFireblocksTime(DateTime.UnixEpoch);
		return (subscription.From is null || time >= subscription.From) &&
			(subscription.To is null || time <= subscription.To);
	}

	private static string GetTransactionPortfolio(
		FireblocksTransaction transaction, string preferredPortfolio = null)
	{
		if (!preferredPortfolio.IsEmpty())
			return IsPortfolioPeer(transaction.Source, preferredPortfolio) ||
				IsPortfolioPeer(transaction.Destination, preferredPortfolio)
					? preferredPortfolio
					: null;
		if (transaction.Source?.Type == FireblocksPeerTypes.VaultAccount &&
			!transaction.Source.Id.IsEmpty())
			return FireblocksExtensions.GetPortfolioName(transaction.Source.Id);
		if (transaction.Destination?.Type == FireblocksPeerTypes.VaultAccount &&
			!transaction.Destination.Id.IsEmpty())
			return FireblocksExtensions.GetPortfolioName(
				transaction.Destination.Id);
		return null;
	}

	private static Sides GetTransactionSide(FireblocksTransaction transaction,
		string portfolioName = null)
	{
		if (!portfolioName.IsEmpty())
		{
			if (IsPortfolioPeer(transaction.Source, portfolioName))
				return Sides.Sell;
			if (IsPortfolioPeer(transaction.Destination, portfolioName))
				return Sides.Buy;
		}
		return transaction.Source?.Type == FireblocksPeerTypes.VaultAccount
			? Sides.Sell
			: Sides.Buy;
	}

	private static bool IsPortfolioPeer(FireblocksTransferPeerResponse peer,
		string portfolioName)
		=> peer?.Type == FireblocksPeerTypes.VaultAccount &&
			!peer.Id.IsEmpty() && FireblocksExtensions.GetPortfolioName(peer.Id)
				.EqualsIgnoreCase(portfolioName);

	private static decimal GetAmount(FireblocksTransaction transaction)
		=> (transaction.AmountInfo?.RequestedAmount.IsEmpty() == false
			? transaction.AmountInfo.RequestedAmount
			: transaction.AmountInfo?.Amount).ParseFireblocksAmount();

	private static decimal? GetCommission(FireblocksTransaction transaction)
	{
		if (transaction.FeeInfo is null)
			return null;
		var network = transaction.FeeInfo.NetworkFee.ParseFireblocksAmount();
		var service = transaction.FeeInfo.ServiceFee.ParseFireblocksAmount();
		return network == 0m && service == 0m ? null : network + service;
	}

	private static string GetFailureMessage(FireblocksTransaction transaction)
	{
		if (!transaction.ErrorDescription.IsEmpty())
			return transaction.ErrorDescription;
		if (!transaction.SubStatus.IsEmpty())
			return $"Fireblocks transaction {transaction.Status}: " +
				transaction.SubStatus;
		return $"Fireblocks transaction ended with {transaction.Status}.";
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
			RemoveFingerprintPrefix(_transactionFingerprints, target);
		}
	}

	private static void RemoveFingerprintPrefix<T>(
		Dictionary<string, T> fingerprints, long target)
	{
		var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
		foreach (var key in fingerprints.Keys.Where(key => key.StartsWith(
			prefix, StringComparison.Ordinal)).ToArray())
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
