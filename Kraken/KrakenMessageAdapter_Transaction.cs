namespace StockSharp.Kraken;

public partial class KrakenMessageAdapter
{
	private string PortfolioName => nameof(Kraken) + "_" + Key.ToId();

	private readonly Dictionary<string, RefTriple<decimal, long, HashSet<string>>> _orderInfo = new(StringComparer.InvariantCultureIgnoreCase);

	private static HashSet<string> CreateTradesSet() => new(StringComparer.InvariantCultureIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = (KrakenOrderCondition)regMsg.Condition;

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

				var withdrawId = await _spotHttpClient.Withdraw(regMsg.SecurityId.SecurityCode, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderStringId = withdrawId,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
				}, cancellationToken);

				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = isMarket ? (decimal?)null : regMsg.Price;

		var orderId = await _spotHttpClient.AddOrder(
			regMsg.SecurityId.ToSymbol(),
			regMsg.Side.ToNative(),
			regMsg.OrderType.ToNative(condition),
			regMsg.Volume,
			price,
			condition?.StopPrice,
			condition?.Leverage,
			condition?.OrderFlags,
			condition?.StartTime,
			regMsg.TillDate.EnsureToday()?.ToUnix(),
			regMsg.TransactionId, default,
			cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderStringId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = isMarket ? OrderStates.Done : OrderStates.Active,
			Balance = isMarket ? 0 : null,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		return _spotHttpClient.CancelOrder(cancelMsg.OrderStringId, cancellationToken).AsValueTask();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
			return;

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Kraken,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		var opened = await _spotHttpClient.GetOpenOrders(true, default, cancellationToken);

		foreach (var pair in opened.Open)
		{
			var refId = pair.Key;
			var order = pair.Value;

			var transId = order.UserRef;

			if (transId == 0 || transId == null)
				transId = TransactionIdGenerator.GetNextId();

			var set = CreateTradesSet();

			_orderInfo.Add(refId, RefTuple.Create(order.GetBalance(), transId.Value, set));

			await ProcessOrder(refId, order, transId.Value, statusMsg.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private ValueTask ProcessOrder(string refId, Native.Spot.Model.OrderInfo order, long transId, long origTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = (transId == 0 ? (order.CloseTime == default ? order.OpenTime : order.CloseTime) : order.OpenTime).FromUnix(),
			SecurityId = order.Description.Pair.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderStringId = refId,
			OrderVolume = order.Volume,
			OrderType = order.GetOrderType(out var condition),
			Condition = condition,
			ExpiryDate = order.ExpireTime.FromUnix(),
			Balance = order.GetBalance(),
			Side = order.Description.Type.ToSide(),
			OrderPrice = order.Description.Price ?? 0,
			PortfolioName = PortfolioName,
			Commission = order.Fee == 0 ? null : order.Fee,
			OrderState = order.Status.ToOrderState(),
		}, cancellationToken);
	}
}
