namespace StockSharp.Deribit;

public partial class DeribitMessageAdapter
{
	private string PortfolioName => nameof(Deribit) + "_" + Key.ToId();

	private static readonly string[] _coins = ["BTC", "ETH"];
	private readonly SynchronizedDictionary<long, long> _requesIdMap = [];

	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var currency = regMsg.SecurityId.ToCurrency();
		var condition = (DeribitOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
					break;

				return _pusherClient.Withdraw(regMsg.TransactionId, currency, regMsg.Volume, condition.WithdrawInfo, regMsg.Comment, cancellationToken);
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		return _pusherClient.RegisterOrder(regMsg.TransactionId, regMsg.Side, currency, regMsg.Volume,
			regMsg.OrderType.ToNative(condition?.StopPrice), regMsg.Price, condition?.StopPrice, regMsg.VisibleVolume,
			regMsg.TimeInForce.ToNative(), condition?.Trigger.ToNative(), condition?.Advanced.ToNative(), regMsg.PostOnly, regMsg.PositionEffect == null ? null : (regMsg.PositionEffect.Value == OrderPositionEffects.CloseOnly),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderStringId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.TransactionId));

		var condition = (DeribitOrderCondition)replaceMsg.Condition;

		return _pusherClient.EditOrder(replaceMsg.TransactionId, replaceMsg.OldOrderStringId, replaceMsg.Volume,
			replaceMsg.Price, condition?.StopPrice, condition?.Advanced.ToNative(), replaceMsg.PostOnly, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		//if (cancelMsg.OrderStringId == null)
		//	throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		return _pusherClient.CancelOrderByLabel(cancelMsg.TransactionId, cancelMsg.OriginalTransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		return _pusherClient.CancelGroupOrders(cancelMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsSubscribe)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = PortfolioName,
				BoardCode = BoardCodes.Deribit,
				OriginalTransactionId = lookupMsg.TransactionId
			}, cancellationToken);

			foreach (var coin in _coins)
			{
				await _pusherClient.RequestAccount(CreateMap(lookupMsg.TransactionId), coin, cancellationToken);
				await _pusherClient.RequestPositions(CreateMap(lookupMsg.TransactionId), coin, cancellationToken);
				await _pusherClient.SubscribeAccount(CreateMap(lookupMsg.TransactionId), coin, cancellationToken);
			}

			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
		else
		{
			foreach (var coin in _coins)
				await _pusherClient.UnSubscribeAccount(lookupMsg.OriginalTransactionId, CreateMap(lookupMsg.TransactionId), coin, cancellationToken);
		}
	}

	private long CreateMap(long transactionId)
	{
		var transId = TransactionIdGenerator.GetNextId();
		_requesIdMap.Add(transId, transactionId);
		return transId;
	}

	private long GetOrigin(long transactionId)
		=> _requesIdMap.TryGetValue(transactionId, out var map) ? map : transactionId;

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (statusMsg.IsSubscribe)
		{
			if (!statusMsg.OrderStringId.IsEmpty())
			{
				await _pusherClient.RequestOrderState(statusMsg.TransactionId, statusMsg.OrderStringId, cancellationToken);
			}
			else
			{
				foreach (var coin in _coins)
				{
					await _pusherClient.RequestOpenOrders(CreateMap(statusMsg.TransactionId), coin, cancellationToken);
				}

				await _pusherClient.SubscribeOrders(TransactionIdGenerator.GetNextId(), cancellationToken);
			}

			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrders(statusMsg.OriginalTransactionId, statusMsg.TransactionId, cancellationToken);
	}

	private async ValueTask SessionOnPositionChanged(long requestId, IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		requestId = GetOrigin(requestId);

		foreach (var position in positions)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = position.Instrument.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId = requestId,
			}
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.FloatingPnL?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.LiquidationPrice, position.EstimatedLiquidationPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.VariationMargin, position.MaintenanceMargin?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.SettlementPrice, position.SettlementPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, position.OpenOrdersMargin?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentValue, position.Size?.ToDecimal(), true), cancellationToken);
		}
	}

	private ValueTask SessionOnAccountChanged(long requestId, Account account, CancellationToken cancellationToken)
	{
		requestId = GetOrigin(requestId);

		return SendOutMessageAsync(new PositionChangeMessage
		{
			SecurityId = account.Currency.ToStockSharp(),
			PortfolioName = PortfolioName,
			ServerTime = CurrentTime,
			OriginalTransactionId = requestId,
		}
		.TryAdd(PositionChangeTypes.RealizedPnL, account.SessionRealPnL?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, account.SessionUnrealPnL?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.VariationMargin, account.MaintenanceMargin?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentValue, account.Balance?.ToDecimal(), true), cancellationToken);
	}

	private async ValueTask SessionOnNewUserTrades(IEnumerable<UserTrade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			if (!long.TryParse(trade.Label, out var origTransId))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = trade.TimeStamp,
				SecurityId = trade.Instrument.IsEmpty() ? default : trade.Instrument.ToStockSharp(),
				TradeStringId = trade.Id,
				TradePrice = (decimal)trade.Price,
				TradeVolume = (decimal)trade.Quantity,
				Commission = (decimal?)trade.Fee,
				CommissionCurrency = trade.FeeCurrency,
				OrderStringId = trade.OrderId,
				OriginalTransactionId = origTransId,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderChanged(long requestId, Order order, CancellationToken cancellationToken)
	{
		var unkTrans = false;

		if (!long.TryParse(order.Label, out var transactionId))
		{
			unkTrans = true;
			transactionId = (long)order.Created.ToUnix(false);
		}

		requestId = GetOrigin(requestId);

		var isLookup = requestId != 0;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = isLookup || unkTrans ? order.Created : (order.Modified ?? CurrentTime),
			SecurityId = order.Instrument.ToStockSharp(),
			TransactionId = isLookup || unkTrans ? transactionId : 0,
			OriginalTransactionId = isLookup ? requestId : transactionId,
			OrderStringId = order.Id,
			OrderVolume = (decimal)order.Quantity,
			VisibleVolume = (decimal?)order.MaxShow,
			Balance = order.GetBalance(),
			Side = order.Direction.ToSide(),
			OrderPrice = (decimal?)(order.Price ?? order.ImpliedVolatility ?? order.Usd) ?? 0,
			PortfolioName = PortfolioName,
			OrderState = order.State.ToOrderState(),
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			Commission = (decimal?)order.Commission,
			OrderType = order.Type.ToOrderType(),
			PositionEffect = order.ReduceOnly == null ? null : (order.ReduceOnly.Value ? OrderPositionEffects.CloseOnly : OrderPositionEffects.Default),
			PostOnly = order.PostOnly,
			Condition = new DeribitOrderCondition
			{
				Advanced = order.Advanced.ToAdvancedType(),
				StopPrice = order.StopPrice,
				Trigger = order.Trigger.ToTrigger(),
			},
		}, cancellationToken);
	}

	private ValueTask SessionOnWithdrawUpdated(long requestId, DeribitWithdraw withdraw, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderId = withdraw.Id,
			ServerTime = CurrentTime,
			OriginalTransactionId = requestId,
			OrderState = withdraw.State.ToWithdrawState(),
			Balance = withdraw.State == "completed" ? 0 : (decimal)withdraw.Amount,
			HasOrderInfo = true,
			Commission = (decimal?)withdraw.Fee,
		}, cancellationToken);
	}
}