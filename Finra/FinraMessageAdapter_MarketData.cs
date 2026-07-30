namespace StockSharp.Finra;

public partial class FinraMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);

		var value = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode)
			?.Trim()
			.ToUpperInvariant();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;

		if (left > 0)
		{
			var rows = await LoadSecurityRows(
				value, cancellationToken);

			foreach (var row in rows
				.Where(r => !r.Symbol.IsEmpty())
				.GroupBy(
					r => r.Symbol.Trim(),
					StringComparer.OrdinalIgnoreCase)
				.Select(g => g.First())
				.OrderBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase))
			{
				var security = row.ToSecurityMessage(
					lookupMsg.TransactionId, DataSet);
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				if (skip > 0)
				{
					skip--;
					continue;
				}

				await SendOutMessageAsync(
					security, cancellationToken);
				if (--left <= 0)
					break;
			}
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
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(
				mdMsg.TransactionId, cancellationToken);
			return;
		}

		var symbol = mdMsg.SecurityId.GetSymbol()
			.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();
		if (from > to)
		{
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from,
				"FINRA history start date is after the end date.");
		}

		var observations = await LoadObservations(
			symbol, from, to, cancellationToken);
		IEnumerable<FinraObservation> selected =
			observations.OrderBy(o => o.Time);

		if (mdMsg.Count is long count)
		{
			var take = checked((int)Math.Min(
				count.Max(0), int.MaxValue));
			selected = from is null
				? selected.TakeLast(take)
				: selected.Take(take);
		}
		else if (from is null)
		{
			selected = selected.TakeLast(1);
		}

		var securityId = mdMsg.SecurityId;
		if (securityId.SecurityCode.IsEmpty() ||
			securityId.BoardCode.IsEmpty())
		{
			securityId = symbol.ToFinraSecurityId();
		}

		var sent = 0;

		foreach (var observation in selected)
		{
			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					ServerTime = observation.Time.UtcDateTime,
				}
				.TryAdd(Level1Fields.Volume, observation.Volume)
				.TryAdd(
					Level1Fields.OpenInterest,
					observation.OpenInterest)
				.TryAdd(
					Level1Fields.ShortRatio,
					observation.ShortRatio)
				.TryAdd(Level1Fields.Change, observation.Change)
				.TryAdd(
					Level1Fields.TradesCount,
					observation.TradesCount)
				.TryAdd(
					Level1Fields.Turnover,
					observation.Turnover),
				cancellationToken);
			sent++;
		}

		if (sent == 0)
		{
			this.AddWarningLog(
				"FINRA returned no {0} observations for {1}.",
				DataSet, symbol);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(
			mdMsg.TransactionId, cancellationToken);
	}

	private async Task<FinraSecurityRow[]> LoadSecurityRows(
		string value,
		CancellationToken cancellationToken)
	{
		var query = await CreateQuery(
			value,
			isPrefix: !value.IsEmpty(),
			from: null,
			to: null,
			latestWhenNoRange: true,
			cancellationToken);
		var dataset = DataSet.ToDatasetName(IsDemo);
		var client = SafeClient();

		return DataSet switch
		{
			FinraDataSets.RegShoDaily =>
				(await client.QueryAll<FinraRegShoRecord>(
					dataset,
					query,
					PageSize,
					MaxRecords,
					cancellationToken))
				.Where(r => !r.Symbol.IsEmpty())
				.Select(r => new FinraSecurityRow
				{
					Symbol = r.Symbol.Trim(),
					Name = r.Symbol.Trim(),
					Class = r.MarketCode.IsEmpty("Reg SHO"),
				})
				.ToArray(),

			FinraDataSets.ConsolidatedShortInterest =>
				(await client.QueryAll<FinraShortInterestRecord>(
					dataset,
					query,
					PageSize,
					MaxRecords,
					cancellationToken))
				.Where(r => !r.Symbol.IsEmpty())
				.Select(r => new FinraSecurityRow
				{
					Symbol = r.Symbol.Trim(),
					Name = r.Name,
					Class = r.MarketClassCode
						.IsEmpty("Short interest"),
				})
				.ToArray(),

			FinraDataSets.WeeklySummary =>
				(await client.QueryAll<FinraWeeklySummaryRecord>(
					dataset,
					query,
					PageSize,
					MaxRecords,
					cancellationToken))
				.Where(r => !r.Symbol.IsEmpty())
				.Select(r => new FinraSecurityRow
				{
					Symbol = r.Symbol.Trim(),
					Name = r.Name,
					Class = r.TierDescription
						.IsEmpty(r.TierIdentifier)
						.IsEmpty("Weekly summary"),
				})
				.ToArray(),

			_ => throw new ArgumentOutOfRangeException(
				nameof(DataSet), DataSet, null),
		};
	}

	private async Task<FinraObservation[]> LoadObservations(
		string symbol,
		DateTimeOffset? from,
		DateTimeOffset? to,
		CancellationToken cancellationToken)
	{
		var query = await CreateQuery(
			symbol,
			isPrefix: false,
			from,
			to,
			latestWhenNoRange: true,
			cancellationToken);
		var dataset = DataSet.ToDatasetName(IsDemo);
		var client = SafeClient();

		switch (DataSet)
		{
			case FinraDataSets.RegShoDaily:
			{
				var rows = await client.QueryAll<FinraRegShoRecord>(
					dataset,
					query,
					PageSize,
					MaxRecords,
					cancellationToken);

				return rows
					.Where(r => !r.TradeReportDate.IsEmpty())
					.GroupBy(r => r.TradeReportDate)
					.Select(group =>
					{
						var total = group.Sum(
							r => r.TotalVolume ?? 0);
						var shortVolume = group.Sum(
							r => (r.ShortVolume ?? 0) +
								(r.ShortExemptVolume ?? 0));
						return new FinraObservation
						{
							Time = group.Key.ToFinraTime(),
							Volume = total,
							ShortRatio = total > 0
								? shortVolume / total
								: null,
						};
					})
					.ToArray();
			}

			case FinraDataSets.ConsolidatedShortInterest:
			{
				var rows =
					await client.QueryAll<FinraShortInterestRecord>(
						dataset,
						query,
						PageSize,
						MaxRecords,
						cancellationToken);

				return rows
					.Where(r => !r.SettlementDate.IsEmpty())
					.GroupBy(r => r.SettlementDate)
					.Select(group =>
					{
						var row = group.Last();
						return new FinraObservation
						{
							Time = group.Key.ToFinraTime(),
							Volume = row.AverageDailyVolume,
							OpenInterest =
								row.CurrentShortPosition,
							ShortRatio = row.DaysToCover,
							Change = row.ChangePercent,
						};
					})
					.ToArray();
			}

			case FinraDataSets.WeeklySummary:
			{
				var rows =
					await client.QueryAll<FinraWeeklySummaryRecord>(
						dataset,
						query,
						PageSize,
						MaxRecords,
						cancellationToken);

				return rows
					.Where(r =>
						!r.WeekStartDate
							.IsEmpty(r.SummaryStartDate)
							.IsEmpty())
					.GroupBy(r =>
						r.WeekStartDate
							.IsEmpty(r.SummaryStartDate))
					.Select(group => new FinraObservation
					{
						Time = group.Key.ToFinraTime(),
						Volume = group.Sum(
							r => r.ShareVolume ?? 0),
						TradesCount = group.Sum(
							r => r.TradeCount ?? 0)
							.ToTradeCount(),
						Turnover = group.Sum(
							r => r.Notional ?? 0),
					})
					.ToArray();
			}

			default:
				throw new ArgumentOutOfRangeException(
					nameof(DataSet), DataSet, null);
		}
	}

	private async Task<FinraQueryRequest> CreateQuery(
		string symbol,
		bool isPrefix,
		DateTimeOffset? from,
		DateTimeOffset? to,
		bool latestWhenNoRange,
		CancellationToken cancellationToken)
	{
		var compareFilters = new List<FinraCompareFilter>();
		var dateRangeFilters = new List<FinraDateRangeFilter>();

		if (!symbol.IsEmpty())
		{
			compareFilters.Add(new FinraCompareFilter
			{
				FieldName = DataSet.ToSymbolField(),
				FieldValue = symbol.Trim().ToUpperInvariant(),
				CompareType = isPrefix
					? "BEGINS_WITH"
					: "EQUAL",
			});
		}

		if (DataSet == FinraDataSets.WeeklySummary)
		{
			if (!WeeklyTierIdentifier.IsEmpty())
			{
				compareFilters.Add(new FinraCompareFilter
				{
					FieldName = "tierIdentifier",
					FieldValue = WeeklyTierIdentifier
						.Trim()
						.ToUpperInvariant(),
					CompareType = "EQUAL",
				});
			}
			if (!WeeklySummaryTypeCode.IsEmpty())
			{
				compareFilters.Add(new FinraCompareFilter
				{
					FieldName = "summaryTypeCode",
					FieldValue = WeeklySummaryTypeCode
						.Trim()
						.ToUpperInvariant(),
					CompareType = "EQUAL",
				});
			}
		}

		var dateField = DataSet.ToDateField();
		if (from is null && to is null && latestWhenNoRange)
		{
			var latest = await GetLatestPartitionDate(
				cancellationToken);
			if (!latest.IsEmpty())
			{
				compareFilters.Add(new FinraCompareFilter
				{
					FieldName = dateField,
					FieldValue = latest,
					CompareType = "EQUAL",
				});
			}
		}
		else
		{
			var start = (from ?? new DateTimeOffset(
				1900, 1, 1, 0, 0, 0, TimeSpan.Zero))
				.UtcDateTime
				.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			var end = (to ?? DateTimeOffset.UtcNow)
				.UtcDateTime
				.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			dateRangeFilters.Add(new FinraDateRangeFilter
			{
				FieldName = dateField,
				StartDate = start,
				EndDate = end,
			});
		}

		return new FinraQueryRequest
		{
			Fields = DataSet.ToFields(),
			CompareFilters =
				compareFilters.Count == 0 ? null : compareFilters,
			DateRangeFilters =
				dateRangeFilters.Count == 0 ? null : dateRangeFilters,
		};
	}

	private async Task<string> GetLatestPartitionDate(
		CancellationToken cancellationToken)
	{
		var partitions = await SafeClient().GetPartitions(
			DataSet.ToDatasetName(IsDemo),
			cancellationToken);
		var rows = partitions.AvailablePartitions ?? [];

		if (DataSet == FinraDataSets.WeeklySummary &&
			!WeeklyTierIdentifier.IsEmpty())
		{
			rows = rows
				.Where(p =>
					p.Values is { Length: > 1 } &&
					p.Values[1].EqualsIgnoreCase(
						WeeklyTierIdentifier.Trim()))
				.ToArray();
		}

		return rows
			.Where(p =>
				p.Values is { Length: > 0 } &&
				!p.Values[0].IsEmpty())
			.Select(p => p.Values[0])
			.OrderByDescending(
				value => value,
				StringComparer.Ordinal)
			.FirstOrDefault();
	}
}
