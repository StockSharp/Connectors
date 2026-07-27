namespace StockSharp.Exante;

public partial class ExanteMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);

        var securityTypes = message.GetSecurityTypes();
        var query = message.SecurityId.SecurityCode;
        var board = message.SecurityId.BoardCode;
        var native = message.SecurityId.Native as string;
        var left = message.Count ?? long.MaxValue;
        var emitted = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (!native.IsEmpty() ||
            (!query.IsEmpty() &&
             (query.Contains('.') || !board.IsEmpty())))
        {
            native = native.IsEmpty(query.Contains('.')
                ? query : $"{query}.{board}");
            var symbol = await Rest.GetSymbol(native, cancellationToken);
            left = await SendSymbol(symbol, message, securityTypes,
                left, emitted, cancellationToken);
        }
        else
        {
            var exchanges = await Rest.GetExchanges(cancellationToken);
            if (!board.IsEmpty())
            {
                exchanges = exchanges.Where(exchange =>
                    exchange.Id.EqualsIgnoreCase(board) ||
                    exchange.Name.EqualsIgnoreCase(board)).ToArray();
            }

            foreach (var exchange in exchanges)
            {
                if (left <= 0)
                    break;

                var symbols = await Rest.GetSymbolsByExchange(
                    exchange.Id, cancellationToken);
                foreach (var symbol in symbols)
                {
                    if (!query.IsEmpty() &&
                        !symbol.Ticker.EqualsIgnoreCase(query) &&
                        !symbol.SymbolId.EqualsIgnoreCase(query) &&
                        !(symbol.Name?.Contains(query,
                            StringComparison.OrdinalIgnoreCase) ?? false))
                        continue;

                    left = await SendSymbol(symbol, message,
                        securityTypes, left, emitted,
                        cancellationToken);
                    if (left <= 0)
                        break;
                }
            }
        }

        await SendSubscriptionResultAsync(message, cancellationToken);
    }

    private async ValueTask<long> SendSymbol(ExanteSymbol symbol,
        SecurityLookupMessage message,
        HashSet<SecurityTypes> securityTypes, long left,
        HashSet<string> emitted,
        CancellationToken cancellationToken)
    {
        if (symbol?.SymbolId.IsEmpty() != false ||
            !emitted.Add(symbol.SymbolId))
            return left;

        CacheSymbol(symbol);
        var type = symbol.SymbolType.ToSecurityType();
        var priceStep = symbol.MinPriceIncrement.ToDecimal();
        var security = new SecurityMessage
        {
            OriginalTransactionId = message.TransactionId,
            SecurityId = symbol.ToSecurityId(),
            Name = symbol.Name,
            ShortName = symbol.Ticker,
            SecurityType = type,
            Currency = symbol.Currency.ToCurrency(),
            PriceStep = priceStep,
            Decimals = priceStep?.GetCachedDecimals(),
            VolumeStep = 1,
            MinVolume = 1,
            ExpiryDate = symbol.Expiration.ParseExpiration(),
            Strike = symbol.OptionData?.StrikePrice.ToDecimal(),
            CfiCode = symbol.Identifiers?.Cfi,
        };

        if (!symbol.UnderlyingSymbolId.IsEmpty())
        {
            security.UnderlyingSecurityId =
                symbol.UnderlyingSymbolId.ToSecurityId();
        }

        if (type == SecurityTypes.Option)
        {
            security.OptionType =
                symbol.OptionData?.OptionRight.EqualsIgnoreCase("call") ==
                true
                    ? OptionTypes.Call
                    : symbol.OptionData?.OptionRight
                        .EqualsIgnoreCase("put") == true
                        ? OptionTypes.Put
                        : null;
        }

        if (!security.IsMatch(message, securityTypes))
            return left;

        await SendOutMessageAsync(security, cancellationToken);
        return left - 1;
    }

    /// <inheritdoc />
    protected override async ValueTask OnLevel1SubscriptionAsync(
        MarketDataMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);
        if (!message.IsSubscribe)
        {
            await RemoveMarketSubscription(
                message.OriginalTransactionId);
            return;
        }

        var symbol = await GetSymbol(
            message.SecurityId, cancellationToken);
        var securityId = symbol.ToSecurityId();
        var quote = await Rest.GetLastQuote(
            symbol.SymbolId, cancellationToken);
        await SendLevel1(quote, message.TransactionId,
            securityId, cancellationToken);

        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId, cancellationToken);
            return;
        }

        await AddMarketSubscription(message, DataType.Level1,
            token => Rest.RunQuoteStream(symbol.SymbolId, false,
                (value, ct) => SendLevel1(value,
                    message.TransactionId, securityId, ct),
                SendStreamError, token),
            cancellationToken);
        await SendSubscriptionResultAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnMarketDepthSubscriptionAsync(
        MarketDataMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);
        if (!message.IsSubscribe)
        {
            await RemoveMarketSubscription(
                message.OriginalTransactionId);
            return;
        }

        var symbol = await GetSymbol(
            message.SecurityId, cancellationToken);
        var securityId = symbol.ToSecurityId();
        var quote = await Rest.GetLastQuote(
            symbol.SymbolId, cancellationToken);
        await SendDepth(quote, message.TransactionId,
            securityId, message.MaxDepth, cancellationToken);

        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId, cancellationToken);
            return;
        }

        await AddMarketSubscription(message, DataType.MarketDepth,
            token => Rest.RunQuoteStream(symbol.SymbolId, true,
                (value, ct) => SendDepth(value,
                    message.TransactionId, securityId,
                    message.MaxDepth, ct),
                SendStreamError, token),
            cancellationToken);
        await SendSubscriptionResultAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTicksSubscriptionAsync(
        MarketDataMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);
        if (!message.IsSubscribe)
        {
            await RemoveMarketSubscription(
                message.OriginalTransactionId);
            return;
        }

        var symbol = await GetSymbol(
            message.SecurityId, cancellationToken);
        var securityId = symbol.ToSecurityId();
        if (message.From is not null ||
            message.To is not null ||
            message.Count is not null ||
            message.IsHistoryOnly())
        {
            var size = (int)Math.Clamp(
                message.Count ?? HistoryRequestSize,
                1, HistoryRequestSize);
            IEnumerable<ExanteTradeTick> trades =
                (await Rest.GetTrades(symbol.SymbolId,
                    message.From?.ToUniversalTime(),
                    message.To?.ToUniversalTime(),
                    size, cancellationToken))
                .Where(trade => trade is not null)
                .OrderBy(trade => trade.Timestamp);
            if (message.Count is > 0 and <= int.MaxValue)
            {
                trades = trades.TakeLast(
                    (int)message.Count.Value);
            }

            foreach (var trade in trades)
            {
                await SendPublicTrade(trade,
                    message.TransactionId, securityId,
                    cancellationToken);
            }
        }

        if (message.IsHistoryOnly())
        {
            await SendSubscriptionFinishedAsync(
                message.TransactionId, cancellationToken);
            return;
        }

        await AddMarketSubscription(message, DataType.Ticks,
            token => Rest.RunPublicTradeStream(symbol.SymbolId,
                (value, ct) => SendPublicTrade(value,
                    message.TransactionId, securityId, ct),
                SendStreamError, token),
            cancellationToken);
        await SendSubscriptionResultAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnTFCandlesSubscriptionAsync(
        MarketDataMessage message,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            message.TransactionId, cancellationToken);
        if (!message.IsSubscribe)
            return;
        if (!message.IsHistoryOnly())
        {
            throw new NotSupportedException(
                "EXANTE HTTP API provides historical OHLC only. " +
                "Realtime candles can be built from the public trade stream.");
        }

        var symbol = await GetSymbol(
            message.SecurityId, cancellationToken);
        var securityId = symbol.ToSecurityId();
        var timeFrame = message.GetTimeFrame();
        var size = (int)Math.Clamp(
            message.Count ?? HistoryRequestSize,
            1, HistoryRequestSize);
        IEnumerable<ExanteOhlc> candles =
            (await Rest.GetOhlc(symbol.SymbolId, timeFrame,
                message.From?.ToUniversalTime(),
                message.To?.ToUniversalTime(),
                size, cancellationToken))
            .Where(candle => candle is not null)
            .OrderBy(candle => candle.Timestamp);
        if (message.Count is > 0 and <= int.MaxValue)
        {
            candles = candles.TakeLast(
                (int)message.Count.Value);
        }

        foreach (var candle in candles)
        {
            var openTime = candle.Timestamp.FromUnixMilliseconds();
            await SendOutMessageAsync(new TimeFrameCandleMessage
            {
                OriginalTransactionId = message.TransactionId,
                SecurityId = securityId,
                TypedArg = timeFrame,
                OpenTime = openTime,
                CloseTime = openTime + timeFrame,
                OpenPrice = candle.Open.ToDecimal() ?? 0m,
                HighPrice = candle.High.ToDecimal() ?? 0m,
                LowPrice = candle.Low.ToDecimal() ?? 0m,
                ClosePrice = candle.Close.ToDecimal() ?? 0m,
                TotalVolume = candle.Volume.ToDecimal() ?? 0m,
                State = CandleStates.Finished,
            }, cancellationToken);
        }

        await SendSubscriptionFinishedAsync(
            message.TransactionId, cancellationToken);
    }

    private ValueTask SendLevel1(ExanteQuote quote,
        long originalTransactionId, SecurityId securityId,
        CancellationToken cancellationToken)
    {
        if (quote is null)
            return default;

        var bid = (quote.Bid ?? [])
            .Select(ToQuote)
            .Where(value => value is not null)
            .OrderByDescending(value => value.Value.Price)
            .FirstOrDefault();
        var ask = (quote.Ask ?? [])
            .Select(ToQuote)
            .Where(value => value is not null)
            .OrderBy(value => value.Value.Price)
            .FirstOrDefault();
        var message = new Level1ChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = securityId,
            ServerTime = quote.Timestamp > 0
                ? quote.Timestamp.FromUnixMilliseconds()
                : CurrentTime,
        }
        .TryAdd(Level1Fields.BestBidPrice, bid?.Price)
        .TryAdd(Level1Fields.BestBidVolume, bid?.Volume)
        .TryAdd(Level1Fields.BestAskPrice, ask?.Price)
        .TryAdd(Level1Fields.BestAskVolume, ask?.Volume);
        return message.Changes.Count == 0
            ? default
            : SendOutMessageAsync(message, cancellationToken);
    }

    private ValueTask SendDepth(ExanteQuote quote,
        long originalTransactionId, SecurityId securityId,
        int? requestedDepth,
        CancellationToken cancellationToken)
    {
        if (quote is null)
            return default;

        var depth = Math.Clamp(
            requestedDepth ?? MaxMarketDepth,
            1, MaxMarketDepth);
        return SendOutMessageAsync(new QuoteChangeMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = securityId,
            ServerTime = quote.Timestamp > 0
                ? quote.Timestamp.FromUnixMilliseconds()
                : CurrentTime,
            Bids = (quote.Bid ?? [])
                .Select(ToQuote)
                .Where(value => value is not null)
                .Select(value => value.Value)
                .OrderByDescending(value => value.Price)
                .Take(depth)
                .Select(value =>
                    new QuoteChange(value.Price, value.Volume))
                .ToArray(),
            Asks = (quote.Ask ?? [])
                .Select(ToQuote)
                .Where(value => value is not null)
                .Select(value => value.Value)
                .OrderBy(value => value.Price)
                .Take(depth)
                .Select(value =>
                    new QuoteChange(value.Price, value.Volume))
                .ToArray(),
        }, cancellationToken);
    }

    private ValueTask SendPublicTrade(ExanteTradeTick trade,
        long originalTransactionId, SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var price = trade?.Price.ToDecimal();
        if (price is null)
            return default;

        var serverTime = trade.Timestamp > 0
            ? trade.Timestamp.FromUnixMilliseconds()
            : CurrentTime;
        var volume = trade.Size.ToDecimal();
        return SendOutMessageAsync(new ExecutionMessage
        {
            DataTypeEx = DataType.Ticks,
            OriginalTransactionId = originalTransactionId,
            SecurityId = securityId,
            ServerTime = serverTime,
            TradeStringId =
                $"{trade.SymbolId}:{trade.Timestamp}:" +
                $"{trade.Price}:{trade.Size}",
            TradePrice = price,
            TradeVolume = volume,
        }, cancellationToken);
    }

    private static (decimal Price, decimal Volume)? ToQuote(
        ExanteQuoteSide side)
    {
        var price = side?.Price.ToDecimal();
        if (price is null)
            return null;
        return (price.Value,
            side.Size.ToDecimal() ??
            side.Value.ToDecimal() ?? 0m);
    }
}
