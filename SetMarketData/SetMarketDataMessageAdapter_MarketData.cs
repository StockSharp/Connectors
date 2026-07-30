namespace StockSharp.SetMarketData;

public partial class SetMarketDataMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);

        var value = lookupMsg.SecurityId.GetSetSymbol();
        var stockTask = SafeClient().GetStocks(
            DataMode,
            new SetStockQuery(
                Markets,
                IndexSectors,
                SecurityTypeCodes,
                value,
                IncludeOddLots),
            cancellationToken);
        var indexTask = IncludeIndices
            ? SafeClient().GetIndices(
                DataMode,
                new SetIndexQuery(
                    Markets,
                    value.IsEmpty(IndexSectors)),
                cancellationToken)
            : Task.FromResult(Array.Empty<SetIndexQuote>());
        await Task.WhenAll(stockTask, indexTask);

        var types = lookupMsg.GetSecurityTypes();
        var securities = (await stockTask)
            .Where(quote =>
                quote is not null &&
                !quote.Symbol.IsEmpty() &&
                quote.Matches(value))
            .GroupBy(
                quote => quote.Symbol,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(quote => quote.ToSecurityMessage(
                lookupMsg.TransactionId))
            .Concat(
                (await indexTask)
                    .Where(quote =>
                        quote is not null &&
                        !quote.Symbol.IsEmpty() &&
                        quote.Matches(value))
                    .GroupBy(
                        quote => quote.Symbol,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Select(quote => quote.ToSecurityMessage(
                        lookupMsg.TransactionId)))
            .Where(security =>
                security.IsMatch(lookupMsg, types))
            .OrderBy(
                security => security.SecurityId.BoardCode)
            .ThenBy(
                security => security.SecurityId.SecurityCode,
                StringComparer.OrdinalIgnoreCase);

        var skip = lookupMsg.Skip ?? 0;
        var left = lookupMsg.Count ?? long.MaxValue;

        foreach (var security in securities)
        {
            if (left <= 0)
                break;
            if (skip > 0)
            {
                skip--;
                continue;
            }

            await SendOutMessageAsync(
                security, cancellationToken);
            left--;
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

        var symbol = mdMsg.SecurityId.GetSetSymbol()
            .ThrowIfEmpty(nameof(mdMsg.SecurityId));
        if (mdMsg.SecurityId.IsIndex())
        {
            var quote = (await SafeClient().GetIndices(
                DataMode,
                new SetIndexQuery(null, symbol),
                cancellationToken))
                .FirstOrDefault(item =>
                    item is not null &&
                    item.Symbol.EqualsIgnoreCase(symbol));
            if (quote is null)
            {
                throw new InvalidOperationException(
                    $"SET index '{symbol}' was not found.");
            }

            await SendOutMessageAsync(
                quote.ToLevel1(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    DataMode),
                cancellationToken);
        }
        else
        {
            var quote = await GetStockQuote(
                symbol, cancellationToken);
            await SendOutMessageAsync(
                quote.ToLevel1(
                    mdMsg.TransactionId,
                    mdMsg.SecurityId,
                    DataMode),
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnMarketDepthSubscriptionAsync(
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
        if (mdMsg.SecurityId.IsIndex())
        {
            throw new NotSupportedException(
                "SET index quotations do not contain an order book.");
        }

        var symbol = mdMsg.SecurityId.GetSetSymbol()
            .ThrowIfEmpty(nameof(mdMsg.SecurityId));
        var quote = await GetStockQuote(
            symbol, cancellationToken);
        var depth = (mdMsg.MaxDepth ?? 5).Max(1).Min(5);
        var bids = (quote.Bids ?? [])
            .Where(level =>
                level.Price is > 0 &&
                level.Volume is > 0)
            .OrderBy(level => level.Rank)
            .Take(depth)
            .Select(level => new QuoteChange(
                level.Price.Value,
                level.Volume.Value))
            .ToArray();
        var asks = (quote.Offers ?? [])
            .Where(level =>
                level.Price is > 0 &&
                level.Volume is > 0)
            .OrderBy(level => level.Rank)
            .Take(depth)
            .Select(level => new QuoteChange(
                level.Price.Value,
                level.Volume.Value))
            .ToArray();

        await SendOutMessageAsync(
            new QuoteChangeMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                SecurityId = mdMsg.SecurityId,
                ServerTime = quote.Time.GetServerTime(),
                Bids = bids,
                Asks = asks,
                State = QuoteChangeStates.SnapshotComplete,
            },
            cancellationToken);
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async Task<SetStockQuote> GetStockQuote(
        string symbol,
        CancellationToken cancellationToken)
    {
        var quotes = await SafeClient().GetStocks(
            DataMode,
            new SetStockQuery(
                null,
                null,
                null,
                symbol,
                IncludeOddLots),
            cancellationToken);
        return quotes.FirstOrDefault(quote =>
                quote is not null &&
                quote.Symbol.EqualsIgnoreCase(symbol)) ??
            throw new InvalidOperationException(
                $"SET security '{symbol}' was not found.");
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
}

static class SetMarketDataMessageExtensions
{
    public static Level1ChangeMessage ToLevel1(
        this SetStockQuote quote,
        long originalTransactionId,
        SecurityId securityId,
        SetMarketDataModes mode)
    {
        var bestBid = (quote.Bids ?? [])
            .Where(level =>
                level.Price is > 0 &&
                level.Volume is > 0)
            .OrderBy(level => level.Rank)
            .FirstOrDefault();
        var bestAsk = (quote.Offers ?? [])
            .Where(level =>
                level.Price is > 0 &&
                level.Volume is > 0)
            .OrderBy(level => level.Rank)
            .FirstOrDefault();

        return new Level1ChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = securityId,
            ServerTime = quote.Time.GetServerTime(),
        }
        .TryAdd(Level1Fields.ClosePrice, quote.Prior)
        .TryAdd(Level1Fields.OpenPrice, quote.Open)
        .TryAdd(Level1Fields.HighPrice, quote.High)
        .TryAdd(Level1Fields.LowPrice, quote.Low)
        .TryAdd(Level1Fields.LastTradePrice, quote.Last)
        .TryAdd(Level1Fields.AveragePrice, quote.Average)
        .TryAdd(Level1Fields.Volume, quote.TotalVolume)
        .TryAdd(Level1Fields.Turnover, quote.TotalValue)
        .TryAdd(Level1Fields.BestBidPrice, bestBid?.Price)
        .TryAdd(Level1Fields.BestBidVolume, bestBid?.Volume)
        .TryAdd(Level1Fields.BestAskPrice, bestAsk?.Price)
        .TryAdd(Level1Fields.BestAskVolume, bestAsk?.Volume)
        .TryAdd(
            Level1Fields.IsSystem,
            mode == SetMarketDataModes.RealTime);
    }

    public static Level1ChangeMessage ToLevel1(
        this SetIndexQuote quote,
        long originalTransactionId,
        SecurityId securityId,
        SetMarketDataModes mode)
        => new Level1ChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = securityId,
            ServerTime = quote.Time.GetServerTime(),
        }
        .TryAdd(Level1Fields.ClosePrice, quote.Prior)
        .TryAdd(Level1Fields.OpenPrice, quote.Open)
        .TryAdd(Level1Fields.HighPrice, quote.High)
        .TryAdd(Level1Fields.LowPrice, quote.Low)
        .TryAdd(Level1Fields.LastTradePrice, quote.Last)
        .TryAdd(Level1Fields.Volume, quote.TotalVolume)
        .TryAdd(Level1Fields.Turnover, quote.TotalValue)
        .TryAdd(
            Level1Fields.IsSystem,
            mode == SetMarketDataModes.RealTime);
}
