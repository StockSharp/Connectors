namespace StockSharp.NasdaqCloudDataService;

public partial class NasdaqCloudDataServiceMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var client = SafeClient();
		var value = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode);
		var board = lookupMsg.SecurityId.BoardCode;
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async Task Emit(SecurityMessage security)
		{
			if (left <= 0 || security == null ||
				!security.IsMatch(lookupMsg, securityTypes) ||
				!sent.Add($"{security.SecurityId.BoardCode}:{security.SecurityId.SecurityCode}"))
			{
				return;
			}
			if (skip > 0)
			{
				skip--;
				return;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}

		if (!value.IsEmpty() && !board.IsEmpty())
		{
			try
			{
				switch (board.ToUpperInvariant())
				{
					case _equityBoard:
						await Emit((await client.GetSymbol(value, cancellationToken))
							?.ToSecurityMessage(lookupMsg.TransactionId));
						break;
					case _indexBoard:
						await Emit((await client.GetIndex(value, cancellationToken))
							?.ToSecurityMessage(lookupMsg.TransactionId));
						break;
					case _etpBoard:
						await Emit((await client.GetEtp(value, cancellationToken))
							?.ToSecurityMessage(lookupMsg.TransactionId));
						break;
					case _optionBoard:
						if (NasdaqCloudOptionKey.TryParse(value, out var exactKey))
						{
							foreach (var contract in await client.GetOptionContracts(
								exactKey.Root, cancellationToken))
							{
								if (NasdaqCloudOptionKey.TryParse(contract.Identifier, out var key) &&
									key.Code.EqualsIgnoreCase(exactKey.Code))
								{
									await Emit(contract.ToSecurityMessage(lookupMsg.TransactionId));
									break;
								}
							}
						}
						break;
				}
			}
			catch (NasdaqCloudApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
			}
		}
		else
		{
			var allowsStocks = securityTypes.Count == 0 ||
				securityTypes.Contains(SecurityTypes.Stock);
			var allowsEtps = securityTypes.Count == 0 ||
				securityTypes.Contains(SecurityTypes.Etf);
			var allowsIndexes = securityTypes.Count == 0 ||
				securityTypes.Contains(SecurityTypes.Index);

			if (allowsStocks && left > 0)
			{
				foreach (var equity in await client.GetSymbols(cancellationToken))
				{
					if (equity?.Symbol.IsEmpty() != false || !equity.Matches(value))
						continue;
					await Emit(equity.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}

			if (allowsEtps && left > 0)
			{
				foreach (var etp in await client.GetEtps(cancellationToken))
				{
					if (etp?.Symbol.IsEmpty() != false || !etp.Matches(value))
						continue;
					await Emit(etp.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}

			if (allowsIndexes && left > 0)
			{
				foreach (var index in await client.GetIndexes(cancellationToken))
				{
					if (index?.Instrument.IsEmpty() != false || !index.Matches(value))
						continue;
					await Emit(index.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}
		}

		var exactOption = NasdaqCloudOptionKey.TryParse(value, out var optionKey);
		var underlying = lookupMsg.GetUnderlyingCode();
		var optionsRequested = securityTypes.Contains(SecurityTypes.Option) ||
			!underlying.IsEmpty() || exactOption;
		if (optionsRequested && left > 0)
		{
			if (exactOption)
				underlying = optionKey.Root;
			else if (underlying.IsEmpty() && securityTypes.Contains(SecurityTypes.Option))
				underlying = value;

			if (!underlying.IsEmpty())
			{
				foreach (var contract in await client.GetOptionContracts(
					underlying, cancellationToken))
				{
					if (contract?.Identifier.IsEmpty() != false ||
						(exactOption && (!NasdaqCloudOptionKey.TryParse(
							contract.Identifier, out var contractKey) ||
							!contractKey.Code.EqualsIgnoreCase(optionKey.Code))))
					{
						continue;
					}
					await Emit(contract.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
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
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (mdMsg.From != null || mdMsg.To != null || mdMsg.Count is > 1)
			throw new NotSupportedException(
				"Nasdaq Cloud REST Level1 endpoints provide only the latest entitled snapshot.");

		var code = GetCode(mdMsg.SecurityId);
		var board = mdMsg.SecurityId.BoardCode;
		var sent = board.EqualsIgnoreCase(_indexBoard)
			? await SendIndexLevel1(mdMsg, code, cancellationToken)
			: board.EqualsIgnoreCase(_etpBoard)
				? await SendEtpLevel1(mdMsg, code, cancellationToken)
				: board.EqualsIgnoreCase(_optionBoard) ||
					NasdaqCloudOptionKey.TryParse(code, out _)
					? await SendOptionLevel1(mdMsg, code, cancellationToken)
					: await SendEquityLevel1(mdMsg, code, cancellationToken);

		if (sent == 0)
			throw new InvalidOperationException(
				$"Nasdaq Cloud returned no entitled Level1 data for '{code}'.");
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
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (!mdMsg.SecurityId.BoardCode.IsEmpty() &&
			!mdMsg.SecurityId.BoardCode.EqualsIgnoreCase(_equityBoard))
		{
			throw new NotSupportedException(
				"Nasdaq Cloud REST bars are available for equities only.");
		}

		var code = GetCode(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToNasdaqCloudPrecision();
		var now = DateTime.UtcNow;
		var to = (mdMsg.To ?? now).ToUniversalTime();
		var from = mdMsg.From?.ToUniversalTime() ?? EstimateFrom(to, timeFrame, mdMsg.Count);
		if (from > to)
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from, "The start date is after the end date.");

		NasdaqCloudBarRanges range;
		if (Source is NasdaqCloudSources.Bx or NasdaqCloudSources.Psx)
		{
			if (now - from > TimeSpan.FromDays(5) || to - from > TimeSpan.FromDays(5))
				throw new NotSupportedException(
					$"Nasdaq Cloud {Source} bars retain at most five days of history.");
			range = NasdaqCloudBarRanges.FiveDays;
		}
		else
			range = SelectRange(timeFrame, now - from);

		var bars = await SafeClient().GetBars(
			Source, Offset, code, timeFrame, range, from, to, cancellationToken);
		var parsed = new List<(DateTime time, NasdaqCloudBar bar)>();
		foreach (var bar in bars.WhereNotNull())
		{
			var time = bar.Timestamp.ToNasdaqCloudTime();
			if (time == null || time < from || time > to ||
				bar.Open == null || bar.High == null || bar.Low == null || bar.Close == null)
			{
				continue;
			}
			parsed.Add((time.Value, bar));
		}

		IEnumerable<(DateTime time, NasdaqCloudBar bar)> selected = parsed
			.OrderBy(item => item.time);
		if (mdMsg.Count is long count)
		{
			var take = checked((int)Math.Min(count.Max(0), int.MaxValue));
			selected = mdMsg.From == null ? selected.TakeLast(take) : selected.Take(take);
		}
		else if (mdMsg.From == null)
			selected = selected.TakeLast(1);

		var securityId = mdMsg.SecurityId.NormalizeNasdaqCloud(
			code, _equityBoard, code);
		var sent = 0;
		foreach (var item in selected)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				DataType = mdMsg.DataType2,
				TypedArg = timeFrame,
				OpenTime = item.time,
				OpenPrice = item.bar.Open.Value,
				HighPrice = item.bar.High.Value,
				LowPrice = item.bar.Low.Value,
				ClosePrice = item.bar.Close.Value,
				TotalVolume = item.bar.Volume ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
			sent++;
		}

		if (sent == 0)
			this.AddWarningLog("Nasdaq Cloud returned no complete bars for {0}.", code);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<int> SendEquityLevel1(MarketDataMessage mdMsg, string code,
		CancellationToken cancellationToken)
	{
		var client = SafeClient();
		var snapshots = await TryEntitled(
			() => client.GetEquitySnapshots(Source, Offset, code, cancellationToken),
			"equity snapshot");
		var quotes = await TryEntitled(
			() => client.GetLastQuotes(Source, Offset, code, cancellationToken),
			"last quote");
		var sales = await TryEntitled(
			() => client.GetLastSales(Source, Offset, code, cancellationToken),
			"last sale");
		var securityId = mdMsg.SecurityId.NormalizeNasdaqCloud(code, _equityBoard, code);
		var sent = 0;

		var snapshot = snapshots?.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(code));
		if (snapshot != null)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = RequireTime(snapshot.Timestamp, "equity snapshot"),
			}
			.TryAdd(Level1Fields.OpenPrice, snapshot.Open)
			.TryAdd(Level1Fields.HighPrice, snapshot.High)
			.TryAdd(Level1Fields.LowPrice, snapshot.Low)
			.TryAdd(Level1Fields.ClosePrice, snapshot.Close)
			.TryAdd(Level1Fields.LastTradePrice, snapshot.LastTrade)
			.TryAdd(Level1Fields.Volume, snapshot.Volume)
			.TryAdd(Level1Fields.MarketPriceYesterday, snapshot.PreviousClose)
			.TryAdd(Level1Fields.Turnover, snapshot.DollarVolume)
			.TryAdd(Level1Fields.Change, snapshot.PercentChange), cancellationToken);
			sent++;
		}

		var quote = quotes?.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(code));
		if (quote != null)
		{
			var time = RequireTime(quote.Timestamp, "last quote");
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = time,
			}
			.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
			.TryAdd(Level1Fields.BestBidTime, quote.BidPrice != null ? time : null)
			.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, quote.AskSize)
			.TryAdd(Level1Fields.BestAskTime, quote.AskPrice != null ? time : null),
				cancellationToken);
			sent++;
		}

		var sale = sales?.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(code));
		if (sale != null)
		{
			var time = RequireTime(sale.Timestamp, "last sale");
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = time,
			}
			.TryAdd(Level1Fields.LastTradePrice, sale.Price)
			.TryAdd(Level1Fields.LastTradeVolume, sale.Size)
			.TryAdd(Level1Fields.LastTradeTime, sale.Price != null ? time : null)
			.TryAdd(Level1Fields.LastTradeUpDown, sale.ChangeIndicator.ToUpTick()),
				cancellationToken);
			sent++;
		}

		return sent;
	}

	private async Task<int> SendIndexLevel1(MarketDataMessage mdMsg, string code,
		CancellationToken cancellationToken)
	{
		var client = SafeClient();
		var values = await TryEntitled(
			() => client.GetIndexValues(Offset, code, cancellationToken), "index value");
		var snapshots = await TryEntitled(
			() => client.GetIndexSnapshots(Offset, code, cancellationToken), "index snapshot");
		var securityId = mdMsg.SecurityId.NormalizeNasdaqCloud(code, _indexBoard, code);
		var sent = 0;

		var value = values?.FirstOrDefault(item => item.Instrument.EqualsIgnoreCase(code));
		if (value != null)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = RequireTime(value.Timestamp, "index value"),
			}.TryAdd(Level1Fields.ClosePrice, value.TickValue), cancellationToken);
			sent++;
		}

		var snapshot = snapshots?.FirstOrDefault(item => item.Instrument.EqualsIgnoreCase(code));
		if (snapshot != null)
		{
			await SendSummary(mdMsg.TransactionId, securityId, snapshot,
				"index snapshot", cancellationToken);
			sent++;
		}
		return sent;
	}

	private async Task<int> SendEtpLevel1(MarketDataMessage mdMsg, string code,
		CancellationToken cancellationToken)
	{
		var client = SafeClient();
		var details = await TryEntitled(
			() => client.GetEtp(code, cancellationToken), "ETP details");
		var ipvSymbol = details?.IpvSymbol.IsEmpty(code) ?? code;
		var values = await TryEntitled(
			() => client.GetEtpValues(Offset, ipvSymbol, cancellationToken), "ETP value");
		var snapshots = await TryEntitled(
			() => client.GetEtpSnapshots(Offset, ipvSymbol, cancellationToken), "ETP snapshot");
		var securityId = mdMsg.SecurityId.NormalizeNasdaqCloud(code, _etpBoard, code);
		var sent = 0;

		var value = values?.FirstOrDefault(item => item.IpvSymbol.EqualsIgnoreCase(ipvSymbol));
		if (value != null)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = RequireTime(value.Timestamp, "ETP value"),
			}.TryAdd(Level1Fields.ClosePrice, value.IpvValue), cancellationToken);
			sent++;
		}

		var snapshot = snapshots?.FirstOrDefault(item =>
			item.IpvSymbol.EqualsIgnoreCase(ipvSymbol));
		if (snapshot != null)
		{
			await SendSummary(mdMsg.TransactionId, securityId, snapshot,
				"ETP snapshot", cancellationToken);
			sent++;
		}
		return sent;
	}

	private async Task<int> SendOptionLevel1(MarketDataMessage mdMsg, string code,
		CancellationToken cancellationToken)
	{
		var native = (mdMsg.SecurityId.Native as string).IsEmpty(code);
		if (!NasdaqCloudOptionKey.TryParse(native, out var key))
			throw new ArgumentException(
				$"Nasdaq Cloud option code '{native}' is not a valid OSI identifier.", nameof(mdMsg));

		var client = SafeClient();
		var prices = await TryEntitled(
			() => client.GetOptionPrices(Offset, key.ApiIdentifier, cancellationToken),
			"option prices");
		var price = prices?.FirstOrDefault(item =>
			NasdaqCloudOptionKey.TryParse(item.Identifier, out var itemKey) &&
			itemKey.Code.EqualsIgnoreCase(key.Code));
		var securityId = mdMsg.SecurityId.NormalizeNasdaqCloud(
			key.Code, _optionBoard, key.ApiIdentifier);
		var sent = 0;
		if (price != null)
		{
			var tradeTime = price.TradeTimestamp.ToNasdaqCloudTime();
			var bidTime = price.BidTimestamp.ToNasdaqCloudTime();
			var askTime = price.AskTimestamp.ToNasdaqCloudTime();
			var time = new[] { tradeTime, bidTime, askTime }.WhereNotNull()
				.DefaultIfEmpty().Max();
			if (time == default)
				throw new InvalidOperationException(
					$"Nasdaq Cloud option '{key.Code}' response has no market timestamp.");

			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = time,
			}
			.TryAdd(Level1Fields.LastTradePrice, price.LastPrice)
			.TryAdd(Level1Fields.LastTradeVolume, price.LastSize)
			.TryAdd(Level1Fields.LastTradeTime, tradeTime)
			.TryAdd(Level1Fields.BestBidPrice, price.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, price.BidSize)
			.TryAdd(Level1Fields.BestBidTime, bidTime)
			.TryAdd(Level1Fields.BestAskPrice, price.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, price.AskSize)
			.TryAdd(Level1Fields.BestAskTime, askTime)
			.TryAdd(Level1Fields.Volume, price.Volume)
			.TryAdd(Level1Fields.OpenInterest, price.OpenInterest), cancellationToken);
			sent++;
		}

		if (IsOptionGreeksEnabled)
		{
			var greeks = await TryEntitled(
				() => client.GetOptionGreeks(Offset, key.ApiIdentifier, cancellationToken),
				"option Greeks");
			var item = greeks?.FirstOrDefault(value =>
				NasdaqCloudOptionKey.TryParse(value.Identifier, out var itemKey) &&
				itemKey.Code.EqualsIgnoreCase(key.Code));
			if (item != null)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					ServerTime = RequireTime(item.Timestamp, "option Greeks"),
				}
				.TryAdd(Level1Fields.Delta, item.Delta)
				.TryAdd(Level1Fields.Gamma, item.Gamma)
				.TryAdd(Level1Fields.Vega, item.Vega)
				.TryAdd(Level1Fields.Theta, item.Theta)
				.TryAdd(Level1Fields.Rho, item.Rho)
				.TryAdd(Level1Fields.ImpliedVolatility, item.ImpliedVolatility)
				.TryAdd(Level1Fields.TheorPrice, item.TheoreticalPrice), cancellationToken);
				sent++;
			}
		}

		return sent;
	}

	private ValueTask SendSummary(long transactionId, SecurityId securityId,
		NasdaqCloudSummarySnapshot snapshot, string endpoint,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = RequireTime(snapshot.Timestamp, endpoint),
		}
		.TryAdd(Level1Fields.OpenPrice, snapshot.StartOfDayValue)
		.TryAdd(Level1Fields.HighPrice, snapshot.High)
		.TryAdd(Level1Fields.LowPrice, snapshot.Low)
		.TryAdd(Level1Fields.ClosePrice, snapshot.EndOfDayValue)
		.TryAdd(Level1Fields.Change, snapshot.NetChange), cancellationToken);

	private async Task<T> TryEntitled<T>(Func<Task<T>> action, string endpoint)
		where T : class
	{
		try
		{
			return await action();
		}
		catch (NasdaqCloudApiException ex) when (ex.StatusCode is
			HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
		{
			this.AddWarningLog("Nasdaq Cloud {0} endpoint is unavailable: {1}",
				endpoint, ex.Message);
			return null;
		}
	}

	private static string GetCode(SecurityId securityId)
		=> (securityId.Native as string).IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId.SecurityCode));

	private static DateTime RequireTime(string value, string endpoint)
		=> value.ToNasdaqCloudTime() ?? throw new InvalidOperationException(
			$"Nasdaq Cloud {endpoint} response has no timestamp.");

	private static DateTime EstimateFrom(DateTime to, TimeSpan timeFrame, long? count)
	{
		if (count == null)
			return to - (timeFrame < TimeSpan.FromDays(1)
				? TimeSpan.FromDays(1)
				: timeFrame < TimeSpan.FromDays(7)
					? TimeSpan.FromDays(31)
					: TimeSpan.FromDays(366));
		var seconds = Math.Min(count.Value.Max(1) * timeFrame.TotalSeconds * 2,
			TimeSpan.FromDays(36500).TotalSeconds);
		return to - TimeSpan.FromSeconds(seconds);
	}

	private static NasdaqCloudBarRanges SelectRange(TimeSpan timeFrame, TimeSpan age)
	{
		age = age.Max(TimeSpan.Zero);
		if (timeFrame == TimeSpan.FromMinutes(1))
		{
			if (age > TimeSpan.FromDays(1))
				throw new NotSupportedException(
					"Nasdaq Cloud 1-minute bars are available only with the 1-day range.");
			return NasdaqCloudBarRanges.Day;
		}
		if (timeFrame == TimeSpan.FromMinutes(5))
		{
			if (age <= TimeSpan.FromDays(1))
				return NasdaqCloudBarRanges.Day;
			if (age <= TimeSpan.FromDays(5))
				return NasdaqCloudBarRanges.FiveDays;
			throw new NotSupportedException(
				"Nasdaq Cloud 5-minute bars are available for at most five days.");
		}
		if (timeFrame is { TotalMinutes: 10 or 15 or 30 })
		{
			if (age > TimeSpan.FromDays(5))
				throw new NotSupportedException(
					$"Nasdaq Cloud {timeFrame.TotalMinutes}-minute bars are available for at most five days.");
			return NasdaqCloudBarRanges.FiveDays;
		}
		if (timeFrame == TimeSpan.FromDays(1))
		{
			if (age <= TimeSpan.FromDays(31))
				return NasdaqCloudBarRanges.Month;
			if (age <= TimeSpan.FromDays(93))
				return NasdaqCloudBarRanges.ThreeMonths;
			if (age <= TimeSpan.FromDays(186))
				return NasdaqCloudBarRanges.SixMonths;
			if (age <= TimeSpan.FromDays(366))
				return NasdaqCloudBarRanges.Year;
			throw new NotSupportedException(
				"Nasdaq Cloud daily bars are available through the 1-year range endpoint.");
		}
		if (timeFrame == TimeSpan.FromDays(7))
		{
			if (age <= TimeSpan.FromDays(366))
				return NasdaqCloudBarRanges.Year;
			if (age <= TimeSpan.FromDays(366 * 5))
				return NasdaqCloudBarRanges.FiveYears;
			return NasdaqCloudBarRanges.Maximum;
		}
		if (timeFrame == TimeSpan.FromDays(30))
			return age <= TimeSpan.FromDays(366 * 5)
				? NasdaqCloudBarRanges.FiveYears
				: NasdaqCloudBarRanges.Maximum;
		throw new NotSupportedException(
			$"Nasdaq Cloud does not support the {timeFrame} candle precision.");
	}
}
