namespace StockSharp.SimFin;

public partial class SimFinMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask MarketDataAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		if (mdMsg.DataType2 == SimFinDataTypes.Fundamentals)
		{
			await OnFundamentalsSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		await base.MarketDataAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.Skip is < 0)
			throw new ArgumentOutOfRangeException(nameof(lookupMsg.Skip));
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			return;
		}
		var types = lookupMsg.GetSecurityTypes();
		if (types.Count > 0 && !types.Contains(SecurityTypes.Stock))
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			return;
		}
		var query = (lookupMsg.SecurityId.Native?.ToString())
			.IsEmpty(lookupMsg.SecurityId.SecurityCode)
			.IsEmpty(lookupMsg.SecurityId.Isin)
			.IsEmpty(lookupMsg.Name)
			.IsEmpty(lookupMsg.ShortName)?.Trim();
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var company in (await GetCompaniesAsync(
			cancellationToken))
			.Where(company => query.IsEmpty() ||
				company.Id.ToString(CultureInfo.InvariantCulture)
					.EqualsIgnoreCase(query) ||
				company.Ticker.Contains(query,
					StringComparison.OrdinalIgnoreCase) ||
				company.Isin.EqualsIgnoreCase(query) ||
				company.Name?.Contains(query,
					StringComparison.OrdinalIgnoreCase) == true)
			.OrderBy(static company => company.Ticker,
				StringComparer.OrdinalIgnoreCase))
		{
			if (skip-- > 0)
				continue;
			var security = company.ToSecurityMessage(
				lookupMsg.TransactionId);
			if (lookupMsg.OnlySecurityId)
				security = new()
				{
					OriginalTransactionId = lookupMsg.TransactionId,
					SecurityId = security.SecurityId,
				};
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var company = await ResolveCompanyAsync(mdMsg.SecurityId,
			cancellationToken);
		var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
		var from = (mdMsg.From ?? to.AddDays(-10))
			.ToUniversalTime();
		var latest = (await RestClient.GetPricesAsync(
			company.Ticker, from, to, IncludeRatios, AsReported,
			cancellationToken)).LastOrDefault();
		if (latest is not null)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = company.ToSecurityId(),
				ServerTime = latest.Date,
			}
			.TryAdd(Level1Fields.OpenPrice, latest.Open, true)
			.TryAdd(Level1Fields.HighPrice, latest.High, true)
			.TryAdd(Level1Fields.LowPrice, latest.Low, true)
			.TryAdd(Level1Fields.ClosePrice, latest.Close, true)
			.TryAdd(Level1Fields.LastTradePrice,
				latest.AdjustedClose ?? latest.Close, true)
			.TryAdd(Level1Fields.Volume, latest.Volume, true),
				cancellationToken);
		await CompleteSubscriptionAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var timeFrame = mdMsg.GetTimeFrame();
		if (timeFrame != TimeSpan.FromDays(1))
			throw new NotSupportedException(
				"SimFin provides daily price candles only.");
		var company = await ResolveCompanyAsync(mdMsg.SecurityId,
			cancellationToken);
		var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
		var maximum = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, MaximumRecords)
			: MaximumRecords;
		var from = (mdMsg.From ??
			to.AddDays(-Math.Min(maximum * 2L, 10000)))
			.ToUniversalTime();
		var prices = await RestClient.GetPricesAsync(company.Ticker,
			from, to, IncludeRatios, AsReported,
			cancellationToken);
		foreach (var price in prices
			.Where(price => price.Date >= from &&
				price.Date <= to)
			.TakeLast(maximum))
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = company.ToSecurityId(),
				TypedArg = timeFrame,
				OpenTime = price.Date,
				CloseTime = price.Date.AddDays(1),
				OpenPrice = price.Open ?? 0,
				HighPrice = price.High ?? 0,
				LowPrice = price.Low ?? 0,
				ClosePrice = price.AdjustedClose ??
					price.Close ?? 0,
				TotalVolume = price.Volume ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
		await CompleteSubscriptionAsync(mdMsg, cancellationToken);
	}

	private async ValueTask OnFundamentalsSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From > mdMsg.To)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From));
		var company = await ResolveCompanyAsync(mdMsg.SecurityId,
			cancellationToken);
		var maximum = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, MaximumRecords)
			: MaximumRecords;
		var values = await RestClient.GetFundamentalsAsync(
			company.Ticker,
			StatementTypes.ThrowIfEmpty(nameof(StatementTypes)),
			Period, mdMsg.From, mdMsg.To, AsReported,
			cancellationToken);
		foreach (var value in values
			.Where(value => mdMsg.From is null ||
				(value.ReportDate ?? value.PublishDate) >=
					mdMsg.From.Value.ToUniversalTime())
			.Where(value => mdMsg.To is null ||
				(value.ReportDate ?? value.PublishDate) <=
					mdMsg.To.Value.ToUniversalTime())
			.TakeLast(maximum))
		{
			var serverTime = value.PublishDate ??
				value.ReportDate ?? DateTime.MinValue;
			await SendOutMessageAsync(
				new SimFinFundamentalMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = company.ToSecurityId(),
					ServerTime = serverTime,
					Statement = value.Statement,
					Metric = value.Metric,
					RawValue = value.RawValue,
					Value = value.Value,
					Currency = value.Currency,
					FiscalYear = value.FiscalYear,
					FiscalPeriod = value.FiscalPeriod,
					ReportDate = value.ReportDate,
					PublishDate = value.PublishDate,
					Source = value.Source,
					Restated = value.Restated,
				}, cancellationToken);
		}
		await CompleteSubscriptionAsync(mdMsg, cancellationToken);
	}

	private async ValueTask CompleteSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message,
			cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
