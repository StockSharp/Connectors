namespace StockSharp.AscendEx.Native.Model;

abstract class AscendExSymbol
{
	public abstract string Pair { get; set; }
	public abstract string Base { get; }
	public abstract string Quote { get; }
	public abstract decimal? PriceStep { get; set; }
	public abstract decimal? VolumeStep { get; set; }
	public abstract decimal? MinimumAmount { get; set; }
	public abstract decimal? MaximumAmount { get; set; }
	public abstract decimal? MinimumNotional { get; set; }
	public abstract bool IsFutures { get; }
	public abstract bool IsTrading { get; }

	[JsonIgnore]
	public bool IsMaintenance => !IsTrading;

	[JsonIgnore]
	public int QuotePrecision
		=> GetPrecision(PriceStep);

	[JsonIgnore]
	public int AmountPrecision
		=> GetPrecision(VolumeStep);

	[JsonIgnore]
	public string SecurityCode
		=> IsFutures
			? Pair.ToAscendExFuturesSymbol()
			: Pair.ToAscendExSpotSymbol();

	private static int GetPrecision(decimal? step)
	{
		if (step is not > 0)
			return 0;
		var value = step.Value;
		var precision = 0;
		while (value != decimal.Truncate(value) && precision < 28)
		{
			value *= 10;
			precision++;
		}
		return precision;
	}
}

sealed class AscendExSpotProduct : AscendExSymbol
{
	[JsonProperty("symbol")]
	public override string Pair { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("baseAsset")]
	private string NativeBase { get; set; }

	[JsonProperty("quoteAsset")]
	private string NativeQuote { get; set; }

	[JsonProperty("tradingStartTime")]
	public long TradingStartTime { get; set; }

	[JsonProperty("statusCode")]
	public string Status { get; set; }

	[JsonProperty("tickSize")]
	public override decimal? PriceStep { get; set; }

	[JsonProperty("lotSize")]
	public override decimal? VolumeStep { get; set; }

	[JsonProperty("minQty")]
	public override decimal? MinimumAmount { get; set; }

	[JsonProperty("maxQty")]
	public override decimal? MaximumAmount { get; set; }

	[JsonProperty("minNotional")]
	public override decimal? MinimumNotional { get; set; }

	[JsonProperty("maxNotional")]
	public decimal? MaximumNotional { get; set; }

	[JsonProperty("qtyScale")]
	public int QuantityScale { get; set; }

	[JsonProperty("priceScale")]
	public int PriceScale { get; set; }

	[JsonIgnore]
	public override string Base
		=> NativeBase.IsEmpty()
			? SplitPair()[0]
			: NativeBase.ToUpperInvariant();

	[JsonIgnore]
	public override string Quote
		=> NativeQuote.IsEmpty()
			? SplitPair()[1]
			: NativeQuote.ToUpperInvariant();

	[JsonIgnore]
	public override bool IsFutures => false;

	[JsonIgnore]
	public override bool IsTrading
		=> Status.EqualsIgnoreCase("Normal");

	private string[] SplitPair()
		=> Pair.ThrowIfEmpty(nameof(Pair)).Split('/');
}

sealed class AscendExFuturesContract : AscendExSymbol
{
	[JsonProperty("symbol")]
	public override string Pair { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("settlementAsset")]
	public string SettlementAsset { get; set; }

	[JsonProperty("underlying")]
	public string Underlying { get; set; }

	[JsonProperty("tradingStartTime")]
	public long TradingStartTime { get; set; }

	[JsonProperty("priceFilter")]
	public AscendExPriceFilter PriceFilter { get; set; }

	[JsonProperty("lotSizeFilter")]
	public AscendExLotSizeFilter LotSizeFilter { get; set; }

	[JsonIgnore]
	public override string Base
		=> Underlying?.Split('/').FirstOrDefault() ??
			Pair?.Split('-').FirstOrDefault();

	[JsonIgnore]
	public override string Quote => SettlementAsset;

	[JsonIgnore]
	public override decimal? PriceStep
	{
		get => PriceFilter?.TickSize;
		set
		{
			PriceFilter ??= new();
			PriceFilter.TickSize = value ?? 0;
		}
	}

	[JsonIgnore]
	public override decimal? VolumeStep
	{
		get => LotSizeFilter?.LotSize;
		set
		{
			LotSizeFilter ??= new();
			LotSizeFilter.LotSize = value ?? 0;
		}
	}

	[JsonIgnore]
	public override decimal? MinimumAmount
	{
		get => LotSizeFilter?.MinimumQty;
		set
		{
			LotSizeFilter ??= new();
			LotSizeFilter.MinimumQty = value ?? 0;
		}
	}

	[JsonIgnore]
	public override decimal? MaximumAmount
	{
		get => LotSizeFilter?.MaximumQty;
		set
		{
			LotSizeFilter ??= new();
			LotSizeFilter.MaximumQty = value ?? 0;
		}
	}

	[JsonIgnore]
	public override decimal? MinimumNotional
	{
		get => null;
		set { }
	}

	[JsonIgnore]
	public override bool IsFutures => true;

	[JsonIgnore]
	public override bool IsTrading
		=> Status.EqualsIgnoreCase("Normal");
}

sealed class AscendExPriceFilter
{
	[JsonProperty("minPrice")]
	public decimal MinimumPrice { get; set; }

	[JsonProperty("maxPrice")]
	public decimal MaximumPrice { get; set; }

	[JsonProperty("tickSize")]
	public decimal TickSize { get; set; }
}

sealed class AscendExLotSizeFilter
{
	[JsonProperty("minQty")]
	public decimal MinimumQty { get; set; }

	[JsonProperty("maxQty")]
	public decimal MaximumQty { get; set; }

	[JsonProperty("lotSize")]
	public decimal LotSize { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
sealed class AscendExQuote
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
}

sealed class AscendExTicker
{
	[JsonProperty("symbol")]
	public string Pair { get; set; }

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

	[JsonProperty("baseVol")]
	private decimal? FuturesVolume
	{
		set
		{
			if (value is not null)
				Volume = value;
		}
	}

	[JsonProperty("bid")]
	public AscendExQuote Bid { get; set; }

	[JsonProperty("ask")]
	public AscendExQuote Ask { get; set; }

	[JsonProperty("time")]
	public long At { get; set; }

	[JsonIgnore]
	public decimal? BidPrice => Bid?.Price;

	[JsonIgnore]
	public decimal? BidVolume => Bid?.Volume;

	[JsonIgnore]
	public decimal? AskPrice => Ask?.Price;

	[JsonIgnore]
	public decimal? AskVolume => Ask?.Volume;

	[JsonIgnore]
	public decimal? OpenPrice => Open;

	[JsonIgnore]
	public decimal? HighPrice => High;

	[JsonIgnore]
	public decimal? LowPrice => Low;

	[JsonIgnore]
	public decimal? LastPrice => Close;

	[JsonIgnore]
	public decimal? PriceChange
		=> Close is decimal close && Open is decimal open
			? close - open
			: null;

	[JsonIgnore]
	public bool? IsBuyer => null;

	[JsonIgnore]
	public long Timestamp => At;
}

sealed class AscendExMarketEnvelope<TData>
{
	[JsonProperty("m")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("s")]
	private string CompactSymbol
	{
		set
		{
			if (!value.IsEmpty())
				Symbol = value;
		}
	}

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class AscendExOrderBook
{
	[JsonProperty("seqnum")]
	public long Sequence { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("asks")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("bids")]
	public decimal[][] Bids { get; set; }

	[JsonIgnore]
	public string Pair { get; set; }

	[JsonIgnore]
	public int Limit { get; set; }
}

sealed class AscendExTrade
{
	[JsonProperty("seqnum")]
	public long Sequence { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("bm")]
	public bool BuyerMaker { get; set; }

	[JsonIgnore]
	public long Id => Sequence;

	[JsonIgnore]
	public decimal Amount => Quantity;

	[JsonIgnore]
	public string Pair { get; set; }

	[JsonIgnore]
	public bool? IsBuyer => !BuyerMaker;
}

sealed class AscendExTradePush
{
	public string Pair { get; set; }
	public string EventId { get; set; }
	public AscendExTrade[] Data { get; set; }
}

sealed class AscendExBar
{
	[JsonProperty("i")]
	public string Interval { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonIgnore]
	public bool IsFinished { get; set; } = true;
}

sealed class AscendExBarEnvelope
{
	[JsonProperty("m")]
	public string Topic { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("data")]
	public AscendExBar Data { get; set; }
}

sealed class AscendExBbo
{
	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("bid")]
	public AscendExQuote Bid { get; set; }

	[JsonProperty("ask")]
	public AscendExQuote Ask { get; set; }
}

sealed class AscendExWsMessage
{
	[JsonProperty("m")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("s")]
	private string CompactSymbol
	{
		set
		{
			if (!value.IsEmpty())
				Symbol = value;
		}
	}

	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("data")]
	public JToken Data { get; set; }

	[JsonProperty("con")]
	public AscendExFuturesPricing[] Contracts { get; set; }
}

sealed class AscendExFuturesPricing
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("ip")]
	public decimal IndexPrice { get; set; }

	[JsonProperty("mp")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("r")]
	public decimal FundingRate { get; set; }

	[JsonProperty("oi")]
	public decimal OpenInterest { get; set; }

	[JsonProperty("f")]
	public long NextFundingTime { get; set; }
}

sealed class AscendExKlineEvent
{
	public string Market { get; set; }
	public AscendExBar Kline { get; set; }
}
