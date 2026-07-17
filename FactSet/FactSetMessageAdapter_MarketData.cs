namespace StockSharp.FactSet;

public partial class FactSetMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var code = lookupMsg.SecurityId.SecurityCode;
		if (!code.IsEmpty())
		{
			var securityTypes = lookupMsg.GetSecurityTypes();
			var left = lookupMsg.Count ?? long.MaxValue;
			foreach (var reference in await SafeClient().GetReferences(code, cancellationToken))
			{
				CacheReference(reference);
				var security = reference.ToSecurityMessage(lookupMsg.TransactionId);
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var reference = await ResolveReference(mdMsg.SecurityId, cancellationToken);
		var isHistory = mdMsg.From != null || mdMsg.To != null || mdMsg.Count != null;
		DateTime? to = isHistory
			? (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime().Date
			: null;
		DateTime? from = isHistory
			? mdMsg.From?.ToUniversalTime().Date ?? GetDefaultFrom(to.Value, mdMsg.Count)
			: null;
		if (from > to)
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from, "The start date is after the end date.");

		var securityId = mdMsg.SecurityId.ToFactSetSecurityId(reference);
		var messages = new List<Level1ChangeMessage>();
		if (reference.IsFixedIncome())
		{
			foreach (var price in await SafeClient().GetFixedIncomePrices(
				reference.GetRequestCode(), from, to, cancellationToken))
			{
				var time = price.GetTime();
				if (time == null)
					continue;
				messages.Add(new Level1ChangeMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					ServerTime = time.Value,
				}
				.TryAdd(Level1Fields.BestBidPrice, price.Bid)
				.TryAdd(Level1Fields.SpreadMiddle, price.Mid)
				.TryAdd(Level1Fields.BestAskPrice, price.Ask));
			}
		}
		else
		{
			foreach (var price in await SafeClient().GetPrices(reference.GetRequestCode(),
				from, to, Currency, PriceAdjustment, cancellationToken))
			{
				var time = price.GetTime();
				if (time == null)
					continue;
				messages.Add(new Level1ChangeMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					ServerTime = time.Value,
				}
				.TryAdd(Level1Fields.OpenPrice, price.Open)
				.TryAdd(Level1Fields.HighPrice, price.High)
				.TryAdd(Level1Fields.LowPrice, price.Low)
				.TryAdd(Level1Fields.ClosePrice, price.Close)
				.TryAdd(Level1Fields.Volume, price.Volume));
			}
		}

		var selected = messages.OrderBy(message => message.ServerTime).AsEnumerable();
		if (mdMsg.Count is long count)
			selected = selected.TakeLast(checked((int)Math.Min(count.Max(0), int.MaxValue)));
		if (!isHistory)
			selected = selected.TakeLast(1);
		var result = selected.ToArray();
		if (result.Length == 0)
			throw new InvalidOperationException(
				$"FactSet returned no price values for '{reference.GetRequestCode()}'.");

		foreach (var message in result)
			await SendOutMessageAsync(message, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (timeFrame != TimeSpan.FromDays(1))
			throw new NotSupportedException(
				"FactSet Prices candles are available only at the native daily frequency.");

		var reference = await ResolveReference(mdMsg.SecurityId, cancellationToken);
		if (reference.IsFixedIncome())
			throw new NotSupportedException(
				"FactSet fixed-income prices provide bid, mid, and ask observations, not OHLC candles.");

		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime().Date;
		var from = mdMsg.From?.ToUniversalTime().Date ?? GetDefaultFrom(to, mdMsg.Count);
		if (from > to)
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from, "The start date is after the end date.");

		IEnumerable<FactSetPrice> prices = await SafeClient().GetPrices(reference.GetRequestCode(),
			from, to, Currency, PriceAdjustment, cancellationToken);
		prices = prices.Where(price => price.HasOhlc).OrderBy(price => price.GetTime());
		if (mdMsg.Count is long count)
		{
			var take = checked((int)Math.Min(count.Max(0), int.MaxValue));
			prices = mdMsg.From == null ? prices.TakeLast(take) : prices.Take(take);
		}

		var securityId = mdMsg.SecurityId.ToFactSetSecurityId(reference);
		foreach (var price in prices)
		{
			var time = price.GetTime();
			if (time == null)
				continue;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				DataType = mdMsg.DataType2,
				TypedArg = timeFrame,
				OpenTime = time.Value,
				OpenPrice = price.Open.Value,
				HighPrice = price.High.Value,
				LowPrice = price.Low.Value,
				ClosePrice = price.Close.Value,
				TotalVolume = price.Volume ?? 0m,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private static DateTime GetDefaultFrom(DateTime to, long? count)
	{
		if (count == null)
			return to.AddYears(-1);
		var days = Math.Min(count.Value.Max(1), 18250) * 2;
		return to.AddDays(-days);
	}
}
