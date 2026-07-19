namespace StockSharp.Coinhako;

public partial class CoinhakoMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
            cancellationToken);
        EnsureConnected();
        var securityTypes = lookupMsg.GetSecurityTypes();
        var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
            ? null
            : lookupMsg.SecurityId.SecurityCode.ToCoinhakoSymbolKey();
        var skip = Math.Max(0, lookupMsg.Skip ?? 0);
        var left = lookupMsg.Count ?? long.MaxValue;

        foreach (var market in GetMarkets().OrderBy(static market =>
            market.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
                !lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
                    BoardCodes.Coinhako))
                continue;
            if (!requestedSymbol.IsEmpty() &&
                !requestedSymbol.EqualsIgnoreCase(
                    market.Symbol.ToCoinhakoSymbolKey()))
                continue;
            var security = CreateSecurity(market, lookupMsg.TransactionId);
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
                _level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.From is not null)
            throw new NotSupportedException(
                "Coinhako Public API does not expose historical Level1 events.");

        var market = GetMarket(mdMsg.SecurityId);
        var prices = await RestClient.GetSpotsAsync(market.BaseCurrency,
            market.CounterCurrency, cancellationToken);
        var price = (prices ?? []).FirstOrDefault(value =>
            value?.Symbol.IsEmpty() == false &&
            value.Symbol.ToCoinhakoSymbolKey().EqualsIgnoreCase(
                market.Symbol.ToCoinhakoSymbolKey())) ??
            throw new InvalidDataException(
                $"Coinhako returned no price for {market.Symbol}.");
        UpdateMarkets([price]);
        await SendSpotAsync(market.Symbol, price, mdMsg.TransactionId,
            cancellationToken);
        if (mdMsg.IsHistoryOnly())
        {
            await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
            return;
        }

        using (_sync.EnterScope())
            _level1Subscriptions.Add(mdMsg.TransactionId, new()
            {
                Symbol = market.Symbol,
            });
        await SendSubscriptionResultAsync(mdMsg, cancellationToken);
    }

    private static SecurityMessage CreateSecurity(MarketDefinition market,
        long originalTransactionId)
        => new()
        {
            SecurityId = market.Symbol.ToStockSharp(),
            Name = $"{market.BaseCurrency}/{market.CounterCurrency}",
            ShortName = $"{market.BaseCurrency}/{market.CounterCurrency}",
            SecurityType = SecurityTypes.CryptoCurrency,
            Currency = market.CounterCurrency.ToCurrency(),
            OriginalTransactionId = originalTransactionId,
        };

    private async ValueTask RefreshLevel1Async(
        CancellationToken cancellationToken)
    {
        KeyValuePair<long, Level1Subscription>[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _level1Subscriptions];
        if (subscriptions.Length == 0)
            return;

        var counters = subscriptions.Select(pair =>
            GetMarket(pair.Value.Symbol).CounterCurrency)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var counter in counters)
        {
            var prices = await RestClient.GetSpotsAsync(null, counter,
                cancellationToken) ?? [];
            UpdateMarkets(prices);
        }

        foreach (var pair in subscriptions)
        {
            var market = GetMarket(pair.Value.Symbol);
            if (market.Spot is not null)
                await SendSpotAsync(market.Symbol, market.Spot, pair.Key,
                    cancellationToken);
        }
    }

    private ValueTask SendSpotAsync(string symbol, CoinhakoSpotPrice price,
        long originalTransactionId, CancellationToken cancellationToken)
    {
        if (price is null)
            return default;
        return SendOutMessageAsync(new Level1ChangeMessage
        {
            SecurityId = symbol.ToStockSharp(),
            ServerTime = CurrentTime,
            OriginalTransactionId = originalTransactionId,
        }
        .TryAdd(Level1Fields.BestBidPrice,
            price.SellPrice > 0 ? price.SellPrice : null)
        .TryAdd(Level1Fields.BestAskPrice,
            price.BuyPrice > 0 ? price.BuyPrice : null), cancellationToken);
    }

    private async ValueTask CompleteMarketSubscriptionAsync(
        MarketDataMessage message, CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(message, cancellationToken);
        await SendSubscriptionFinishedAsync(message.TransactionId,
            cancellationToken);
    }
}
