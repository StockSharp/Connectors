namespace StockSharp.QuantFeed.Native.Model;

enum QuantFeedFileKinds
{
	Unknown,
	Reference,
	MarketData,
}

sealed class QuantFeedCsvRow(string[] values, long lineNumber)
{
	private readonly string[] _values = values ?? throw new ArgumentNullException(nameof(values));

	public long LineNumber { get; } = lineNumber;

	public string Get(int index)
		=> index >= 0 && index < _values.Length ? _values[index]?.Trim() : null;
}

sealed class QuantFeedCsvHeader
{
	private QuantFeedCsvHeader(string[] names)
	{
		InstrumentCode = Find(names, "FeedOSInstrumentCode", "InstrumentCode",
			"InstrumentId", "InstrumentID", "FIC", "SecurityId", "SecurityID");
		Symbol = Find(names, "LocalCode", "Symbol", "Ticker", "SecurityCode");
		Mic = Find(names, "MIC", "Mic", "MarketIdentifierCode", "MarketCode", "Market");
		Name = Find(names, "InstrumentName", "SecurityName", "Name", "Description");
		SecurityType = Find(names, "InstrumentType", "SecurityType", "AssetType", "TypeName");
		Currency = Find(names, "Currency", "CurrencyCode", "TradingCurrency");
		Isin = Find(names, "ISIN", "Isin");
		Expiration = Find(names, "ExpirationDate", "ExpiryDate", "MaturityDate");
		Strike = Find(names, "Strike", "StrikePrice");
		OptionType = Find(names, "OptionType", "CallPut", "PutCall");
		PriceStep = Find(names, "PriceStep", "TickSize", "MinimumPriceIncrement");
		Multiplier = Find(names, "Multiplier", "ContractMultiplier", "LotSize");

		MarketTimestamp = Find(names, "MarketTimestamp", "MktTimestamp", "MarketDateTime",
			"OfficialTimestamp", "ExchangeTimestamp", "LastPriceMarketTime",
			"LastPriceMarketDateTime");
		MarketDate = Find(names, "MarketDate", "ExchangeDate", "OfficialDate");
		MarketTime = Find(names, "MarketTime", "ExchangeTime", "OfficialTime");
		ServerTimestamp = Find(names, "ServerTimestamp", "SrvTimestamp", "ServerDateTime",
			"FeedTimestamp", "LastPriceServerTime", "LastPriceServerDateTime");
		ServerDate = Find(names, "ServerDate", "FeedDate");
		ServerTime = Find(names, "ServerTime", "FeedTime");
		CaptureTimestamp = Find(names, "CaptureTimestamp", "ClientTimestamp",
			"CaptureDateTime", "ClientDateTime", "ReceptionTimestamp");
		CaptureDate = Find(names, "CaptureDate", "ClientDate", "ReceptionDate");
		CaptureTime = Find(names, "CaptureTime", "ClientTime", "ReceptionTime");
		Timestamp = Find(names, "Timestamp", "DateTime", "EventTimestamp");
		Date = Find(names, "Date", "EventDate", "TradeDate");
		Time = Find(names, "Time", "EventTime", "TradeTime");

		EventType = Find(names, "EventType", "Event", "UpdateType", "MessageType");
		Side = Find(names, "Side", "BookSide", "BidAsk", "BuySell");
		Level = Find(names, "Level", "DepthLevel", "Position", "BookPosition");
		Action = Find(names, "Action", "UpdateAction", "ChangeAction");
		Price = Find(names, "Price", "EventPrice", "QuotePrice");
		Quantity = Find(names, "Quantity", "Size", "EventQuantity", "QuoteSize");
		OrderCount = Find(names, "OrderCount", "Orders", "NumberOfOrders");
		Sequence = Find(names, "Sequence", "SequenceNumber", "SeqNum");

		BidPrice = Find(names, "BidPrice", "BestBidPrice", "Bid");
		BidSize = Find(names, "BidSize", "BidQuantity", "BestBidSize", "BidVolume");
		BidOrderCount = Find(names, "BidOrderCount", "BidOrders", "BestBidOrderCount");
		AskPrice = Find(names, "AskPrice", "BestAskPrice", "OfferPrice", "Ask");
		AskSize = Find(names, "AskSize", "AskQuantity", "BestAskSize", "AskVolume",
			"OfferSize");
		AskOrderCount = Find(names, "AskOrderCount", "AskOrders", "BestAskOrderCount");
		LastTradePrice = Find(names, "LastTradePrice", "TradePrice", "LastPrice");
		LastTradeQuantity = Find(names, "LastTradeQty", "LastTradeQuantity", "TradeQuantity",
			"LastSize");
		TradingStatus = Find(names, "TradingStatus", "MarketStatus", "SecurityStatus");
		Open = Find(names, "DailyOpeningPrice", "OpenPrice", "Open");
		High = Find(names, "DailyHighPrice", "HighPrice", "High");
		Low = Find(names, "DailyLowPrice", "LowPrice", "Low");
		Close = Find(names, "DailyClosingPrice", "ClosePrice", "Close");
		Volume = Find(names, "DailyVolume", "CumulativeVolume", "TotalVolume", "Volume");
		OpenInterest = Find(names, "OpenInterest", "DailyOpenInterest");

		Kind = Classify();
	}

	public QuantFeedFileKinds Kind { get; }
	public int InstrumentCode { get; }
	public int Symbol { get; }
	public int Mic { get; }
	public int Name { get; }
	public int SecurityType { get; }
	public int Currency { get; }
	public int Isin { get; }
	public int Expiration { get; }
	public int Strike { get; }
	public int OptionType { get; }
	public int PriceStep { get; }
	public int Multiplier { get; }
	public int MarketTimestamp { get; }
	public int MarketDate { get; }
	public int MarketTime { get; }
	public int ServerTimestamp { get; }
	public int ServerDate { get; }
	public int ServerTime { get; }
	public int CaptureTimestamp { get; }
	public int CaptureDate { get; }
	public int CaptureTime { get; }
	public int Timestamp { get; }
	public int Date { get; }
	public int Time { get; }
	public int EventType { get; }
	public int Side { get; }
	public int Level { get; }
	public int Action { get; }
	public int Price { get; }
	public int Quantity { get; }
	public int OrderCount { get; }
	public int Sequence { get; }
	public int BidPrice { get; }
	public int BidSize { get; }
	public int BidOrderCount { get; }
	public int AskPrice { get; }
	public int AskSize { get; }
	public int AskOrderCount { get; }
	public int LastTradePrice { get; }
	public int LastTradeQuantity { get; }
	public int TradingStatus { get; }
	public int Open { get; }
	public int High { get; }
	public int Low { get; }
	public int Close { get; }
	public int Volume { get; }
	public int OpenInterest { get; }

	public static QuantFeedCsvHeader Create(string[] names)
		=> new(names ?? throw new ArgumentNullException(nameof(names)));

	private QuantFeedFileKinds Classify()
	{
		var hasKey = InstrumentCode >= 0 || Symbol >= 0;
		var hasTimestamp = MarketTimestamp >= 0 || ServerTimestamp >= 0 ||
			CaptureTimestamp >= 0 || Timestamp >= 0 ||
			MarketDate >= 0 && MarketTime >= 0 || Date >= 0 && Time >= 0;
		var hasMarketValue = EventType >= 0 || Price >= 0 || BidPrice >= 0 ||
			AskPrice >= 0 || LastTradePrice >= 0 || TradingStatus >= 0 ||
			Open >= 0 || High >= 0 || Low >= 0 || Close >= 0;
		if (hasKey && hasTimestamp && hasMarketValue)
			return QuantFeedFileKinds.MarketData;
		var hasReferenceValue = Name >= 0 || SecurityType >= 0 || Currency >= 0 ||
			Isin >= 0 || Expiration >= 0 || PriceStep >= 0 || Multiplier >= 0;
		return hasKey && hasReferenceValue
			? QuantFeedFileKinds.Reference : QuantFeedFileKinds.Unknown;
	}

	private static int Find(string[] names, params string[] candidates)
	{
		for (var i = 0; i < names.Length; i++)
		{
			var name = Normalize(names[i]);
			foreach (var candidate in candidates)
			{
				if (name.Equals(Normalize(candidate), StringComparison.OrdinalIgnoreCase))
					return i;
			}
		}
		return -1;
	}

	private static string Normalize(string value)
	{
		if (value.IsEmpty())
			return string.Empty;
		var builder = new StringBuilder(value.Length);
		foreach (var character in value.Trim().TrimStart('\uFEFF'))
		{
			if (char.IsLetterOrDigit(character))
				builder.Append(character);
		}
		var normalized = builder.ToString();
		foreach (var prefix in new[] { "Quotation", "Referential", "FeedOS" })
		{
			if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
				normalized.Length > prefix.Length)
			{
				return normalized[prefix.Length..];
			}
		}
		return normalized;
	}
}

readonly record struct QuantFeedSecurityKey(string InstrumentCode, string Symbol, string Mic)
{
	private const string Prefix = "QH1";

	public string SecurityCode => Symbol.IsEmpty(InstrumentCode);

	public string ToNative()
		=> string.Join('|', Prefix, Escape(InstrumentCode), Escape(Symbol), Escape(Mic));

	public bool Matches(QuantFeedSecurityKey other)
	{
		bool codeMatches;
		if (!InstrumentCode.IsEmpty())
		{
			codeMatches = !other.InstrumentCode.IsEmpty()
				? InstrumentCode.EqualsIgnoreCase(other.InstrumentCode)
				: InstrumentCode.EqualsIgnoreCase(other.Symbol);
		}
		else if (!Symbol.IsEmpty())
		{
			codeMatches = !other.Symbol.IsEmpty()
				? Symbol.EqualsIgnoreCase(other.Symbol)
				: Symbol.EqualsIgnoreCase(other.InstrumentCode);
		}
		else
			codeMatches = true;
		return codeMatches && (Mic.IsEmpty() || other.Mic.IsEmpty() || Mic.EqualsIgnoreCase(other.Mic));
	}

	public static bool TryParse(string value, out QuantFeedSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 4 || !parts[0].Equals(Prefix, StringComparison.Ordinal))
			return false;
		try
		{
			key = new(Unescape(parts[1]), Unescape(parts[2]), Unescape(parts[3]));
			return !key.SecurityCode.IsEmpty();
		}
		catch (UriFormatException)
		{
			return false;
		}
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string Unescape(string value)
		=> Uri.UnescapeDataString(value);
}

readonly record struct QuantFeedReferenceRow(
	string InstrumentCode,
	string Symbol,
	string Mic,
	string Name,
	string SecurityType,
	string Currency,
	string Isin,
	DateTime? Expiration,
	decimal? Strike,
	string OptionType,
	decimal? PriceStep,
	decimal? Multiplier)
{
	public QuantFeedSecurityKey ToKey()
		=> new(InstrumentCode, Symbol, Mic);
}

readonly record struct QuantFeedMarketRow(
	string InstrumentCode,
	string Symbol,
	string Mic,
	DateTime? MarketTimestamp,
	DateTime? ServerTimestamp,
	DateTime? CaptureTimestamp,
	DateTime? Timestamp,
	string EventType,
	string Side,
	int? Level,
	string Action,
	decimal? Price,
	decimal? Quantity,
	int? OrderCount,
	long? Sequence,
	decimal? BidPrice,
	decimal? BidSize,
	int? BidOrderCount,
	decimal? AskPrice,
	decimal? AskSize,
	int? AskOrderCount,
	decimal? LastTradePrice,
	decimal? LastTradeQuantity,
	string TradingStatus,
	decimal? Open,
	decimal? High,
	decimal? Low,
	decimal? Close,
	decimal? Volume,
	decimal? OpenInterest)
{
	public DateTime EventTime => MarketTimestamp ?? ServerTimestamp ?? CaptureTimestamp ??
		Timestamp ?? throw new InvalidOperationException("QuantHouse event has no timestamp.");

	public bool IsTrade => LastTradePrice != null || HasEvent("trade") || HasEvent("last");
	public bool IsCancellation => HasEvent("cancel") ||
		!Action.IsEmpty() && Action.Contains("cancel", StringComparison.OrdinalIgnoreCase);
	public bool IsBid => BidPrice != null || HasEvent("bid") ||
		!IsTrade && Side is not null &&
			(Side.EqualsIgnoreCase("B") || Side.EqualsIgnoreCase("BID") ||
			 Side.EqualsIgnoreCase("BUY"));
	public bool IsAsk => AskPrice != null || HasEvent("ask") || HasEvent("offer") ||
		!IsTrade && Side is not null &&
			(Side.EqualsIgnoreCase("A") || Side.EqualsIgnoreCase("ASK") ||
			 Side.EqualsIgnoreCase("OFFER") || Side.EqualsIgnoreCase("S") ||
			 Side.EqualsIgnoreCase("SELL"));
	public decimal? TradePrice => LastTradePrice ?? (IsTrade ? Price : null);
	public decimal? TradeQuantity => LastTradeQuantity ?? (IsTrade ? Quantity : null);

	public QuantFeedSecurityKey ToKey()
		=> new(InstrumentCode, Symbol, Mic);

	private bool HasEvent(string value)
		=> !EventType.IsEmpty() && EventType.Contains(value, StringComparison.OrdinalIgnoreCase);
}

readonly record struct QuantFeedDepthLevel(decimal Price, decimal Volume, int? OrderCount);
