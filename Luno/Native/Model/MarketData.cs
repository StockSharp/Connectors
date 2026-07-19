namespace StockSharp.Luno.Native.Model;

sealed class LunoMarketsResponse
{
	[JsonProperty("markets")]
	public LunoMarketInfo[] Markets { get; init; }
}

sealed class LunoMarketInfo
{
	[JsonProperty("market_id")]
	public string MarketId { get; init; }

	[JsonProperty("trading_status")]
	public LunoTradingStatuses TradingStatus { get; init; }

	[JsonProperty("base_currency")]
	public string BaseCurrency { get; init; }

	[JsonProperty("counter_currency")]
	public string CounterCurrency { get; init; }

	[JsonProperty("min_volume")]
	public decimal MinimumVolume { get; init; }

	[JsonProperty("max_volume")]
	public decimal MaximumVolume { get; init; }

	[JsonProperty("volume_scale")]
	public int VolumeScale { get; init; }

	[JsonProperty("min_price")]
	public decimal MinimumPrice { get; init; }

	[JsonProperty("max_price")]
	public decimal MaximumPrice { get; init; }

	[JsonProperty("price_scale")]
	public int PriceScale { get; init; }

	[JsonProperty("fee_scale")]
	public int FeeScale { get; init; }
}

sealed class LunoTickersResponse
{
	[JsonProperty("tickers")]
	public LunoTicker[] Tickers { get; init; }
}

sealed class LunoTicker
{
	[JsonProperty("ask")]
	public decimal Ask { get; init; }

	[JsonProperty("bid")]
	public decimal Bid { get; init; }

	[JsonProperty("last_trade")]
	public decimal LastTrade { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("rolling_24_hour_volume")]
	public decimal RollingVolume { get; init; }

	[JsonProperty("status")]
	public LunoTickerStatuses Status { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }
}

sealed class LunoOrderBook
{
	[JsonProperty("asks")]
	public LunoOrderBookLevel[] Asks { get; init; }

	[JsonProperty("bids")]
	public LunoOrderBookLevel[] Bids { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }
}

sealed class LunoOrderBookLevel
{
	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; init; }
}

sealed class LunoPublicTradesResponse
{
	[JsonProperty("trades")]
	public LunoPublicTrade[] Trades { get; init; }
}

sealed class LunoPublicTrade
{
	[JsonProperty("is_buy")]
	public bool IsBuy { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("sequence")]
	public long Sequence { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; init; }
}

sealed class LunoCandlesResponse
{
	[JsonProperty("candles")]
	public LunoCandle[] Candles { get; init; }

	[JsonProperty("duration")]
	public long Duration { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }
}

sealed class LunoCandle
{
	[JsonProperty("close")]
	public decimal Close { get; init; }

	[JsonProperty("high")]
	public decimal High { get; init; }

	[JsonProperty("low")]
	public decimal Low { get; init; }

	[JsonProperty("open")]
	public decimal Open { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; init; }
}

sealed class LunoTickerRequest
{
	public string Pair { get; init; }
}

sealed class LunoTradesRequest
{
	public string Pair { get; init; }
	public long? Since { get; init; }
}

sealed class LunoCandlesRequest
{
	public string Pair { get; init; }
	public long Since { get; init; }
	public int Duration { get; init; }
}
