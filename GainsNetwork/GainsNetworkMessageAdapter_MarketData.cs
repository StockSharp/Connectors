namespace StockSharp.GainsNetwork;

public partial class GainsNetworkMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var market in GetMarkets().OrderBy(static item => item.Symbol,
			StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.GainsNetwork))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.Symbol,
					StringComparison.OrdinalIgnoreCase))
				continue;
			if (securityTypes.Count > 0 &&
				!securityTypes.Contains(SecurityTypes.Future))
				continue;
			var security = CreateSecurity(market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Gains does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		var price = GetPrice(market.PairIndex);
		if (price is null || price.MarkPrice <= 0)
		{
			await RefreshChartsAsync(cancellationToken);
			price = GetPrice(market.PairIndex);
		}
		if (price is null || price.MarkPrice <= 0)
			throw new InvalidDataException("Gains returned no current price for " +
				market.Symbol + ".");
		await SendLevel1Async(market, price, mdMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions.Add(mdMsg.TransactionId, market.PairIndex);
	}

	private async ValueTask PublishLevel1Async(int pairIndex,
		CancellationToken cancellationToken)
	{
		GainsMarket market;
		GainsMarketPrice price;
		long[] subscriptions;
		using (_sync.EnterScope())
		{
			if (!_marketsByIndex.TryGetValue(pairIndex, out market) ||
				!_prices.TryGetValue(pairIndex, out price))
				return;
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value == pairIndex).Select(static pair => pair.Key)];
		}
		foreach (var transactionId in subscriptions)
			await SendLevel1Async(market, price, transactionId,
				cancellationToken);
	}

	private ValueTask SendLevel1Async(GainsMarket market,
		GainsMarketPrice price, long transactionId,
		CancellationToken cancellationToken)
	{
		var time = price.Time == default ? ServerTime : price.Time.EnsureUtc();
		var mark = price.MarkPrice > 0 ? price.MarkPrice : price.ClosePrice;
		var spread = market.SpreadPercentage / 100m;
		var bid = mark > 0 ? mark * (1m - spread) : 0m;
		var ask = mark > 0 ? mark * (1m + spread) : 0m;
		UpdateServerTime(time);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.PriceStep,
			1m / GainsNetworkExtensions.PricePrecision)
		.TryAdd(Level1Fields.VolumeStep, market.VolumeStep)
		.TryAdd(Level1Fields.State,
			market.IsEnabled && market.IsMarketOpen
				? SecurityStates.Trading
				: SecurityStates.Stoped)
		.TryAdd(Level1Fields.TheorPrice, mark > 0 ? mark : null)
		.TryAdd(Level1Fields.Index,
			price.IndexPrice > 0 ? price.IndexPrice : null)
		.TryAdd(Level1Fields.BestBidPrice, bid > 0 ? bid : null)
		.TryAdd(Level1Fields.BestBidTime, bid > 0 ? time : null)
		.TryAdd(Level1Fields.BestAskPrice, ask > 0 ? ask : null)
		.TryAdd(Level1Fields.BestAskTime, ask > 0 ? time : null)
		.TryAdd(Level1Fields.OpenPrice,
			price.OpenPrice > 0 ? price.OpenPrice : null)
		.TryAdd(Level1Fields.HighPrice,
			price.HighPrice > 0 ? price.HighPrice : null)
		.TryAdd(Level1Fields.LowPrice,
			price.LowPrice > 0 ? price.LowPrice : null)
		.TryAdd(Level1Fields.ClosePrice,
			price.ClosePrice > 0 ? price.ClosePrice : null), cancellationToken);
	}

	private static SecurityMessage CreateSecurity(GainsMarket market,
		long transactionId)
	{
		var message = new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Symbol + " Gains perpetual",
			ShortName = market.Symbol,
			Class = market.Group,
			SecurityType = SecurityTypes.Future,
			Currency = Enum.TryParse<CurrencyTypes>(market.QuoteAsset, true,
				out var currency) ? currency : null,
			PriceStep = 1m / GainsNetworkExtensions.PricePrecision,
			VolumeStep = market.VolumeStep,
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		};
		message.TryFillUnderlyingId(market.BaseAsset);
		return message;
	}
}
