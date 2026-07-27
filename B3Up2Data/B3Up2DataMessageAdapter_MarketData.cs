namespace StockSharp.B3Up2Data;

public partial class B3Up2DataMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask MarketDataAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (B3Up2DataDataTypes.TryGetKind(
            mdMsg.DataType2, out var kind))
        {
            await OnFileSubscriptionAsync(
                mdMsg, kind, cancellationToken);
            return;
        }
        await base.MarketDataAsync(mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);
        if (lookupMsg.Skip is < 0)
            throw new ArgumentOutOfRangeException(nameof(lookupMsg.Skip));
        if (lookupMsg.Count is <= 0)
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        RequireCsv();
        var types = lookupMsg.GetSecurityTypes();
        if (types.Count > 0 &&
            !types.Any(type =>
                type is SecurityTypes.Stock or
                    SecurityTypes.Etf or
                    SecurityTypes.Fund or
                    SecurityTypes.Index))
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var includeEquities = types.Count == 0 ||
            types.Any(type =>
                type is SecurityTypes.Stock or
                    SecurityTypes.Etf or
                    SecurityTypes.Fund);
        var includeIndexes = types.Count == 0 ||
            types.Contains(SecurityTypes.Index);
        var skip = lookupMsg.Skip ?? 0;
        var remaining = lookupMsg.Count ?? long.MaxValue;
        var seen = new HashSet<SecurityId>();
        var asOf = DateTime.UtcNow.ToUtcDate();

        if (includeEquities && remaining > 0)
        {
            var table = await TryDownloadLatestCsv(
                B3Up2DataDataKinds.SecurityMaster,
                asOf,
                cancellationToken);
            if (table is not null)
            {
                RequireColumns(
                    table,
                    B3Up2DataDataKinds.SecurityMaster,
                    "TckrSymb",
                    "SctyCtgyNm");
                foreach (var row in table.Rows)
                {
                    if (remaining <= 0)
                        break;
                    if (row.Get("TckrSymb").IsEmpty())
                        continue;
                    SecurityMessage security;
                    try
                    {
                        security = row.ToSecurityMessage(
                            lookupMsg.TransactionId);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                    if (!seen.Add(security.SecurityId) ||
                        !security.IsMatch(lookupMsg, types))
                    {
                        continue;
                    }
                    if (skip > 0)
                    {
                        skip--;
                        continue;
                    }
                    await SendOutMessageAsync(
                        security, cancellationToken);
                    remaining--;
                }
            }
        }

        if (includeIndexes && remaining > 0)
        {
            var table = await TryDownloadLatestCsv(
                B3Up2DataDataKinds.IndexEod,
                asOf,
                cancellationToken);
            if (table is not null)
            {
                RequireColumns(
                    table,
                    B3Up2DataDataKinds.IndexEod,
                    "TckrSymb");
                foreach (var row in table.Rows)
                {
                    if (remaining <= 0)
                        break;
                    if (row.Get("TckrSymb").IsEmpty())
                        continue;
                    SecurityMessage security;
                    try
                    {
                        security = row.ToIndexSecurityMessage(
                            lookupMsg.TransactionId);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                    if (!seen.Add(security.SecurityId) ||
                        !security.IsMatch(lookupMsg, types))
                    {
                        continue;
                    }
                    if (skip > 0)
                    {
                        skip--;
                        continue;
                    }
                    await SendOutMessageAsync(
                        security, cancellationToken);
                    remaining--;
                }
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
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.From is not null)
        {
            throw new NotSupportedException(
                "B3 UP2DATA exposes EOD Level1 snapshots; use To " +
                "to select the latest snapshot date.");
        }

        RequireCsv();
        var ticker = mdMsg.SecurityId.GetTicker();
        var index = mdMsg.SecurityId.IsIndex();
        var kind = index
            ? B3Up2DataDataKinds.IndexEod
            : B3Up2DataDataKinds.EquitiesEod;
        var result = await DownloadLatest(
            kind,
            (mdMsg.To ?? DateTime.UtcNow).ToUtcDate(),
            cancellationToken);
        var table = B3CsvTable.Parse(result.Download.Content);
        RequireColumns(table, kind, "RptDt", "TckrSymb", "LastPric");
        var row = table.Rows.FirstOrDefault(candidate =>
            candidate.Get("TckrSymb")
                .EqualsIgnoreCase(ticker));
        if (row is not null)
        {
            var time = row.GetDate("RptDt") ?? result.Date;
            var last = Positive(row.GetDecimal("LastPric"));
            var change = row.GetDecimal("OscnPctg");
            if (index && change is not null)
                change *= 100;
            var message = new Level1ChangeMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId.Normalize(
                    ticker, index),
                ServerTime = time,
            }
            .TryAdd(Level1Fields.LastTradePrice, last)
            .TryAdd(
                Level1Fields.LastTradeTime,
                last is null ? null : (DateTime?)time)
            .TryAdd(
                Level1Fields.OpenPrice,
                Positive(row.GetDecimal("FrstPric")))
            .TryAdd(
                Level1Fields.HighPrice,
                Positive(row.GetDecimal("MaxPric")))
            .TryAdd(
                Level1Fields.LowPrice,
                Positive(row.GetDecimal("MinPric")))
            .TryAdd(Level1Fields.ClosePrice, last)
            .TryAdd(Level1Fields.Change, change)
            .TryAdd(
                Level1Fields.BestBidPrice,
                Positive(row.GetDecimal("BestBidPric")))
            .TryAdd(
                Level1Fields.BestAskPrice,
                Positive(row.GetDecimal("BestAskPric")))
            .TryAdd(
                Level1Fields.Volume,
                NonNegative(row.GetDecimal("FinInstrmQty")));
            if (message.Changes.Count > 0)
            {
                await SendOutMessageAsync(
                    message, cancellationToken);
            }
        }

        await CompleteSubscription(mdMsg, cancellationToken);
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
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(
                mdMsg, cancellationToken);
            return;
        }
        var timeFrame = mdMsg.GetTimeFrame();
        if (timeFrame != TimeSpan.FromDays(1))
        {
            throw new NotSupportedException(
                $"B3 UP2DATA does not support {timeFrame} candles.");
        }

        RequireCsv();
        var ticker = mdMsg.SecurityId.GetTicker();
        var index = mdMsg.SecurityId.IsIndex();
        var kind = index
            ? B3Up2DataDataKinds.IndexEod
            : B3Up2DataDataKinds.EquitiesEod;
        var to = (mdMsg.To ?? DateTime.UtcNow).ToUtcDate();
        var from = (mdMsg.From ??
            to.AddDays(-(LookbackDays - 1))).ToUtcDate();
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                from,
                "The candle-history start date is after its end date.");
        }
        var earliest = to.AddDays(-(LookbackDays - 1));
        if (from < earliest)
            from = earliest;

        var remaining = mdMsg.Count ?? long.MaxValue;
        for (var date = from;
            date <= to && remaining > 0;
            date = date.AddDays(1))
        {
            var blobs = await ListDataset(
                kind, date, 1, cancellationToken);
            if (blobs.Length == 0)
                continue;
            var download = await SafeClient().DownloadBlob(
                blobs[0].Name, cancellationToken);
            var table = B3CsvTable.Parse(download.Content);
            RequireColumns(
                table, kind, "RptDt", "TckrSymb",
                "FrstPric", "MinPric", "MaxPric", "LastPric");
            var row = table.Rows.FirstOrDefault(candidate =>
                candidate.Get("TckrSymb")
                    .EqualsIgnoreCase(ticker));
            if (row is null)
                continue;

            var open = Positive(row.GetDecimal("FrstPric"));
            var high = Positive(row.GetDecimal("MaxPric"));
            var low = Positive(row.GetDecimal("MinPric"));
            var close = Positive(row.GetDecimal("LastPric"));
            if (open is null ||
                high is null ||
                low is null ||
                close is null ||
                high < Math.Max(open.Value, close.Value) ||
                low > Math.Min(open.Value, close.Value))
            {
                continue;
            }
            var reportDate = row.GetDate("RptDt") ?? date;
            await SendOutMessageAsync(
                new TimeFrameCandleMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    SecurityId = mdMsg.SecurityId.Normalize(
                        ticker, index),
                    DataType = mdMsg.DataType2,
                    TypedArg = timeFrame,
                    OpenTime = reportDate,
                    CloseTime = reportDate.AddDays(1),
                    OpenPrice = open.Value,
                    HighPrice = high.Value,
                    LowPrice = low.Value,
                    ClosePrice = close.Value,
                    TotalVolume = NonNegative(
                        row.GetDecimal("FinInstrmQty")) ?? 0,
                    State = CandleStates.Finished,
                },
                cancellationToken);
            remaining--;
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async ValueTask OnFileSubscriptionAsync(
        MarketDataMessage mdMsg,
        B3Up2DataDataKinds kind,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.From > mdMsg.To)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mdMsg.From),
                mdMsg.From,
                "The raw-file start date is after its end date.");
        }

        var target = checked((int)Math.Min(
            mdMsg.Count ?? MaxRawFiles,
            MaxRawFiles));
        if (kind == B3Up2DataDataKinds.BlobCatalog)
        {
            var entries = await ListAll(
                SafeBlobPrefix(), target, cancellationToken);
            foreach (var entry in entries)
            {
                await SendOutMessageAsync(
                    CreateFileMessage(
                        mdMsg.TransactionId,
                        kind,
                        entry,
                        null,
                        entry.LastModified ?? DateTime.UtcNow),
                    cancellationToken);
            }
            await CompleteSubscription(mdMsg, cancellationToken);
            return;
        }

        var ticker = mdMsg.SecurityId.GetOptionalTicker();
        var emitted = 0;
        if (mdMsg.From is null && mdMsg.To is null)
        {
            var asOf = DateTime.UtcNow.ToUtcDate();
            for (var offset = 0;
                offset < LookbackDays && emitted < target;
                offset++)
            {
                var date = asOf.AddDays(-offset);
                var blobs = await ListDataset(
                    kind, date, target - emitted, cancellationToken);
                blobs = FilterDatasetByTicker(
                    kind, ticker, blobs);
                if (blobs.Length == 0)
                    continue;
                emitted += await EmitFiles(
                    mdMsg,
                    kind,
                    date,
                    blobs.Take(target - emitted),
                    cancellationToken);
                break;
            }
        }
        else
        {
            var to = (mdMsg.To ??
                DateTime.UtcNow).ToUtcDate();
            var from = (mdMsg.From ?? to).ToUtcDate();
            var earliest = to.AddDays(-(LookbackDays - 1));
            if (from < earliest)
                from = earliest;
            for (var date = from;
                date <= to && emitted < target;
                date = date.AddDays(1))
            {
                var blobs = await ListDataset(
                    kind, date, target - emitted, cancellationToken);
                blobs = FilterDatasetByTicker(
                    kind, ticker, blobs);
                emitted += await EmitFiles(
                    mdMsg,
                    kind,
                    date,
                    blobs.Take(target - emitted),
                    cancellationToken);
            }
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task<int> EmitFiles(
        MarketDataMessage mdMsg,
        B3Up2DataDataKinds kind,
        DateTime date,
        IEnumerable<B3BlobItem> blobs,
        CancellationToken cancellationToken)
    {
        var emitted = 0;
        foreach (var blob in blobs)
        {
            var download = await SafeClient().DownloadBlob(
                blob.Name, cancellationToken);
            await SendOutMessageAsync(
                CreateFileMessage(
                    mdMsg.TransactionId,
                    kind,
                    blob,
                    download,
                    download.LastModified ?? date),
                cancellationToken);
            emitted++;
        }
        return emitted;
    }

    private B3Up2DataFileMessage CreateFileMessage(
        long originalTransactionId,
        B3Up2DataDataKinds kind,
        B3BlobItem blob,
        B3DownloadedBlob download,
        DateTime serverTime)
        => new()
        {
            OriginalTransactionId = originalTransactionId,
            Dataset = kind,
            ServerTime = serverTime,
            Channel = SafeChannel(),
            BlobName = blob.Name,
            ContentType = (download?.ContentType)
                .IsEmpty(blob.ContentType),
            ContentLength = download?.Content.LongLength ??
                blob.ContentLength,
            ETag = (download?.ETag).IsEmpty(blob.ETag),
            Payload = download?.DecodeText(),
        };

    private static B3BlobItem[] FilterDatasetByTicker(
        B3Up2DataDataKinds kind,
        string ticker,
        B3BlobItem[] blobs)
    {
        if (ticker.IsEmpty() ||
            kind is not (
                B3Up2DataDataKinds.IndexIntraday or
                B3Up2DataDataKinds.IndexComposition))
        {
            return blobs;
        }
        var marker = $"_{ticker}_";
        return blobs
            .Where(blob =>
                Path.GetFileName(blob.Name)
                    .Contains(
                        marker,
                        StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private async Task<B3CsvTable> TryDownloadLatestCsv(
        B3Up2DataDataKinds kind,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        var found = await FindLatest(
            kind, asOf, cancellationToken);
        if (found is null)
            return null;
        var download = await SafeClient().DownloadBlob(
            found.Value.Blob.Name, cancellationToken);
        return B3CsvTable.Parse(download.Content);
    }

    private static void RequireColumns(
        B3CsvTable table,
        B3Up2DataDataKinds kind,
        params string[] names)
    {
        var missing = names
            .Where(name => !table.HasColumn(name))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"B3 UP2DATA {kind} CSV is missing columns: " +
                missing.JoinComma() + ".");
        }
    }

    private async Task CompleteSubscription(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(
            mdMsg, cancellationToken);
        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId, cancellationToken);
    }

    private static decimal? Positive(decimal? value)
        => value is > 0 ? value : null;

    private static decimal? NonNegative(decimal? value)
        => value is >= 0 ? value : null;
}
