namespace StockSharp.Copper;

public partial class CopperMessageAdapter
{
	private readonly record struct BalanceSnapshot(CopperWallet[] Wallets,
		CopperClearLoopBalance[] ClearLoopBalances);

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		if (!ValidateSecurityId(regMsg.SecurityId))
			throw new InvalidOperationException(
				$"Security board '{regMsg.SecurityId.BoardCode}' is not Copper.");
		if (regMsg.OrderType != OrderTypes.Conditional ||
			regMsg.Condition is not CopperOrderCondition condition ||
			!condition.IsWithdraw)
			throw new NotSupportedException(
				"Copper accepts conditional withdrawal requests only.");
		if (regMsg.Side != Sides.Sell)
			throw new NotSupportedException(
				"A Copper outgoing transfer must use the sell side.");
		if (regMsg.PostOnly == true || regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Post-only and time-in-force are not applicable to transfers.");
		var portfolio = GetPortfolio(regMsg.PortfolioName);
		var currency = regMsg.SecurityId.SecurityCode.ThrowIfEmpty(
			nameof(regMsg.SecurityId)).Trim();
		if (!IsKnownCurrency(currency))
			this.AddWarningLog("Copper currency {0} was not present in the last " +
				"reference-data snapshot.", currency);
		var amount = regMsg.Volume.Abs();
		if (amount <= 0)
			throw new InvalidOperationException(
				"Copper transfer amount must be positive.");
		var externalId = CreateExternalOrderId(regMsg);
		var request = CreateOrderRequest(regMsg, condition, portfolio, currency,
			amount, externalId);

		CopperOrder order;
		try
		{
			order = await RestClient.CreateOrderAsync(request, cancellationToken);
		}
		catch (CopperApiException error) when (error.StatusCode is
			HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
		{
			order = await RestClient.TryGetOrderByExternalIdAsync(externalId,
				cancellationToken);
			if (order is null)
				throw;
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
			error is HttpRequestException or TaskCanceledException)
		{
			order = await RestClient.TryGetOrderByExternalIdAsync(externalId,
				cancellationToken);
			if (order is null)
				throw;
		}

		if (order is null || order.Id.IsEmpty())
			throw new InvalidDataException(
				"Copper returned an incomplete create-order response.");
		PopulateCreatedOrder(order, request);
		TrackOrder(order, regMsg.TransactionId);
		await SendOrderAsync(order, regMsg.TransactionId, true,
			cancellationToken, regMsg.PortfolioName);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		var copperId = cancelMsg.OrderStringId;
		if (copperId.IsEmpty())
		{
			using (_sync.EnterScope())
				_copperOrderIds.TryGetValue(cancelMsg.OriginalTransactionId,
					out copperId);
		}
		if (copperId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					cancelMsg.OriginalTransactionId));
		var order = await RestClient.CancelOrderAsync(copperId, null,
			cancellationToken);
		order ??= await RestClient.GetOrderAsync(copperId, cancellationToken);
		var localId = GetLocalTransactionId(order,
			cancelMsg.OriginalTransactionId);
		TrackOrder(order, localId);
		await SendOrderAsync(order, cancelMsg.TransactionId, true,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Copper custody orders cannot be replaced.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Copper has no atomic custody-order group cancellation endpoint.");
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
		var portfolios = await RefreshPortfoliosAsync(cancellationToken);
		var selected = SelectPortfolios(portfolios, lookupMsg.PortfolioName);
		foreach (var portfolio in selected)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = portfolio.Name,
				BoardCode = BoardCodes.Copper,
				ClientCode = portfolio.PortfolioName,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}
		var balances = await GetBalanceSnapshotAsync(cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true, selected,
			balances, cancellationToken);
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
				"Copper order identifiers are strings.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Copper order history has no exchange-side user filter.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Copper history.");
		var subscription = new OrderSubscription
		{
			CopperId = statusMsg.OrderStringId,
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

	private CopperCreateOrderRequest CreateOrderRequest(
		OrderRegisterMessage message, CopperOrderCondition condition,
		PortfolioReference portfolio, string currency, decimal amount,
		string externalId)
	{
		string toAddress = null;
		string toCryptoAddressId = null;
		string toPortfolioId = null;
		switch (condition.DestinationType)
		{
			case CopperDestinationTypes.ExternalAddress:
				if (condition.WithdrawInfo.Type != WithdrawTypes.Crypto)
					throw new NotSupportedException(
						"A Copper external destination must be a crypto address.");
				toAddress = condition.WithdrawInfo.CryptoAddress.ThrowIfEmpty(
					nameof(WithdrawInfo.CryptoAddress)).Trim();
				break;
			case CopperDestinationTypes.AddressBook:
				toCryptoAddressId = condition.DestinationId.ThrowIfEmpty(
					nameof(condition.DestinationId)).Trim();
				break;
			case CopperDestinationTypes.Portfolio:
				toPortfolioId = ResolveDestinationPortfolioId(
					condition.DestinationId);
				break;
			default:
				throw new ArgumentOutOfRangeException(
					nameof(condition.DestinationType), condition.DestinationType,
					null);
		}
		var feeLevel = condition.FeeLevel ?? CopperFeeLevels.Unknown;
		var memo = condition.Memo.IsEmpty()
			? condition.WithdrawInfo.PaymentId
			: condition.Memo;
		var mainCurrency = condition.MainCurrency;
		if (mainCurrency.IsEmpty())
			mainCurrency = message.SecurityId.Native as string;
		return new()
		{
			ExternalOrderId = externalId,
			PortfolioId = portfolio.PortfolioId,
			BaseCurrency = currency,
			MainCurrency = mainCurrency,
			Amount = amount.ToString(CultureInfo.InvariantCulture),
			ToAddress = toAddress,
			ToCryptoAddressId = toCryptoAddressId,
			ToPortfolioId = toPortfolioId,
			Memo = memo?.Trim(),
			FeeLevel = feeLevel,
			IsFeeIncluded = condition.IsFeeIncluded,
			Description = condition.Description.IsEmpty()
				? message.Comment
				: condition.Description,
		};
	}

	private string ResolveDestinationPortfolioId(string destination)
	{
		destination = destination.ThrowIfEmpty(nameof(destination)).Trim();
		using (_sync.EnterScope())
			return _portfolios.TryGetValue(destination, out var portfolio)
				? portfolio.PortfolioId
				: destination;
	}

	private static string CreateExternalOrderId(OrderRegisterMessage message)
	{
		var value = message.UserOrderId.IsEmpty()
			? "stocksharp-" + message.TransactionId.ToString(
				CultureInfo.InvariantCulture)
			: message.UserOrderId.Trim();
		if (value.Length > 255)
			throw new ArgumentOutOfRangeException(nameof(message.UserOrderId),
				value, "Copper external order ID cannot exceed 255 characters.");
		return value;
	}

	private static void PopulateCreatedOrder(CopperOrder order,
		CopperCreateOrderRequest request)
	{
		order.ExternalOrderId = order.ExternalOrderId.IsEmpty()
			? request.ExternalOrderId
			: order.ExternalOrderId;
		order.PortfolioId = order.PortfolioId.IsEmpty()
			? request.PortfolioId
			: order.PortfolioId;
		order.BaseCurrency = order.BaseCurrency.IsEmpty()
			? request.BaseCurrency
			: order.BaseCurrency;
		order.MainCurrency = order.MainCurrency.IsEmpty()
			? request.MainCurrency
			: order.MainCurrency;
		order.Amount = order.Amount.IsEmpty() ? request.Amount : order.Amount;
		if (order.Type == CopperOrderTypes.Unknown)
			order.Type = CopperOrderTypes.Withdraw;
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
			active = [.. _activeOrders];
		}

		if (portfolioTargets.Length > 0)
		{
			var portfolios = await RefreshPortfoliosAsync(cancellationToken);
			var balances = await GetBalanceSnapshotAsync(cancellationToken);
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target.Key, false,
					SelectPortfolios(portfolios, target.Value.PortfolioName), balances,
					cancellationToken);
		}

		foreach (var item in active)
		{
			var order = await RestClient.GetOrderAsync(item.Key,
				cancellationToken);
			TrackOrder(order, item.Value);
			await SendOrderAsync(order, item.Value, false, cancellationToken);
		}

		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);
	}

	private static PortfolioReference[] SelectPortfolios(
		IEnumerable<PortfolioReference> portfolios, string portfolioName)
	{
		var result = portfolios.Where(portfolio => portfolio is not null &&
			(portfolioName.IsEmpty() || portfolio.Name.EqualsIgnoreCase(
				portfolioName))).ToArray();
		if (!portfolioName.IsEmpty() && result.Length == 0)
			throw new InvalidOperationException(
				$"Unknown Copper portfolio '{portfolioName}'.");
		return result;
	}

	private async ValueTask<BalanceSnapshot> GetBalanceSnapshotAsync(
		CancellationToken cancellationToken)
		=> new(await RestClient.GetWalletsAsync(PageSize, MaximumItems,
				cancellationToken),
			await RestClient.TryGetClearLoopBalancesAsync(cancellationToken));

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, IEnumerable<PortfolioReference> portfolios,
		BalanceSnapshot snapshot, CancellationToken cancellationToken)
	{
		foreach (var portfolio in portfolios)
		{
			if (portfolio.ClientAccountId.IsEmpty())
			{
				foreach (var wallet in snapshot.Wallets.Where(wallet =>
					wallet is not null && wallet.PortfolioId.EqualsIgnoreCase(
						portfolio.PortfolioId)))
					await SendWalletBalanceAsync(target, isForced, portfolio,
						wallet, cancellationToken);
			}
			else
			{
				foreach (var balance in snapshot.ClearLoopBalances.Where(balance =>
					balance is not null && balance.PortfolioId.EqualsIgnoreCase(
						portfolio.PortfolioId) && balance.ClientAccountId.EqualsIgnoreCase(
						portfolio.ClientAccountId)))
					await SendClearLoopBalanceAsync(target, isForced, portfolio,
						balance, cancellationToken);
			}
		}
	}

	private ValueTask SendWalletBalanceAsync(long target, bool isForced,
		PortfolioReference portfolio, CopperWallet wallet,
		CancellationToken cancellationToken)
	{
		if (wallet.Currency.IsEmpty())
			return default;
		var current = (wallet.TotalBalance.IsEmpty()
			? wallet.Balance
			: wallet.TotalBalance).ParseCopperAmount();
		var available = wallet.Available.IsEmpty()
			? current
			: wallet.Available.ParseCopperAmount();
		var blocked = Math.Max(Math.Max(0m, current - available), Math.Max(
			wallet.Locked.ParseCopperAmount(), wallet.Reserve.ParseCopperAmount()));
		return SendBalanceAsync(target, isForced, portfolio.Name,
			wallet.Currency, current, blocked, wallet.UpdatedAt, cancellationToken);
	}

	private ValueTask SendClearLoopBalanceAsync(long target, bool isForced,
		PortfolioReference portfolio, CopperClearLoopBalance balance,
		CancellationToken cancellationToken)
	{
		if (balance.Currency.IsEmpty())
			return default;
		var current = balance.Balance.ParseCopperAmount();
		var available = (balance.AvailableToUndelegate.IsEmpty()
			? balance.DelegatedAvailable
			: balance.AvailableToUndelegate).ParseCopperAmount();
		var blocked = Math.Max(Math.Max(0m, current - available),
			balance.Reserve.ParseCopperAmount());
		return SendBalanceAsync(target, isForced, portfolio.Name,
			balance.Currency, current, blocked, null, cancellationToken);
	}

	private async ValueTask SendBalanceAsync(long target, bool isForced,
		string portfolioName, string currency, decimal current, decimal blocked,
		string updatedAt, CancellationToken cancellationToken)
	{
		var fingerprint = new BalanceFingerprint(current, blocked);
		var key = $"{target}:{portfolioName}:{currency}";
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
				SecurityCode = currency,
				BoardCode = BoardCodes.Copper,
			},
			ServerTime = updatedAt.ToCopperTime(CurrentTime.EnsureUtc()),
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
		CopperOrder[] orders;
		if (!subscription.CopperId.IsEmpty())
			orders = [await RestClient.GetOrderAsync(subscription.CopperId,
				cancellationToken)];
		else
		{
			var portfolioId = subscription.PortfolioName.IsEmpty()
				? null
				: GetPortfolio(subscription.PortfolioName).PortfolioId;
			var requested = (int)Math.Min(HistoryLimit,
				(long)subscription.Skip + subscription.Maximum);
			orders = await RestClient.GetOrdersAsync(subscription.From,
				subscription.To, portfolioId,
				subscription.SecurityId.SecurityCode, Math.Max(1, requested),
				cancellationToken);
		}

		var skipped = 0;
		var delivered = 0;
		foreach (var order in orders
			.Where(order => Matches(subscription, order))
			.OrderBy(order => order.CreatedAt.ToCopperTime(DateTime.UnixEpoch)))
		{
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			var localId = GetLocalTransactionId(order, 0);
			TrackOrder(order, localId);
			await SendOrderAsync(order, target, isForced, cancellationToken,
				subscription.PortfolioName);
		}
	}

	private async ValueTask SendOrderAsync(CopperOrder order, long target,
		bool isForced, CancellationToken cancellationToken,
		string portfolioName = null)
	{
		ArgumentNullException.ThrowIfNull(order);
		var key = $"{target}:{order.Id}";
		var fingerprint = new OrderFingerprint(order.Status, order.UpdatedAt,
			order.Extra?.TransactionId);
		using (_sync.EnterScope())
		{
			if (!isForced && _orderFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_orderFingerprints[key] = fingerprint;
		}

		var state = order.Status.ToOrderState();
		var amount = order.Amount.ParseCopperAmount();
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = new()
			{
				SecurityCode = order.BaseCurrency,
				BoardCode = BoardCodes.Copper,
				Native = order.MainCurrency,
			},
			ServerTime = order.UpdatedAt.ToCopperTime(
				order.CreatedAt.ToCopperTime(CurrentTime.EnsureUtc())),
			PortfolioName = GetOrderPortfolioName(order, portfolioName),
			Side = order.Type.ToSide(),
			OrderVolume = amount,
			Balance = state == OrderStates.Active ? amount : 0m,
			OrderPrice = 0m,
			OrderType = OrderTypes.Conditional,
			OrderState = state,
			OrderStringId = order.Id,
			OrderBoardId = order.Extra?.TransactionId,
			UserOrderId = order.ExternalOrderId,
			TransactionId = GetLocalTransactionId(order, 0),
			OriginalTransactionId = target,
			Comment = order.Extra?.Description,
			Commission = GetCommission(order),
			CommissionCurrency = order.BaseCurrency,
		};
		if (state == OrderStates.Failed)
			message.Error = new InvalidOperationException(
				$"Copper order {order.Id} ended with status {order.Status}.");
		await SendOutMessageAsync(message, cancellationToken);
	}

	private void TrackOrder(CopperOrder order, long localTransactionId)
	{
		if (order?.Id.IsEmpty() != false)
			return;
		if (localTransactionId == 0)
			localTransactionId = ParseLocalTransactionId(order.ExternalOrderId);
		using (_sync.EnterScope())
		{
			if (localTransactionId != 0)
			{
				_localTransactionIds[order.Id] = localTransactionId;
				_copperOrderIds[localTransactionId] = order.Id;
			}
			if (!order.Status.IsFinal() && localTransactionId != 0)
				_activeOrders[order.Id] = localTransactionId;
			else
				_activeOrders.Remove(order.Id);
		}
	}

	private long GetLocalTransactionId(CopperOrder order, long fallback)
	{
		if (order is null)
			return fallback;
		using (_sync.EnterScope())
			if (!order.Id.IsEmpty() &&
				_localTransactionIds.TryGetValue(order.Id, out var value))
				return value;
		var parsed = ParseLocalTransactionId(order.ExternalOrderId);
		return parsed == 0 ? fallback : parsed;
	}

	private static long ParseLocalTransactionId(string externalId)
	{
		const string prefix = "stocksharp-";
		if (externalId.IsEmpty() ||
			!externalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return 0;
		return long.TryParse(externalId[prefix.Length..], NumberStyles.None,
			CultureInfo.InvariantCulture, out var value)
			? value
			: 0;
	}

	private bool Matches(OrderSubscription subscription, CopperOrder order)
	{
		if (order is null || order.Id.IsEmpty() || order.BaseCurrency.IsEmpty())
			return false;
		if (!subscription.CopperId.IsEmpty() &&
			!subscription.CopperId.EqualsIgnoreCase(order.Id))
			return false;
		if (!subscription.SecurityId.BoardCode.IsEmpty() &&
			!subscription.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Copper))
			return false;
		if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
			!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
				order.BaseCurrency))
			return false;
		if (!subscription.PortfolioName.IsEmpty())
		{
			var portfolio = GetPortfolio(subscription.PortfolioName);
			if (!portfolio.PortfolioId.EqualsIgnoreCase(order.PortfolioId) ||
				(!portfolio.ClientAccountId.IsEmpty() &&
					!portfolio.ClientAccountId.EqualsIgnoreCase(
						order.Extra?.ClientAccountId)))
				return false;
		}
		if (subscription.Side is Sides side && order.Type.ToSide() != side)
			return false;
		if (subscription.Volume is decimal volume &&
			order.Amount.ParseCopperAmount() != volume)
			return false;
		var state = order.Status.ToOrderState();
		if (subscription.States is { Length: > 0 } states &&
			!states.Contains(state))
			return false;
		var time = order.CreatedAt.ToCopperTime(DateTime.UnixEpoch);
		return (subscription.From is null || time >= subscription.From) &&
			(subscription.To is null || time <= subscription.To);
	}

	private string GetOrderPortfolioName(CopperOrder order,
		string preferredPortfolio)
	{
		if (!preferredPortfolio.IsEmpty())
			return preferredPortfolio;
		var clientAccountId = order.Extra?.ClientAccountId;
		using (_sync.EnterScope())
		{
			if (!clientAccountId.IsEmpty())
			{
				var clearLoop = _portfolios.Values.FirstOrDefault(item =>
					item.PortfolioId.EqualsIgnoreCase(order.PortfolioId) &&
					item.ClientAccountId.EqualsIgnoreCase(
						clientAccountId));
				if (clearLoop is not null)
					return clearLoop.Name;
			}
			return _portfolios.Values.FirstOrDefault(item =>
				item.ClientAccountId.IsEmpty() && item.PortfolioId.EqualsIgnoreCase(
					order.PortfolioId))?.Name ??
				CopperExtensions.GetPortfolioName(order.PortfolioId);
		}
	}

	private static decimal? GetCommission(CopperOrder order)
	{
		var value = order.Extra?.WithdrawFee;
		if (value.IsEmpty())
			return null;
		return value.ParseCopperAmount();
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
			RemoveFingerprintPrefix(_orderFingerprints, target);
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
