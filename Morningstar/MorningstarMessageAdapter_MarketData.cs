namespace StockSharp.Morningstar;

public partial class MorningstarMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var identifier = lookupMsg.SecurityId.Native as string;
		if (identifier.IsEmpty())
			identifier = lookupMsg.SecurityId.SecurityCode;
		var board = lookupMsg.SecurityId.BoardCode;
		if (board.EqualsIgnoreCase(_boardCode) ||
			InvestmentSource != MorningstarInvestmentSources.Equities)
			board = null;

		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var token = default(string);
		var seenTokens = new HashSet<string>(StringComparer.Ordinal);
		while (left > 0)
		{
			var response = await SafeClient().GetInvestments(InvestmentSource, Universe,
				board, token, cancellationToken);
			foreach (var investment in response.Investments ?? [])
			{
				CacheInvestment(investment);
				if (!investment.Matches(identifier))
					continue;
				var security = investment.ToSecurityMessage(lookupMsg.TransactionId);
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}

			var next = response.Metadata?.PaginationTokenNext;
			if (next.IsEmpty() || !seenTokens.Add(next))
				break;
			token = next;
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

		var isHistory = mdMsg.From != null || mdMsg.To != null || mdMsg.Count != null;
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime().Date;
		var from = mdMsg.From?.ToUniversalTime().Date ?? GetDefaultFrom(to, mdMsg.Count);
		if (from > to)
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from, "The start date is after the end date.");

		var (identifier, identifierType) = GetIdentifier(mdMsg.SecurityId);
		var response = await SafeClient().GetDailyOhlcv(identifier, identifierType,
			mdMsg.SecurityId.BoardCode, from, to, Currency, cancellationToken);
		var investment = SelectInvestment(response, identifier, identifierType);
		IEnumerable<MorningstarDailyOhlcv> points = investment.TimeSeries?.Data ?? [];
		points = points.Where(point => point.GetTime() != null)
			.OrderBy(point => point.GetTime());
		if (mdMsg.Count is long count)
			points = points.TakeLast(checked((int)Math.Min(count.Max(0), int.MaxValue)));
		if (!isHistory)
			points = points.TakeLast(1);
		var data = points.ToArray();
		if (data.Length == 0)
			throw new InvalidOperationException(
				$"Morningstar returned no daily OHLCV values for '{identifier}'.");

		var securityId = GetResultSecurityId(mdMsg.SecurityId, investment);
		foreach (var point in data)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = point.GetTime().Value,
			}
			.TryAdd(Level1Fields.OpenPrice, point.Open)
			.TryAdd(Level1Fields.HighPrice, point.High)
			.TryAdd(Level1Fields.LowPrice, point.Low)
			.TryAdd(Level1Fields.ClosePrice, point.Close)
			.TryAdd(Level1Fields.Volume, point.Volume), cancellationToken);
		}

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
				"Morningstar daily OHLCV is available only at its native daily frequency.");

		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime().Date;
		var from = mdMsg.From?.ToUniversalTime().Date ?? GetDefaultFrom(to, mdMsg.Count);
		if (from > to)
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from, "The start date is after the end date.");

		var (identifier, identifierType) = GetIdentifier(mdMsg.SecurityId);
		var response = await SafeClient().GetDailyOhlcv(identifier, identifierType,
			mdMsg.SecurityId.BoardCode, from, to, Currency, cancellationToken);
		var investment = SelectInvestment(response, identifier, identifierType);
		IEnumerable<MorningstarDailyOhlcv> points = investment.TimeSeries?.Data ?? [];
		points = points.Where(point => point.HasOhlc && point.GetTime() != null)
			.OrderBy(point => point.GetTime());
		if (mdMsg.Count is long count)
		{
			var take = checked((int)Math.Min(count.Max(0), int.MaxValue));
			points = mdMsg.From == null ? points.TakeLast(take) : points.Take(take);
		}
		var data = points.ToArray();
		if (data.Length == 0)
			throw new InvalidOperationException(
				$"Morningstar returned no complete daily OHLCV values for '{identifier}'.");

		var securityId = GetResultSecurityId(mdMsg.SecurityId, investment);
		foreach (var point in data)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				DataType = mdMsg.DataType2,
				TypedArg = timeFrame,
				OpenTime = point.GetTime().Value,
				OpenPrice = point.Open.Value,
				HighPrice = point.High.Value,
				LowPrice = point.Low.Value,
				ClosePrice = point.Close.Value,
				TotalVolume = point.Volume ?? 0m,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private static MorningstarDailyOhlcvInvestment SelectInvestment(
		MorningstarDailyOhlcvResponse response, string identifier,
		MorningstarIdentifierTypes identifierType)
	{
		var investments = (response.Investments ?? [])
			.Where(investment => investment?.TimeSeries?.Data != null)
			.ToArray();
		var exact = investments.Where(investment =>
			investment.Matches(identifier, identifierType)).ToArray();
		if (exact.Length == 1)
			return exact[0];
		if (exact.Length > 1 || investments.Length > 1)
			throw new InvalidOperationException(
				$"Morningstar identifier '{identifier}' is ambiguous. Specify its MIC board or Performance ID.");
		if (investments.Length == 1)
			return investments[0];

		var warning = response.Metadata?.Messages?.Message;
		if (warning.IsEmpty())
			warning = "No entitled investment matched the request.";
		throw new InvalidOperationException($"Morningstar: {warning}");
	}

	private static SecurityId GetResultSecurityId(SecurityId requested,
		MorningstarDailyOhlcvInvestment investment)
	{
		var ids = investment.Identifiers;
		if (requested.SecurityCode.IsEmpty())
			requested.SecurityCode = ids?.TradingSymbol.IsEmpty(ids?.PerformanceId);
		if (requested.BoardCode.IsEmpty())
			requested.BoardCode = _boardCode;
		var performanceId = ids?.PerformanceId;
		if (requested.Native == null && !performanceId.IsEmpty())
			requested.Native = performanceId;
		if (requested.Isin.IsEmpty())
			requested.Isin = ids?.Isin;
		if (requested.Cusip.IsEmpty())
			requested.Cusip = ids?.Cusip;
		if (requested.Sedol.IsEmpty())
			requested.Sedol = ids?.Sedol;
		return requested;
	}

	private static DateTime GetDefaultFrom(DateTime to, long? count)
	{
		if (count == null)
			return to.AddYears(-1);
		var days = Math.Min(count.Value.Max(1), 18250) * 2;
		return to.AddDays(-days);
	}
}
