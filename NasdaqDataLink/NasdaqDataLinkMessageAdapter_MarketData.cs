namespace StockSharp.NasdaqDataLink;

public partial class NasdaqDataLinkMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var value = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;

		if (value.TryParseDataLinkCode(out var databaseCode, out var datasetCode))
		{
			try
			{
				var dataset = await SafeClient().GetMetadata(
					databaseCode, datasetCode, cancellationToken);
				var security = dataset.ToSecurityMessage(
					lookupMsg.TransactionId, SecurityType, Currency);
				if (security.IsMatch(lookupMsg, securityTypes) && skip <= 0 && left > 0)
					await SendOutMessageAsync(security, cancellationToken);
			}
			catch (NasdaqDataLinkApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
			}
		}
		else
		{
			var page = 1;
			while (left > 0)
			{
				var response = await SafeClient().Search(new NasdaqDataLinkSearchQuery
				{
					Query = value,
					DatabaseCode = DatabaseCode,
					Page = page,
				}, cancellationToken);
				var datasets = response?.Datasets ?? [];
				foreach (var dataset in datasets.WhereNotNull())
				{
					if (!dataset.Matches(value) ||
						(!DatabaseCode.IsEmpty() &&
							!dataset.DatabaseCode.EqualsIgnoreCase(DatabaseCode)))
					{
						continue;
					}

					SecurityMessage security;
					try
					{
						security = dataset.ToSecurityMessage(
							lookupMsg.TransactionId, SecurityType, Currency);
					}
					catch (InvalidOperationException)
					{
						continue;
					}
					if (!security.IsMatch(lookupMsg, securityTypes))
						continue;
					if (skip > 0)
					{
						skip--;
						continue;
					}
					await SendOutMessageAsync(security, cancellationToken);
					if (--left <= 0)
						break;
				}

				var totalPages = response?.Meta?.TotalPages ?? page;
				if (page >= totalPages.Max(1) || datasets.Length == 0)
					break;
				page++;
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

		var result = await GetObservations(mdMsg, cancellationToken);
		var sent = 0;
		foreach (var row in result.Rows)
		{
			var open = result.Columns.Get(row, result.Columns.Open);
			var high = result.Columns.Get(row, result.Columns.High);
			var low = result.Columns.Get(row, result.Columns.Low);
			var close = result.Columns.Close >= 0
				? result.Columns.Get(row, result.Columns.Close)
				: result.Columns.Get(row, result.Columns.Value);
			var volume = result.Columns.Get(row, result.Columns.Volume);
			var openInterest = result.Columns.Get(row, result.Columns.OpenInterest);
			if (open == null && high == null && low == null && close == null &&
				volume == null && openInterest == null)
			{
				continue;
			}

			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = result.SecurityId,
				ServerTime = row.Date,
			}
			.TryAdd(Level1Fields.OpenPrice, open)
			.TryAdd(Level1Fields.HighPrice, high)
			.TryAdd(Level1Fields.LowPrice, low)
			.TryAdd(Level1Fields.ClosePrice, close)
			.TryAdd(Level1Fields.Volume, volume)
			.TryAdd(Level1Fields.OpenInterest, openInterest), cancellationToken);
			sent++;
		}

		if (sent == 0)
			this.AddWarningLog("Nasdaq Data Link returned no numeric observations for {0}.", result.Code);
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

		var timeFrame = mdMsg.GetTimeFrame();
		if (timeFrame != TimeSpan.FromDays(1))
			throw new NotSupportedException(
				"Nasdaq Data Link candles are available only at the native daily frequency.");

		var result = await GetObservations(mdMsg, cancellationToken);
		if (result.Frequency != NasdaqDataLinkFrequencies.Daily)
			throw new NotSupportedException(
				$"Nasdaq Data Link series '{result.Code}' has native {result.Frequency} frequency, not daily.");
		if (!result.Columns.HasOhlc)
			throw new NotSupportedException(
				$"Nasdaq Data Link series '{result.Code}' does not expose native OHLC columns.");

		var sent = 0;
		foreach (var row in result.Rows)
		{
			var open = result.Columns.Get(row, result.Columns.Open);
			var high = result.Columns.Get(row, result.Columns.High);
			var low = result.Columns.Get(row, result.Columns.Low);
			var close = result.Columns.Get(row, result.Columns.Close);
			if (open == null || high == null || low == null || close == null)
				continue;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = result.SecurityId,
				DataType = mdMsg.DataType2,
				TypedArg = timeFrame,
				OpenTime = row.Date,
				OpenPrice = open.Value,
				HighPrice = high.Value,
				LowPrice = low.Value,
				ClosePrice = close.Value,
				TotalVolume = result.Columns.Get(row, result.Columns.Volume) ?? 0,
				OpenInterest = result.Columns.Get(row, result.Columns.OpenInterest),
				State = CandleStates.Finished,
			}, cancellationToken);
			sent++;
		}

		if (sent == 0)
			this.AddWarningLog("Nasdaq Data Link returned no complete OHLC rows for {0}.", result.Code);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<NasdaqDataLinkObservationResult> GetObservations(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var code = (mdMsg.SecurityId.Native as string)
			.IsEmpty(mdMsg.SecurityId.SecurityCode)
			.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		if (!code.TryParseDataLinkCode(out var databaseCode, out var datasetCode))
			throw new ArgumentException(
				$"Nasdaq Data Link security code '{code}' must have DATABASE/DATASET format.",
				nameof(mdMsg));

		var from = mdMsg.From?.ToUniversalTime();
		var to = mdMsg.To?.ToUniversalTime();
		if (from > to)
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from, "The start date is after the end date.");

		int? limit = mdMsg.Count is long count
			? checked((int)Math.Min(count.Max(0), int.MaxValue))
			: from == null ? 1 : null;
		var query = new NasdaqDataLinkDataQuery
		{
			StartDate = from,
			EndDate = to,
			Limit = limit,
			Order = from == null
				? NasdaqDataLinkOrders.Descending
				: NasdaqDataLinkOrders.Ascending,
		};

		var client = SafeClient();
		var metadata = await client.GetMetadata(databaseCode, datasetCode, cancellationToken);
		var data = await client.GetData(databaseCode, datasetCode, query, cancellationToken);
		var columnNames = data.ColumnNames?.Length > 0
			? data.ColumnNames
			: metadata.ColumnNames;
		var columns = new NasdaqDataLinkColumnMap(columnNames,
			ValueColumn, OpenColumn, HighColumn, LowColumn, CloseColumn,
			VolumeColumn, OpenInterestColumn);
		var rows = data.Data ?? [];
		foreach (var row in rows)
			columns.Validate(row);

		IEnumerable<NasdaqDataLinkRow> selected = rows.OrderBy(row => row.Date);
		if (mdMsg.Count is long requested)
		{
			var take = checked((int)Math.Min(requested.Max(0), int.MaxValue));
			selected = from == null ? selected.TakeLast(take) : selected.Take(take);
		}
		else if (from == null)
			selected = selected.TakeLast(1);

		return new NasdaqDataLinkObservationResult
		{
			Code = code,
			SecurityId = mdMsg.SecurityId.NormalizeDataLink(code),
			Frequency = data.Frequency == NasdaqDataLinkFrequencies.Unknown
				? metadata.Frequency
				: data.Frequency,
			Columns = columns,
			Rows = selected.ToArray(),
		};
	}
}

sealed class NasdaqDataLinkObservationResult
{
	public string Code { get; set; }
	public SecurityId SecurityId { get; set; }
	public NasdaqDataLinkFrequencies Frequency { get; set; }
	public NasdaqDataLinkColumnMap Columns { get; set; }
	public NasdaqDataLinkRow[] Rows { get; set; }
}
