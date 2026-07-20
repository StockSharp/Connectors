namespace StockSharp.Jupiter;

public partial class JupiterMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedCode = lookupMsg.SecurityId.SecurityCode?.Trim();
		JupiterMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static item =>
			item.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Jupiter))
				continue;
			if (!requestedCode.IsEmpty() &&
				!requestedCode.EqualsIgnoreCase(market.SecurityCode))
				continue;
			var security = CreateSecurity(market,
				lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State, SecurityStates.Trading),
				cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
				RemoveFingerprintPrefix(_level1Fingerprints,
					mdMsg.OriginalTransactionId);
			}
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Jupiter does not expose historical Level1 events.");
		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1Async(market, mdMsg.TransactionId, true,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Market = market,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(JupiterMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Kind == JupiterMarketKinds.Spot
				? $"{market.BaseToken.Name}/{market.QuoteToken.Symbol}"
				: $"{market.BaseToken.Name} perpetual",
			ShortName = market.SecurityCode,
			SecurityType = market.Kind == JupiterMarketKinds.Spot
				? SecurityTypes.CryptoCurrency
				: SecurityTypes.Future,
			Currency = market.Kind == JupiterMarketKinds.Perpetual
				? CurrencyTypes.USD
				: ToCurrencyType(market.QuoteToken.Symbol),
			PriceStep = DecimalStep(market.QuoteToken.Decimals),
			VolumeStep = DecimalStep(market.BaseToken.Decimals),
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.BaseToken.Symbol);

	private async ValueTask SendLevel1Async(JupiterMarket market, long target,
		bool isForced, CancellationToken cancellationToken)
	{
		var snapshot = market.Kind == JupiterMarketKinds.Spot
			? await LoadSpotLevel1Async(market, cancellationToken)
			: await LoadPerpetualLevel1Async(market, cancellationToken);
		await SendLevel1Async(market, target, isForced, snapshot,
			cancellationToken);
	}

	private async ValueTask SendLevel1Async(JupiterMarket market, long target,
		bool isForced,
		(decimal Bid, decimal Ask, decimal Last, decimal High, decimal Low,
			decimal Volume, decimal Change) snapshot,
		CancellationToken cancellationToken)
	{
		var fingerprint = new Level1Fingerprint(snapshot.Bid, snapshot.Ask,
			snapshot.Last, snapshot.High, snapshot.Low, snapshot.Volume,
			snapshot.Change);
		var key = $"{target}:{market.SecurityCode}";
		using (_sync.EnterScope())
		{
			if (!isForced && _level1Fingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_level1Fingerprints[key] = fingerprint;
			if (snapshot.Last > 0)
				market.LastPrice = snapshot.Last;
		}
		var message = new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
		};
		if (snapshot.Bid > 0)
			message
				.TryAdd(Level1Fields.BestBidPrice, snapshot.Bid)
				.TryAdd(Level1Fields.BestBidVolume, ProbeVolume);
		if (snapshot.Ask > 0)
			message
				.TryAdd(Level1Fields.BestAskPrice, snapshot.Ask)
				.TryAdd(Level1Fields.BestAskVolume, ProbeVolume);
		if (snapshot.Last > 0)
			message.TryAdd(Level1Fields.LastTradePrice, snapshot.Last);
		if (snapshot.High > 0)
			message.TryAdd(Level1Fields.HighPrice, snapshot.High);
		if (snapshot.Low > 0)
			message.TryAdd(Level1Fields.LowPrice, snapshot.Low);
		if (snapshot.Volume > 0)
			message.TryAdd(Level1Fields.Volume, snapshot.Volume);
		if (snapshot.Change != 0)
			message.TryAdd(Level1Fields.Change, snapshot.Change);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask<
		(decimal Bid, decimal Ask, decimal Last, decimal High, decimal Low,
			decimal Volume, decimal Change)> LoadSpotLevel1Async(
		JupiterMarket market, CancellationToken cancellationToken)
	{
		var units = ProbeVolume.ToRawAmount(market.BaseToken,
			DateTime.UtcNow).ToString(CultureInfo.InvariantCulture);
		var bidOrder = await ApiClient.GetSwapOrderAsync(new()
		{
			InputMint = market.BaseToken.Mint,
			OutputMint = market.QuoteToken.Mint,
			Amount = units,
			SwapMode = JupiterSwapModes.ExactInput,
		}, cancellationToken);
		ValidateSwapQuote(bidOrder, market, JupiterSwapModes.ExactInput);
		var askOrder = await ApiClient.GetSwapOrderAsync(new()
		{
			InputMint = market.QuoteToken.Mint,
			OutputMint = market.BaseToken.Mint,
			Amount = units,
			SwapMode = JupiterSwapModes.ExactOutput,
		}, cancellationToken);
		ValidateSwapQuote(askOrder, market, JupiterSwapModes.ExactOutput);
		var bidOutput = bidOrder.OutputAmount.FromRawAmount(
			market.QuoteToken, DateTime.UtcNow);
		var askInput = askOrder.InputAmount.FromRawAmount(
			market.QuoteToken, DateTime.UtcNow);
		var bid = bidOutput / ProbeVolume;
		var ask = askInput / ProbeVolume;
		if (bid <= 0 || ask <= 0)
			throw new InvalidDataException(
				"Jupiter returned a non-positive executable quote.");
		return (bid, ask, 0m, 0m, 0m, 0m, 0m);
	}

	private async ValueTask<
		(decimal Bid, decimal Ask, decimal Last, decimal High, decimal Low,
			decimal Volume, decimal Change)> LoadPerpetualLevel1Async(
		JupiterMarket market, CancellationToken cancellationToken)
	{
		var stats = await ApiClient.GetPerpetualStatsAsync(
			market.BaseToken.Mint, cancellationToken);
		var last = JupiterExtensions.ParseDecimal(stats.Price,
			"perpetual price");
		var high = JupiterExtensions.ParseDecimal(stats.PriceHigh24Hours,
			"24-hour high");
		var low = JupiterExtensions.ParseDecimal(stats.PriceLow24Hours,
			"24-hour low");
		var usdVolume = JupiterExtensions.ParseDecimal(stats.Volume,
			"24-hour volume");
		var changePercent = JupiterExtensions.ParseDecimal(
			stats.PriceChange24Hours, "24-hour price change");
		if (last <= 0 || high <= 0 || low <= 0 || usdVolume < 0)
			throw new InvalidDataException(
				"Jupiter returned invalid perpetual market statistics.");
		return (0m, 0m, last, high, low, usdVolume / last,
			last * changePercent / 100m);
	}

	private static void ValidateSwapQuote(JupiterSwapOrder order,
		JupiterMarket market, JupiterSwapModes expectedMode)
	{
		ArgumentNullException.ThrowIfNull(order);
		if (!order.Error.IsEmpty())
			throw new JupiterApiException(order.ErrorMessage ?? order.Error);
		var expectedInput = expectedMode == JupiterSwapModes.ExactInput
			? market.BaseToken.Mint
			: market.QuoteToken.Mint;
		var expectedOutput = expectedMode == JupiterSwapModes.ExactInput
			? market.QuoteToken.Mint
			: market.BaseToken.Mint;
		if (order.InputMint != expectedInput ||
			order.OutputMint != expectedOutput ||
			order.SwapMode != expectedMode ||
			order.InputAmount.IsEmpty() || order.OutputAmount.IsEmpty())
			throw new InvalidDataException(
				"Jupiter returned a quote for an unexpected swap request.");
	}

	private async ValueTask PollMarketAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, Level1Subscription>[] targets;
		using (_sync.EnterScope())
			targets = [.. _level1Subscriptions];
		foreach (var group in targets.GroupBy(static pair =>
			pair.Value.Market.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			var market = group.First().Value.Market;
			var snapshot = market.Kind == JupiterMarketKinds.Spot
				? await LoadSpotLevel1Async(market, cancellationToken)
				: await LoadPerpetualLevel1Async(market, cancellationToken);
			foreach (var target in group)
				await SendLevel1Async(market, target.Key, false, snapshot,
					cancellationToken);
		}
	}

	private static decimal? DecimalStep(int decimals)
	{
		if (decimals is < 0 or > 28)
			return null;
		var result = 1m;
		for (var index = 0; index < decimals; index++)
			result /= 10m;
		return result;
	}

	private static CurrencyTypes? ToCurrencyType(string symbol)
		=> symbol?.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" or "JUPUSD" => CurrencyTypes.USD,
			"EUR" or "EURC" => CurrencyTypes.EUR,
			"GBP" => CurrencyTypes.GBP,
			"JPY" => CurrencyTypes.JPY,
			_ => null,
		};

	private static void RemoveFingerprintPrefix<TValue>(
		IDictionary<string, TValue> values, long target)
	{
		var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
		foreach (var key in values.Keys.Where(key =>
			key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
			values.Remove(key);
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
