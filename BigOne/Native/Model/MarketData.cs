namespace StockSharp.BigOne.Native.Model;

sealed class BigOneSpotPair : BigOneSymbol
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("base_scale")]
	public int BaseScale { get; set; }

	[JsonProperty("quote_scale")]
	public int QuoteScale { get; set; }

	[JsonProperty("base_asset")]
	public BigOneAsset BaseAsset { get; set; }

	[JsonProperty("quote_asset")]
	public BigOneAsset QuoteAsset { get; set; }

	[JsonProperty("min_quote_value")]
	public decimal? MinimumQuoteValue { get; set; }

	[JsonProperty("max_quote_value")]
	public decimal? MaximumQuoteValue { get; set; }

	public override BigOneMarketKind Kind => BigOneMarketKind.Spot;
	public override string Pair => Name;
	public override string Base => BaseAsset?.Symbol;
	public override string Quote => QuoteAsset?.Symbol;
	public override int AmountPrecision => BaseScale;
	public override int QuotePrecision => QuoteScale;
	public override decimal? PriceStep
		=> BigOneExtensions.GetStep(QuoteScale);
	public override decimal? VolumeStep
		=> BigOneExtensions.GetStep(BaseScale);
	public override string SecurityCode
		=> BigOneExtensions.CreateSecurityCode(Base, Quote);
}

sealed class BigOneContractInstrument : BigOneSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("usdtPrice")]
	public decimal? UsdtPrice { get; set; }

	[JsonProperty("btcPrice")]
	public decimal? BtcPrice { get; set; }

	[JsonProperty("ethPrice")]
	public decimal? EthPrice { get; set; }

	[JsonProperty("nextFundingRate")]
	public decimal? NextFundingRate { get; set; }

	[JsonProperty("fundingRate")]
	public decimal? FundingRate { get; set; }

	[JsonProperty("latestPrice")]
	public decimal? LatestPrice { get; set; }

	[JsonProperty("last24hPriceChange")]
	public decimal? Last24hPriceChange { get; set; }

	[JsonProperty("indexPrice")]
	public decimal? IndexPrice { get; set; }

	[JsonProperty("volume24h")]
	public decimal? Volume24h { get; set; }

	[JsonProperty("turnover24h")]
	public decimal? Turnover24h { get; set; }

	[JsonProperty("nextFundingTime")]
	public long NextFundingTime { get; set; }

	[JsonProperty("markPrice")]
	public decimal? MarkPrice { get; set; }

	[JsonProperty("last24hMaxPrice")]
	public decimal? Last24hMaxPrice { get; set; }

	[JsonProperty("last24hMinPrice")]
	public decimal? Last24hMinPrice { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest { get; set; }

	public override BigOneMarketKind Kind
		=> BigOneMarketKind.Contract;
	public override string Pair => Symbol;
	public override string SecurityCode => Symbol?.ToUpperInvariant();
	public override string Base => SplitSymbol().baseCurrency;
	public override string Quote => SplitSymbol().quoteCurrency;
	public override int AmountPrecision => 0;
	public override int QuotePrecision => 8;
	public override decimal? PriceStep => null;
	public override decimal? VolumeStep => 1m;
	public override decimal? MinimumAmount => 1m;

	public BigOneTicker ToTicker()
		=> new()
		{
			Pair = Symbol,
			LastPrice = LatestPrice,
			HighPrice = Last24hMaxPrice,
			LowPrice = Last24hMinPrice,
			Volume = Volume24h,
			PriceChange = LatestPrice is decimal last &&
				Last24hPriceChange is decimal change
					? last * change
					: null,
			MarkPrice = MarkPrice,
			IndexPrice = IndexPrice,
			FundingRate = FundingRate,
			OpenInterest = OpenInterest,
			Timestamp = DateTime.UtcNow.ToBigOneMilliseconds(),
		};

	private (string baseCurrency, string quoteCurrency) SplitSymbol()
	{
		var symbol = Symbol?.Trim().ToUpperInvariant() ?? string.Empty;
		foreach (var quote in new[] { "USDT", "USDC", "USD" })
			if (symbol.Length > quote.Length &&
				symbol.EndsWith(quote, StringComparison.Ordinal))
				return (symbol[..^quote.Length], quote);
		return (symbol, null);
	}
}

class BigOneTicker
{
	public string Pair { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? BidPrice { get; set; }
	public decimal? BidVolume { get; set; }
	public decimal? AskPrice { get; set; }
	public decimal? AskVolume { get; set; }
	public decimal? OpenPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal? Volume { get; set; }
	public decimal? PriceChange { get; set; }
	public decimal? MarkPrice { get; set; }
	public decimal? IndexPrice { get; set; }
	public decimal? FundingRate { get; set; }
	public decimal? OpenInterest { get; set; }
	public long Timestamp { get; set; }
	public bool? IsBuyer => null;
}

sealed class BigOneSpotTicker
{
	[JsonProperty("asset_pair_name")]
	public string Market { get; set; }

	[JsonProperty("market")]
	private string StreamMarket
	{
		set
		{
			if (!value.IsEmpty())
				Market = value;
		}
	}

	[JsonProperty("bid")]
	public BigOnePriceLevel Bid { get; set; }

	[JsonProperty("ask")]
	public BigOnePriceLevel Ask { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("daily_change")]
	public decimal? DailyChange { get; set; }

	[JsonProperty("dailyChange")]
	private decimal? StreamDailyChange
	{
		set => DailyChange = value;
	}

	public BigOneTicker ToTicker()
		=> new()
		{
			Pair = Market,
			LastPrice = Close,
			BidPrice = Bid?.Price,
			BidVolume = Bid?.Amount,
			AskPrice = Ask?.Price,
			AskVolume = Ask?.Amount,
			OpenPrice = Open,
			HighPrice = High,
			LowPrice = Low,
			Volume = Volume,
			PriceChange = DailyChange ??
				(Close is decimal close && Open is decimal open
					? close - open
					: null),
			Timestamp = DateTime.UtcNow.ToBigOneMilliseconds(),
		};
}

sealed class BigOneSpotDepth
{
	[JsonProperty("asset_pair_name")]
	public string Market { get; set; }

	[JsonProperty("market")]
	private string StreamMarket
	{
		set
		{
			if (!value.IsEmpty())
				Market = value;
		}
	}

	[JsonProperty("bids")]
	public BigOnePriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BigOnePriceLevel[] Asks { get; set; }

	public BigOneOrderBook ToOrderBook()
		=> new()
		{
			Pair = Market,
			Timestamp = DateTime.UtcNow.ToBigOneMilliseconds(),
			Bids = ToLevels(Bids),
			Asks = ToLevels(Asks),
		};

	private static decimal[][] ToLevels(BigOnePriceLevel[] levels)
		=> [.. (levels ?? []).Select(
			static level => new[] { level.Price, level.Amount })];
}

sealed class BigOneContractDepth
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bids")]
	public Dictionary<string, decimal> Bids { get; set; }

	[JsonProperty("asks")]
	public Dictionary<string, decimal> Asks { get; set; }

	[JsonProperty("to")]
	public long To { get; set; }

	[JsonProperty("from")]
	public long From { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("bestPrices")]
	public BigOneBestPrices BestPrices { get; set; }

	public BigOneOrderBook ToOrderBook(string fallbackSymbol)
		=> new()
		{
			Pair = Symbol.IsEmpty() ? fallbackSymbol : Symbol,
			Timestamp = DateTime.UtcNow.ToBigOneMilliseconds(),
			Bids = ToLevels(Bids),
			Asks = ToLevels(Asks),
		};

	private static decimal[][] ToLevels(
		IDictionary<string, decimal> levels)
		=> [.. (levels ?? new Dictionary<string, decimal>())
			.Select(static pair => new[]
			{
				decimal.Parse(pair.Key, CultureInfo.InvariantCulture),
				pair.Value,
			})];
}

sealed class BigOneOrderBook
{
	public string Pair { get; set; }
	public long Timestamp { get; set; }
	public int Limit { get; set; }
	public decimal[][] Asks { get; set; }
	public decimal[][] Bids { get; set; }
}

sealed class BigOneSpotTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("taker_side")]
	public string TakerSide { get; set; }

	[JsonProperty("takerSide")]
	private string StreamTakerSide
	{
		set => TakerSide = value;
	}

	[JsonProperty("inserted_at")]
	public DateTime? InsertedAt { get; set; }

	[JsonProperty("createdAt")]
	private DateTime? StreamCreatedAt
	{
		set => InsertedAt = value;
	}

	public BigOneTrade ToTrade(string fallbackMarket)
		=> new()
		{
			Id = Id,
			Pair = Market.IsEmpty() ? fallbackMarket : Market,
			Price = Price,
			Amount = Amount,
			Timestamp = InsertedAt?.ToUtc()
				.ToBigOneMilliseconds() ?? 0,
			IsBuyer = TakerSide.EqualsIgnoreCase("BID"),
		};
}

sealed class BigOneContractTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("matchedAt")]
	public long MatchedAt { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	public BigOneTrade ToTrade()
		=> new()
		{
			Id = Id,
			Pair = Symbol,
			Price = Price,
			Amount = Size,
			Timestamp = MatchedAt,
			IsBuyer = Side.EqualsIgnoreCase("BUY"),
		};
}

sealed class BigOneTrade
{
	public string Id { get; set; }
	public string Pair { get; set; }
	public decimal Price { get; set; }
	public decimal Amount { get; set; }
	public long Timestamp { get; set; }
	public bool? IsBuyer { get; set; }
}

sealed class BigOneTradePush
{
	public string Pair { get; set; }
	public string EventId { get; set; }
	public BigOneTrade[] Data { get; set; }
}

sealed class BigOneSpotCandle
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("time")]
	public DateTime Time { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("period")]
	public string Period { get; set; }

	public BigOneCandle ToCandle(bool isFinished = true)
		=> new()
		{
			Pair = Market,
			Timestamp = Time.ToUtc().ToBigOneMilliseconds(),
			Open = Open,
			High = High,
			Low = Low,
			Close = Close,
			Volume = Volume,
			IsFinished = isFinished,
		};
}

sealed class BigOneContractCandle
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("turnover")]
	public decimal? Turnover { get; set; }

	[JsonProperty("nTrades")]
	public long TradesCount { get; set; }

	[JsonProperty("nextTs")]
	public long NextTimestamp { get; set; }

	public BigOneCandle ToCandle()
		=> new()
		{
			Pair = Symbol,
			Timestamp = Time,
			Open = Open,
			High = High,
			Low = Low,
			Close = Close,
			Volume = Volume,
			IsFinished = NextTimestamp <=
				DateTime.UtcNow.ToBigOneMilliseconds(),
		};
}

sealed class BigOneCandle
{
	public string Pair { get; set; }
	public long Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public bool IsFinished { get; set; } = true;
}

sealed class BigOneKlineEvent
{
	public string Market { get; set; }
	public BigOneStreamKline Kline { get; set; }
}

sealed class BigOneStreamKline
{
	public long StartTime { get; set; }
	public long EndTime { get; set; }
	public string Resolution { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public bool IsFinished { get; set; }
}

sealed class BigOneSpotWsMessage
{
	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("error")]
	public BigOneWsError Error { get; set; }

	[JsonProperty("tickersSnapshot")]
	public BigOneSpotTickersContainer TickersSnapshot { get; set; }

	[JsonProperty("tickerUpdate")]
	public BigOneSpotTickerContainer TickerUpdate { get; set; }

	[JsonProperty("depthSnapshot")]
	public BigOneSpotDepthContainer DepthSnapshot { get; set; }

	[JsonProperty("depthUpdate")]
	public BigOneSpotDepthContainer DepthUpdate { get; set; }

	[JsonProperty("tradesSnapshot")]
	public BigOneSpotTradesContainer TradesSnapshot { get; set; }

	[JsonProperty("tradeUpdate")]
	public BigOneSpotTradeContainer TradeUpdate { get; set; }

	[JsonProperty("candlesSnapshot")]
	public BigOneSpotCandlesContainer CandlesSnapshot { get; set; }

	[JsonProperty("candleUpdate")]
	public BigOneSpotCandleContainer CandleUpdate { get; set; }

	[JsonProperty("accountsSnapshot")]
	public BigOneSpotAccountsContainer AccountsSnapshot { get; set; }

	[JsonProperty("accountUpdate")]
	public BigOneSpotAccountContainer AccountUpdate { get; set; }

	[JsonProperty("ordersSnapshot")]
	public BigOneSpotOrdersContainer OrdersSnapshot { get; set; }

	[JsonProperty("orderUpdate")]
	public BigOneSpotOrderContainer OrderUpdate { get; set; }
}

sealed class BigOneWsError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class BigOneSpotTickersContainer
{
	[JsonProperty("tickers")]
	public BigOneSpotTicker[] Tickers { get; set; }
}

sealed class BigOneSpotTickerContainer
{
	[JsonProperty("ticker")]
	public BigOneSpotTicker Ticker { get; set; }
}

sealed class BigOneSpotDepthContainer
{
	[JsonProperty("depth")]
	public BigOneSpotDepth Depth { get; set; }
}

sealed class BigOneSpotTradesContainer
{
	[JsonProperty("trades")]
	public BigOneSpotTrade[] Trades { get; set; }
}

sealed class BigOneSpotTradeContainer
{
	[JsonProperty("trade")]
	public BigOneSpotTrade Trade { get; set; }
}

sealed class BigOneSpotCandlesContainer
{
	[JsonProperty("candles")]
	public BigOneSpotCandle[] Candles { get; set; }
}

sealed class BigOneSpotCandleContainer
{
	[JsonProperty("candle")]
	public BigOneSpotCandle Candle { get; set; }
}

sealed class BigOneSpotAccountsContainer
{
	[JsonProperty("accounts")]
	public BigOneSpotAccount[] Accounts { get; set; }
}

sealed class BigOneSpotAccountContainer
{
	[JsonProperty("account")]
	public BigOneSpotAccount Account { get; set; }
}

sealed class BigOneSpotOrdersContainer
{
	[JsonProperty("orders")]
	public BigOneSpotOrder[] Orders { get; set; }
}

sealed class BigOneSpotOrderContainer
{
	[JsonProperty("order")]
	public BigOneSpotOrder Order { get; set; }
}
