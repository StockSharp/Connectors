namespace StockSharp.ApexOmni;

public partial class ApexOmniMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady(true);
		ValidatePortfolio(regMsg.PortfolioName);
		var instrument = GetInstrument(regMsg.SecurityId);
		if (instrument.Group == ApexOmniInstrumentGroups.Stock)
			throw new NotSupportedException(
				"ApeX Omni tokenized stocks require a separate RWA account, " +
				"API key, and signing context. This adapter uses the primary " +
				"contract account for trading.");
		if (!instrument.IsTradingEnabled)
			throw new InvalidOperationException(
				$"ApeX Omni instrument '{instrument.Symbol}' is not tradable.");

		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"ApeX Omni order volume must be positive.");
		var volumeStep = instrument.StepSize.ParseRequiredDecimal("size step");
		var minimumVolume = instrument.MinOrderSize.ToDecimal() ?? volumeStep;
		if (volume < minimumVolume || volume % volumeStep != 0)
			throw new InvalidOperationException(
				$"ApeX Omni order volume must be at least {minimumVolume} and " +
				$"a multiple of {volumeStep}.");
		if (instrument.MaxOrderSize.ToDecimal() is decimal maximumVolume &&
			volume > maximumVolume)
			throw new InvalidOperationException(
				$"ApeX Omni order volume exceeds the maximum {maximumVolume}.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as ApexOmniOrderCondition ?? new();
		var isConditional = orderType == OrderTypes.Conditional;
		if (isConditional && condition.TriggerType is not
			(ApexOmniTriggerTypes.StopLoss or ApexOmniTriggerTypes.TakeProfit))
			throw new InvalidOperationException(
				"An ApeX Omni conditional order requires a trigger type.");
		if (isConditional && condition.ActivationPrice is not > 0)
			throw new InvalidOperationException(
				"An ApeX Omni conditional order requires a positive trigger price.");
		var isMarket = orderType == OrderTypes.Market ||
			isConditional && condition.IsMarket;
		if (!isMarket && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"A positive ApeX Omni limit price is required.");
		if (regMsg.PostOnly == true && isMarket)
			throw new InvalidOperationException(
				"An ApeX Omni market order cannot be post-only.");

		var side = regMsg.Side.ToApexOmni();
		var signingPrice = regMsg.Price;
		if (isMarket)
		{
			var worstPrice = await RestClient.GetWorstPriceAsync(new()
			{
				Symbol = instrument.Symbol,
				Size = volume.ToWire(),
				Side = side,
			}, cancellationToken);
			signingPrice = worstPrice.WorstPrice.ParseRequiredDecimal(
				"worst execution price");
		}
		var priceStep = instrument.TickSize.ParseRequiredDecimal("tick size");
		if (signingPrice <= 0 || signingPrice % priceStep != 0)
			throw new InvalidOperationException(
				$"ApeX Omni order price must be a positive multiple of {priceStep}.");

		var clientId = CreateClientId(regMsg.TransactionId, regMsg.UserOrderId);
		var contractAccount = _account.ContractAccount;
		var takerFeeRate = contractAccount.TakerFeeRate.ParseRequiredDecimal(
			"taker fee rate");
		var makerFeeRate = contractAccount.MakerFeeRate.ParseRequiredDecimal(
			"maker fee rate");
		var asset = GetSettlementAsset(instrument);
		var signature = _signer.Sign(new()
		{
			AccountId = _account.Id,
			PairId = instrument.L2PairId,
			Decimals = asset.Decimals,
			ClientId = clientId,
			Size = volume,
			Price = signingPrice,
			IsBuy = regMsg.Side == Sides.Buy,
			TakerFeeRate = takerFeeRate,
			MakerFeeRate = makerFeeRate,
		});
		var nativeType = ToNativeOrderType(orderType, condition);
		var timeInForce = regMsg.TimeInForce.ToApexOmni(
			regMsg.PostOnly == true, isMarket);
		var expiration = GetOrderExpiration(regMsg.TillDate);
		var request = new ApexOmniCreateOrderRequest
		{
			Symbol = instrument.Symbol,
			Side = side,
			Type = nativeType,
			TimeInForce = timeInForce,
			Size = volume.ToWire(),
			Price = signingPrice.ToWire(),
			LimitFee = ApexOmniZkSigner.CalculateLimitFee(volume,
				signingPrice, takerFeeRate),
			Expiration = expiration.ToUnixMilliseconds(),
			TriggerPrice = isConditional
				? condition.ActivationPrice.Value.ToWire()
				: null,
			TriggerPriceType = isConditional
				? condition.TriggerPrice.ToNative()
				: null,
			ClientId = clientId,
			Signature = signature,
			IsReduceOnly = condition.IsReduceOnly ||
				regMsg.PositionEffect == OrderPositionEffects.CloseOnly,
		};
		var order = await RestClient.CreateOrderAsync(request,
			cancellationToken);
		if (order.EffectiveId.IsEmpty())
			throw new InvalidDataException(
				"ApeX Omni accepted an order without returning an order ID.");

		await SendOrderAsync(order, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"ApeX Omni does not expose a single-order amendment endpoint. " +
			"Cancel the active order and submit a newly signed order.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady(false);
		ValidatePortfolio(cancelMsg.PortfolioName);
		var id = cancelMsg.OrderStringId;
		if (id.IsEmpty() && cancelMsg.OrderId is long numericId)
			id = numericId.ToString(CultureInfo.InvariantCulture);
		var isClientId = false;
		if (id.IsEmpty() && !cancelMsg.UserOrderId.IsEmpty())
		{
			id = cancelMsg.UserOrderId.Trim();
			isClientId = true;
		}
		if (id.IsEmpty() && cancelMsg.OriginalTransactionId > 0)
		{
			id = CreateClientId(cancelMsg.OriginalTransactionId, null);
			isClientId = true;
		}
		if (id.IsEmpty())
			throw new InvalidOperationException(
				"ApeX Omni cancellation requires an exchange or client order ID.");
		await RestClient.CancelOrderAsync(id, isClientId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady(false);
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"ApeX Omni bulk cancellation does not close positions.");
		if (cancelMsg.Side is not null || cancelMsg.IsStop is not null)
			throw new NotSupportedException(
				"ApeX Omni bulk cancellation supports only an optional symbol filter.");
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetInstrument(cancelMsg.SecurityId).Symbol;
		await RestClient.CancelAllOrdersAsync(symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady(false);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId ||
				lookupMsg.OriginalTransactionId == 0)
				_portfolioSubscriptionId = 0;
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.ApexOmni,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady(false);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId ||
				statusMsg.OriginalTransactionId == 0)
				_orderStatusSubscriptionId = 0;
			return;
		}
		ValidatePortfolio(statusMsg.PortfolioName);
		await SendOrderSnapshotAsync(statusMsg.TransactionId, statusMsg,
			statusMsg.Count, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private DateTime GetOrderExpiration(DateTime? tillDate)
	{
		var now = ServerTime;
		var maximum = now.AddDays(28);
		var expiration = tillDate?.ToUniversalTime() ?? maximum;
		if (expiration <= now)
			throw new InvalidOperationException(
				"ApeX Omni order expiration must be in the future.");
		if (expiration > maximum)
			throw new InvalidOperationException(
				"ApeX Omni order expiration cannot be more than 28 days ahead.");
		return expiration;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var account = await RestClient.GetAccountAsync(cancellationToken);
		ValidateAccount(account);
		_account = account;
		var balance = await RestClient.GetAccountBalanceAsync(cancellationToken);
		await SendAccountBalanceAsync(balance, transactionId, ServerTime,
			cancellationToken);
		foreach (var wallet in account.ContractWallets ?? [])
			await SendWalletAsync(wallet, transactionId, ServerTime,
				cancellationToken);
		foreach (var position in account.Positions ?? [])
			await SendPositionAsync(position, transactionId, ServerTime,
				cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long transactionId,
		OrderStatusMessage statusMsg, long? requestedCount,
		CancellationToken cancellationToken)
	{
		var instrument = statusMsg?.SecurityId.SecurityCode.IsEmpty() == false
			? GetInstrument(statusMsg.SecurityId)
			: null;
		var symbol = instrument?.Symbol;
		var maximum = GetHistoryLimit(requestedCount, 1000, 10000);
		var openOrders = await RestClient.GetOpenOrdersAsync(symbol,
			cancellationToken);
		var history = await LoadOrderHistoryAsync(symbol, statusMsg?.From,
			statusMsg?.To, maximum, cancellationToken);
		foreach (var order in openOrders.Concat(history)
			.Where(order => IsMatchingOrder(order, statusMsg))
			.GroupBy(static order => order.EffectiveId.IsEmpty()
				? order.EffectiveClientId
				: order.EffectiveId, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime)
			.TakeLast(maximum))
			await SendOrderAsync(order, transactionId, cancellationToken);

		var fills = await LoadFillsAsync(symbol, statusMsg?.From,
			statusMsg?.To, maximum, cancellationToken);
		foreach (var fill in fills
			.Where(fill => IsMatchingFill(fill, statusMsg))
			.GroupBy(static fill => fill.EffectiveId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(GetFillTime)
			.TakeLast(maximum))
			await SendFillAsync(fill, transactionId, cancellationToken);
	}

	private async ValueTask<ApexOmniOrder[]> LoadOrderHistoryAsync(string symbol,
		DateTime? from, DateTime? to, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<ApexOmniOrder>();
		for (var page = 0; result.Count < maximum; page++)
		{
			var limit = (maximum - result.Count).Min(100).Max(1);
			var response = await RestClient.GetOrderHistoryAsync(new()
			{
				Symbol = symbol,
				Limit = limit,
				Page = page,
				BeginTime = from?.ToUniversalTime().ToUnixMilliseconds(),
				EndTime = to?.ToUniversalTime().AddMilliseconds(1)
					.ToUnixMilliseconds(),
			}, cancellationToken);
			var items = response?.Orders ?? [];
			result.AddRange(items.Where(static item => item is not null));
			if (items.Length < limit || response.TotalSize > 0 &&
				result.Count >= response.TotalSize)
				break;
		}
		return [.. result.Take(maximum)];
	}

	private async ValueTask<ApexOmniFill[]> LoadFillsAsync(string symbol,
		DateTime? from, DateTime? to, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<ApexOmniFill>();
		for (var page = 0; result.Count < maximum; page++)
		{
			var limit = (maximum - result.Count).Min(100).Max(1);
			var response = await RestClient.GetFillsAsync(new()
			{
				Symbol = symbol,
				Limit = limit,
				Page = page,
				BeginTime = from?.ToUniversalTime().ToUnixMilliseconds(),
				EndTime = to?.ToUniversalTime().AddMilliseconds(1)
					.ToUnixMilliseconds(),
			}, cancellationToken);
			var items = response?.Fills ?? [];
			result.AddRange(items.Where(static item => item is not null));
			if (items.Length < limit || response.TotalSize > 0 &&
				result.Count >= response.TotalSize)
				break;
		}
		return [.. result.Take(maximum)];
	}

	private async ValueTask OnPrivateFeedAsync(ApexOmniPrivateFeed feed,
		CancellationToken cancellationToken)
	{
		if (feed is null)
			return;
		var contents = feed.GetContents();
		var serverTime = feed.Timestamp > 0
			? feed.Timestamp.ToApexOmniTime()
			: ServerTime;
		var contractAccount = (contents.ContractAccounts ?? contents.Accounts)?
			.FirstOrDefault();
		if (contractAccount is not null)
			_account.ContractAccount = contractAccount;
		if (_portfolioSubscriptionId != 0)
		{
			foreach (var wallet in (contents.ContractWallets ?? [])
				.Concat(contents.SpotWallets ?? [])
				.Concat(contents.Wallets ?? []))
				await SendWalletAsync(wallet, _portfolioSubscriptionId,
					serverTime, cancellationToken);
			foreach (var position in contents.Positions ?? [])
				await SendPositionAsync(position, _portfolioSubscriptionId,
					serverTime, cancellationToken);
		}
		foreach (var order in contents.Orders ?? [])
			foreach (var target in GetPrivateTargets(order?.EffectiveClientId))
				await SendOrderAsync(order, target, cancellationToken);
		foreach (var fill in contents.Fills ?? [])
			foreach (var target in GetPrivateTargets(fill?.EffectiveClientId))
				await SendFillAsync(fill, target, cancellationToken);
	}

	private long[] GetPrivateTargets(string clientId)
	{
		var targets = new HashSet<long>();
		var transactionId = clientId.ToTransactionId();
		if (transactionId != 0)
			targets.Add(transactionId);
		if (_orderStatusSubscriptionId != 0)
			targets.Add(_orderStatusSubscriptionId);
		return [.. targets];
	}

	private ValueTask SendAccountBalanceAsync(ApexOmniAccountBalance balance,
		long transactionId, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		if (balance is null || transactionId == 0)
			return default;
		var message = this.CreatePortfolioChangeMessage(PortfolioName);
		message.ServerTime = serverTime;
		message.OriginalTransactionId = transactionId;
		message
			.TryAdd(PositionChangeTypes.CurrentValue,
				balance.TotalEquityValue.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				balance.InitialMargin.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.VariationMargin,
				balance.MaintenanceMargin.ToDecimal(), true);
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask SendWalletAsync(ApexOmniWallet wallet, long transactionId,
		DateTime serverTime, CancellationToken cancellationToken)
	{
		if (wallet?.Currency.IsEmpty() != false || transactionId == 0)
			return default;
		var blocked = (wallet.PendingWithdrawAmount.ToDecimal() ?? 0m) +
			(wallet.PendingTransferOutAmount.ToDecimal() ?? 0m);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = new()
			{
				SecurityCode = wallet.Currency.ToUpperInvariant(),
				BoardCode = BoardCodes.ApexOmni,
			},
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			wallet.Balance.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(ApexOmniPosition position,
		long transactionId, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false || transactionId == 0)
			return default;
		ApexOmniContract instrument;
		using (_sync.EnterScope())
			_instruments.TryGetValue(position.Symbol, out instrument);
		if (instrument is null)
		{
			this.AddWarningLog("Unknown ApeX Omni position symbol '{0}'.",
				position.Symbol);
			return default;
		}
		var size = position.Size.ParseRequiredDecimal("position size").Abs();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = instrument.ToStockSharp(),
			ServerTime = position.UpdatedAt > 0
				? position.UpdatedAt.ToApexOmniTime()
				: serverTime,
			OriginalTransactionId = transactionId,
			Side = size == 0 ? null : position.Side.ToStockSharp(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, size, true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.EntryPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			position.RealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Commission,
			position.FundingFee.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendOrderAsync(ApexOmniOrder order, long transactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Symbol.IsEmpty() != false ||
			(order.EffectiveId.IsEmpty() && order.EffectiveClientId.IsEmpty()) ||
			transactionId == 0)
			return default;
		ApexOmniContract instrument;
		using (_sync.EnterScope())
			_instruments.TryGetValue(order.Symbol, out instrument);
		if (instrument is null)
			return default;
		var volume = order.Size.ToDecimal();
		var remaining = order.RemainingSize.ToDecimal();
		if (remaining is null && volume is decimal total)
			remaining = (total - (order.FilledSize.ToDecimal() ?? 0m)).Max(0m);
		var condition = ToOrderCondition(order);
		var state = order.Status.ToStockSharp();
		var serverTime = GetOrderTime(order);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = instrument.ToStockSharp(),
			ServerTime = serverTime != DateTime.MinValue
				? serverTime
				: ServerTime,
			PortfolioName = PortfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = remaining,
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.LatestFillPrice.ToDecimal(),
			OrderType = ToStockSharpOrderType(order.Type),
			OrderState = state,
			OrderStringId = order.EffectiveId,
			UserOrderId = order.EffectiveClientId,
			TransactionId = order.EffectiveClientId.ToTransactionId(),
			OriginalTransactionId = transactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			ExpiryDate = order.ExpiresAt > 0
				? order.ExpiresAt.ToApexOmniTime()
				: null,
			PostOnly = order.IsPostOnly,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Commission = order.FilledFee.ToDecimal() ?? order.Fee.ToDecimal(),
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(ApexOmniFill fill, long transactionId,
		CancellationToken cancellationToken)
	{
		if (fill?.Symbol.IsEmpty() != false || fill.EffectiveId.IsEmpty() ||
			!TryAcceptFill(transactionId, fill.EffectiveId))
			return default;
		ApexOmniContract instrument;
		using (_sync.EnterScope())
			_instruments.TryGetValue(fill.Symbol, out instrument);
		if (instrument is null)
			return default;
		var serverTime = GetFillTime(fill);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = instrument.ToStockSharp(),
			ServerTime = serverTime != DateTime.MinValue
				? serverTime
				: ServerTime,
			PortfolioName = PortfolioName,
			Side = fill.Side.ToStockSharp(),
			OrderStringId = fill.OrderId,
			TradeStringId = fill.EffectiveId,
			TradePrice = fill.Price.ToDecimal(),
			TradeVolume = fill.Size.ToDecimal(),
			Commission = fill.Fee.ToDecimal(),
			TransactionId = fill.EffectiveClientId.ToTransactionId(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private static ApexOmniNativeOrderTypes ToNativeOrderType(
		OrderTypes orderType, ApexOmniOrderCondition condition)
		=> orderType switch
		{
			OrderTypes.Limit => ApexOmniNativeOrderTypes.Limit,
			OrderTypes.Market => ApexOmniNativeOrderTypes.Market,
			OrderTypes.Conditional when condition.TriggerType ==
				ApexOmniTriggerTypes.StopLoss => condition.IsMarket
					? ApexOmniNativeOrderTypes.StopMarket
					: ApexOmniNativeOrderTypes.StopLimit,
			OrderTypes.Conditional when condition.TriggerType ==
				ApexOmniTriggerTypes.TakeProfit => condition.IsMarket
					? ApexOmniNativeOrderTypes.TakeProfitMarket
					: ApexOmniNativeOrderTypes.TakeProfitLimit,
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
				"Unsupported ApeX Omni order type."),
		};

	private static OrderTypes ToStockSharpOrderType(
		ApexOmniNativeOrderTypes orderType)
		=> orderType switch
		{
			ApexOmniNativeOrderTypes.Limit => OrderTypes.Limit,
			ApexOmniNativeOrderTypes.Market => OrderTypes.Market,
			ApexOmniNativeOrderTypes.StopLimit or
			ApexOmniNativeOrderTypes.StopMarket or
			ApexOmniNativeOrderTypes.TakeProfitLimit or
			ApexOmniNativeOrderTypes.TakeProfitMarket => OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
				"Unknown ApeX Omni order type."),
		};

	private static ApexOmniOrderCondition ToOrderCondition(ApexOmniOrder order)
		=> new()
		{
			TriggerType = order.Type is ApexOmniNativeOrderTypes.TakeProfitLimit or
				ApexOmniNativeOrderTypes.TakeProfitMarket
					? ApexOmniTriggerTypes.TakeProfit
					: order.Type is ApexOmniNativeOrderTypes.StopLimit or
						ApexOmniNativeOrderTypes.StopMarket
							? ApexOmniTriggerTypes.StopLoss
							: ApexOmniTriggerTypes.None,
			TriggerPrice = order.TriggerPriceType.ToStockSharp(),
			ActivationPrice = order.TriggerPrice.ToDecimal(),
			IsMarket = order.Type is ApexOmniNativeOrderTypes.StopMarket or
				ApexOmniNativeOrderTypes.TakeProfitMarket,
			IsReduceOnly = order.IsReduceOnly,
		};

	private static bool IsMatchingOrder(ApexOmniOrder order,
		OrderStatusMessage message)
	{
		if (order is null || order.Symbol.IsEmpty() ||
			(order.EffectiveId.IsEmpty() && order.EffectiveClientId.IsEmpty()))
			return false;
		if (message is null)
			return true;
		if (message.OrderId is long numericId &&
			!order.EffectiveId.EqualsIgnoreCase(numericId.ToString(
				CultureInfo.InvariantCulture)))
			return false;
		if (!message.OrderStringId.IsEmpty() &&
			!order.EffectiveId.EqualsIgnoreCase(message.OrderStringId))
			return false;
		if (!message.UserOrderId.IsEmpty() &&
			!order.EffectiveClientId.EqualsIgnoreCase(message.UserOrderId))
			return false;
		if (message.Side is Sides side && order.Side.ToStockSharp() != side)
			return false;
		return IsInRange(GetOrderTime(order), message.From, message.To);
	}

	private static bool IsMatchingFill(ApexOmniFill fill,
		OrderStatusMessage message)
	{
		if (fill is null || fill.Symbol.IsEmpty() || fill.EffectiveId.IsEmpty())
			return false;
		if (message is null)
			return true;
		if (message.OrderId is long numericId &&
			!fill.OrderId.EqualsIgnoreCase(numericId.ToString(
				CultureInfo.InvariantCulture)))
			return false;
		if (!message.OrderStringId.IsEmpty() &&
			!fill.OrderId.EqualsIgnoreCase(message.OrderStringId))
			return false;
		if (!message.UserOrderId.IsEmpty() &&
			!fill.EffectiveClientId.EqualsIgnoreCase(message.UserOrderId))
			return false;
		if (message.Side is Sides side && fill.Side.ToStockSharp() != side)
			return false;
		return IsInRange(GetFillTime(fill), message.From, message.To);
	}

	private static bool IsInRange(DateTime time, DateTime? from, DateTime? to)
		=> (from is null || time >= from.Value.ToUniversalTime()) &&
			(to is null || time <= to.Value.ToUniversalTime());

	private static DateTime GetOrderTime(ApexOmniOrder order)
	{
		var value = order?.UpdatedAt > 0
			? order.UpdatedAt
			: order?.CreatedAt ?? 0;
		return value > 0 ? value.ToApexOmniTime() : DateTime.MinValue;
	}

	private static DateTime GetFillTime(ApexOmniFill fill)
	{
		var value = fill?.UpdatedAt > 0
			? fill.UpdatedAt
			: fill?.CreatedAt ?? 0;
		return value > 0 ? value.ToApexOmniTime() : DateTime.MinValue;
	}

	private static string CreateClientId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty())
		{
			userOrderId = userOrderId.Trim();
			if (userOrderId.Length <= 64)
				return userOrderId;
		}
		if (transactionId <= 0)
			throw new ArgumentOutOfRangeException(nameof(transactionId),
				transactionId, "A positive transaction ID is required.");
		return transactionId.ToString(CultureInfo.InvariantCulture);
	}
}
