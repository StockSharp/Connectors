namespace StockSharp.Birdeye;

public partial class BirdeyeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		if (securityTypes.Count > 0 &&
			!securityTypes.Contains(
				SecurityTypes.CryptoCurrency))
		{
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
			return;
		}
		var requested =
			(lookupMsg.SecurityId.Native as string)
				.IsEmpty(lookupMsg.SecurityId.SecurityCode)
				.IsEmpty(lookupMsg.Name);
		var address =
			lookupMsg.SecurityId.Native as string;
		if (address.IsEmpty())
			address = TokenAddress;
		BirdeyeToken[] tokens;
		if (BirdeyeExtensions.IsSafeAddress(address))
		{
			var overview = await RestClient.GetOverviewAsync(
				address, cancellationToken);
			tokens = overview is null ? [] : [overview];
		}
		else
			tokens = await RestClient.GetTokensAsync(
				MinimumLiquidity,
				MaximumItems,
				cancellationToken);
		RememberTokens(tokens);
		var skip = Math.Max(0L, lookupMsg.Skip ?? 0);
		var left = Math.Min(
			lookupMsg.Count ?? MaximumItems,
			MaximumItems);
		foreach (var token in tokens
			.Where(token => Matches(token, requested))
			.OrderByDescending(static token =>
				token.Liquidity ?? 0)
			.ThenBy(static token =>
				token.Symbol,
				StringComparer.OrdinalIgnoreCase))
		{
			var security = CreateSecurity(
				token, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(
				security, cancellationToken);
			await SendLevel1Async(
				token,
				token,
				lookupMsg.TransactionId,
				cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId);
			await RefreshStreamSubscriptionsAsync(
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Birdeye does not expose historical Level1 events.");
		var token = await ResolveTokenAsync(
			mdMsg.SecurityId, cancellationToken);
		var snapshot = await RestClient.GetOverviewAsync(
			token.Address, cancellationToken);
		if (snapshot is null)
			throw new InvalidDataException(
				$"Birdeye returned no overview for " +
					$"'{token.Address}'.");
		RememberTokens([snapshot]);
		await SendLevel1Async(
			token,
			snapshot,
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Token = token,
				LastUpdate = CurrentTime,
			};
		try
		{
			await RefreshStreamSubscriptionsAsync(
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(
					mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(
			mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(
					mdMsg.OriginalTransactionId);
			await RefreshStreamSubscriptionsAsync(
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		var token = await ResolveTokenAsync(
			mdMsg.SecurityId, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToInterval();
		if (timeFrame < TimeSpan.FromMinutes(1) &&
			!Chain.EqualsIgnoreCase("solana"))
			throw new NotSupportedException(
				"Birdeye sub-minute candles are available only " +
					"for Solana.");
		var to = (mdMsg.To ?? DateTime.UtcNow)
			.ToUniversalTime();
		var maximum = (mdMsg.Count ??
			(mdMsg.From is null ? 1 : HistoryLimit))
			.Max(1)
			.Min(HistoryLimit)
			.To<int>();
		var from = (mdMsg.From ??
			to - timeFrame * maximum)
			.ToUniversalTime();
		foreach (var candle in
			(await RestClient.GetCandlesAsync(
				token.Address,
				timeFrame,
				from,
				to,
				PriceInUsd,
				cancellationToken) ?? [])
			.Where(candle =>
				candle.OpenTime >= from &&
				candle.OpenTime <= to)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(maximum))
			await SendCandleAsync(
				token,
				candle,
				timeFrame,
				mdMsg.TransactionId,
				cancellationToken);
		if (!StreamingEnabled || mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				Token = token,
				TimeFrame = timeFrame,
			};
		try
		{
			await RefreshStreamSubscriptionsAsync(
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(
					mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(
			mdMsg, cancellationToken);
	}

	private async ValueTask<BirdeyeToken> ResolveTokenAsync(
		SecurityId securityId,
		CancellationToken cancellationToken)
	{
		try
		{
			return GetToken(securityId);
		}
		catch (InvalidOperationException)
		{
			var address =
				securityId.Native as string;
			if (address.IsEmpty() &&
				BirdeyeExtensions.IsSafeAddress(
					securityId.SecurityCode))
				address = securityId.SecurityCode;
			BirdeyeToken[] tokens;
			if (!address.IsEmpty())
			{
				var overview =
					await RestClient.GetOverviewAsync(
						address, cancellationToken);
				tokens =
					overview is null ? [] : [overview];
			}
			else
				tokens = await RestClient.GetTokensAsync(
					MinimumLiquidity,
					MaximumItems,
					cancellationToken);
			RememberTokens(tokens);
			return GetToken(securityId);
		}
	}

	private async ValueTask OnStreamCandleAsync(
		BirdeyeCandle candle,
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, Level1Subscription>[] level1;
		KeyValuePair<long, CandleSubscription>[] candles;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Token.Address.EqualsIgnoreCase(
					candle.Address) &&
				candle.TimeFrame ==
					TimeSpan.FromMinutes(1))];
			candles = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Token.Address.EqualsIgnoreCase(
					candle.Address) &&
				pair.Value.TimeFrame == candle.TimeFrame)];
		}
		foreach (var pair in level1)
			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					SecurityId =
						pair.Value.Token.ToStockSharp(),
					ServerTime =
						candle.OpenTime +
							(candle.TimeFrame ??
								TimeSpan.FromMinutes(1)),
					OriginalTransactionId = pair.Key,
				}
				.TryAdd(
					Level1Fields.LastTradePrice,
					candle.Close)
				.TryAdd(
					Level1Fields.Volume,
					candle.VolumeUsd ??
						candle.Volume)
				.TryAdd(
					Level1Fields.State,
					SecurityStates.Trading),
				cancellationToken);
		foreach (var pair in candles)
			await SendCandleAsync(
				pair.Value.Token,
				candle,
				pair.Value.TimeFrame,
				pair.Key,
				cancellationToken);
	}

	private ValueTask SendLevel1Async(
		BirdeyeToken token,
		BirdeyeToken snapshot,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = token.ToStockSharp(),
				ServerTime =
					snapshot.LastTradeTime ?? CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				Level1Fields.LastTradePrice,
				snapshot.Price)
			.TryAdd(
				Level1Fields.Volume,
				snapshot.Volume24Hours)
			.TryAdd(
				Level1Fields.Change,
				snapshot.PriceChange24Hours)
			.TryAdd(
				Level1Fields.State,
				SecurityStates.Trading),
			cancellationToken);

	private ValueTask SendCandleAsync(
		BirdeyeToken token,
		BirdeyeCandle candle,
		TimeSpan timeFrame,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new TimeFrameCandleMessage
			{
				SecurityId = token.ToStockSharp(),
				TypedArg = timeFrame,
				OpenTime = candle.OpenTime,
				CloseTime = candle.OpenTime + timeFrame,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State =
					candle.OpenTime + timeFrame <= DateTime.UtcNow
						? CandleStates.Finished
						: CandleStates.Active,
				OriginalTransactionId =
					originalTransactionId,
			},
			cancellationToken);

	private static SecurityMessage CreateSecurity(
		BirdeyeToken token,
		long originalTransactionId)
		=> new()
		{
			SecurityId = token.ToStockSharp(),
			Name = token.Name.IsEmpty()
				? token.Symbol
				: token.Name,
			ShortName = token.Symbol,
			Class = token.Chain,
			SecurityType = SecurityTypes.CryptoCurrency,
			Decimals = token.Decimals,
			OriginalTransactionId = originalTransactionId,
		};

	private static bool Matches(
		BirdeyeToken token,
		string requested)
		=> requested.IsEmpty() ||
			token.Address.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			token.Symbol.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) ||
			token.Name?.Contains(
				requested,
				StringComparison.OrdinalIgnoreCase) == true;

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
