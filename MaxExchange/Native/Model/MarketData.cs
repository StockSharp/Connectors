namespace StockSharp.MaxExchange.Native.Model;

class MaxExchangeSymbol
{
	[JsonProperty("id")]
	public string Pair { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("base_unit")]
	public string Base { get; set; }

	[JsonProperty("base_unit_precision")]
	public int AmountPrecision { get; set; }

	[JsonProperty("min_base_amount")]
	public decimal? MinimumAmount { get; set; }

	[JsonProperty("quote_unit")]
	public string Quote { get; set; }

	[JsonProperty("quote_unit_precision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("min_quote_amount")]
	public decimal? MinimumQuoteAmount { get; set; }

	[JsonProperty("m_wallet_supported")]
	public bool MarginWalletSupported { get; set; }

	[JsonIgnore]
	public decimal? MaximumAmount => null;

	[JsonIgnore]
	public bool IsMaintenance
		=> !Status.EqualsIgnoreCase("active");

	[JsonIgnore]
	public string SecurityCode
		=> MaxExchangeExtensions.CreateSecurityCode(Base, Quote);
}

sealed class MaxExchangeMarket : MaxExchangeSymbol
{
	[JsonIgnore]
	public string Id
	{
		get => Pair;
		set => Pair = value;
	}

	[JsonIgnore]
	public string BaseUnit
	{
		get => Base;
		set => Base = value;
	}

	[JsonIgnore]
	public string QuoteUnit
	{
		get => Quote;
		set => Quote = value;
	}

	[JsonIgnore]
	public int BasePrecision
	{
		get => AmountPrecision;
		set => AmountPrecision = value;
	}

	[JsonIgnore]
	public int QuoteUnitPrecision
	{
		get => QuotePrecision;
		set => QuotePrecision = value;
	}
}

sealed class MaxExchangeTicker
{
	[JsonProperty("market")]
	public string Pair { get; set; }

	[JsonProperty("at")]
	public long At { get; set; }

	[JsonProperty("buy")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("buy_vol")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("sell")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("sell_vol")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("open")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("low")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("high")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("last")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("vol")]
	public decimal? Volume { get; set; }

	[JsonProperty("vol_in_btc")]
	public decimal? VolumeInBtc { get; set; }

	[JsonIgnore]
	public decimal? Last
	{
		get => LastPrice;
		set => LastPrice = value;
	}

	[JsonIgnore]
	public decimal? PriceChange
		=> LastPrice is decimal last && OpenPrice is decimal open
			? last - open
			: null;

	[JsonIgnore]
	public bool? IsBuyer => null;

	[JsonIgnore]
	public long Timestamp
		=> At is > 0 and < 100_000_000_000
			? checked(At * 1000)
			: At;
}

sealed class MaxExchangeOrderBook
{
	[JsonIgnore]
	public string Pair { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("last_update_version")]
	public long LastUpdateVersion { get; set; }

	[JsonProperty("last_update_id")]
	public long LastUpdateId { get; set; }

	[JsonIgnore]
	public int Limit { get; set; }

	[JsonProperty("asks")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("bids")]
	public decimal[][] Bids { get; set; }
}

sealed class MaxExchangeTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal Amount { get; set; }

	[JsonProperty("funds")]
	public decimal Funds { get; set; }

	[JsonProperty("market")]
	public string Pair { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("created_at")]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public bool? IsBuyer
		=> Side?.ToLowerInvariant() switch
		{
			"bid" or "buy" => true,
			"ask" or "sell" => false,
			_ => null,
		};
}

sealed class MaxExchangeTradePush
{
	public string Pair { get; set; }
	public string EventId { get; set; }
	public MaxExchangeTrade[] Data { get; set; }
}

sealed class MaxExchangeCandle
{
	public long Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public bool IsFinished { get; set; } = true;
}

sealed class MaxExchangeBookEvent
{
	[JsonProperty("c")]
	public string Channel { get; set; }

	[JsonProperty("M")]
	public string Market { get; set; }

	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("a")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("b")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("T")]
	public long Timestamp { get; set; }

	[JsonProperty("fi")]
	public long FirstUpdateId { get; set; }

	[JsonProperty("li")]
	public long LastUpdateId { get; set; }

	[JsonProperty("v")]
	public long Version { get; set; }
}

sealed class MaxExchangeTradeEvent
{
	[JsonProperty("c")]
	public string Channel { get; set; }

	[JsonProperty("M")]
	public string Market { get; set; }

	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("t")]
	public MaxExchangeStreamTrade[] Trades { get; set; }

	[JsonProperty("T")]
	public long Timestamp { get; set; }
}

sealed class MaxExchangeStreamTrade
{
	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("T")]
	public long Timestamp { get; set; }

	[JsonProperty("tr")]
	public string Trend { get; set; }
}

sealed class MaxExchangeTickerEvent
{
	[JsonProperty("M")]
	public string Market { get; set; }

	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("tk")]
	public MaxExchangeStreamTicker Ticker { get; set; }

	[JsonProperty("T")]
	public long Timestamp { get; set; }
}

sealed class MaxExchangeStreamTicker
{
	[JsonProperty("M")]
	public string Market { get; set; }

	[JsonProperty("O")]
	public decimal Open { get; set; }

	[JsonProperty("H")]
	public decimal High { get; set; }

	[JsonProperty("L")]
	public decimal Low { get; set; }

	[JsonProperty("C")]
	public decimal Close { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("V")]
	public decimal VolumeInBtc { get; set; }
}

sealed class MaxExchangeKlineEvent
{
	[JsonProperty("M")]
	public string Market { get; set; }

	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("k")]
	public MaxExchangeStreamKline Kline { get; set; }

	[JsonProperty("T")]
	public long Timestamp { get; set; }
}

sealed class MaxExchangeStreamKline
{
	[JsonProperty("ST")]
	public long StartTime { get; set; }

	[JsonProperty("ET")]
	public long EndTime { get; set; }

	[JsonProperty("R")]
	public string Resolution { get; set; }

	[JsonProperty("O")]
	public decimal Open { get; set; }

	[JsonProperty("H")]
	public decimal High { get; set; }

	[JsonProperty("L")]
	public decimal Low { get; set; }

	[JsonProperty("C")]
	public decimal Close { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("ti")]
	public long LastTradeId { get; set; }

	[JsonProperty("x")]
	public bool IsFinished { get; set; }
}

sealed class MaxExchangeMarketStatusEvent
{
	[JsonProperty("ms")]
	public MaxExchangeStreamMarket[] Markets { get; set; }

	[JsonProperty("T")]
	public long Timestamp { get; set; }
}

sealed class MaxExchangeStreamMarket
{
	[JsonProperty("M")]
	public string Id { get; set; }

	[JsonProperty("st")]
	public string Status { get; set; }

	[JsonProperty("bu")]
	public string BaseUnit { get; set; }

	[JsonProperty("bup")]
	public int BasePrecision { get; set; }

	[JsonProperty("mba")]
	public decimal MinimumBaseAmount { get; set; }

	[JsonProperty("qu")]
	public string QuoteUnit { get; set; }

	[JsonProperty("qup")]
	public int QuotePrecision { get; set; }

	[JsonProperty("mqa")]
	public decimal MinimumQuoteAmount { get; set; }

	[JsonProperty("mws")]
	public bool MarginWalletSupported { get; set; }

	public MaxExchangeSymbol ToMarket()
		=> new()
		{
			Pair = Id,
			Status = Status,
			Base = BaseUnit,
			AmountPrecision = BasePrecision,
			MinimumAmount = MinimumBaseAmount,
			Quote = QuoteUnit,
			QuotePrecision = QuotePrecision,
			MinimumQuoteAmount = MinimumQuoteAmount,
			MarginWalletSupported = MarginWalletSupported,
		};
}
