namespace StockSharp.Exegy.Native.Model;

enum ExegyFileKinds
{
	Unknown,
	Reference,
	MarketData,
}

sealed class ExegyCsvRow(string[] values, long lineNumber)
{
	private readonly string[] _values = values ?? throw new ArgumentNullException(nameof(values));

	public long LineNumber { get; } = lineNumber;

	public string Get(int index)
		=> index >= 0 && index < _values.Length ? _values[index]?.Trim() : null;
}

sealed class ExegyCsvHeader
{
	private ExegyCsvHeader(string[] names)
	{
		InstrumentId = Find(names, "InstrumentId", "InstrumentID", "SecurityId",
			"SecurityID", "ProductId", "ProductID", "NativeId");
		Symbol = Find(names, "Symbol", "Ticker", "LocalCode", "SecurityCode");
		Venue = Find(names, "Venue", "Exchange", "MIC", "Market", "SourceVenue");
		Name = Find(names, "InstrumentName", "SecurityName", "Name", "Description");
		SecurityType = Find(names, "InstrumentType", "SecurityType", "AssetClass",
			"AssetType");
		Currency = Find(names, "Currency", "CurrencyCode", "TradingCurrency");
		Isin = Find(names, "ISIN", "Isin");
		Expiration = Find(names, "ExpirationDate", "ExpiryDate", "MaturityDate");
		Strike = Find(names, "Strike", "StrikePrice");
		OptionType = Find(names, "OptionType", "CallPut", "PutCall");
		PriceStep = Find(names, "PriceStep", "TickSize", "MinimumPriceIncrement");
		Multiplier = Find(names, "Multiplier", "ContractMultiplier", "LotSize");

		ExchangeTimestamp = Find(names, "ExchangeTimestamp", "ExchangeTime",
			"MarketTimestamp", "EventTime", "SourceEventTime");
		SourceTimestamp = Find(names, "SourceTimestamp", "SourceTime", "FeedTimestamp",
			"ApplianceTimestamp");
		CaptureTimestamp = Find(names, "CaptureTimestamp", "CaptureTime",
			"ReceiveTimestamp", "ReceiveTime", "HardwareTimestamp");
		Timestamp = Find(names, "Timestamp", "DateTime", "TimeStamp");
		Date = Find(names, "Date", "EventDate", "TradeDate");
		Time = Find(names, "Time", "TimeOfDay");

		MessageType = Find(names, "MessageType", "EventType", "UpdateType", "Event");
		Action = Find(names, "Action", "UpdateAction", "ChangeAction", "EntryAction");
		Side = Find(names, "Side", "BookSide", "BidAsk", "BuySell");
		Level = Find(names, "Level", "DepthLevel", "Position", "BookPosition");
		Price = Find(names, "Price", "EntryPrice", "OrderPrice", "EventPrice");
		Size = Find(names, "Size", "Quantity", "EntrySize", "OrderQuantity", "Volume");
		OrderCount = Find(names, "OrderCount", "Orders", "NumberOfOrders");
		OrderId = Find(names, "OrderId", "OrderID", "EntryId", "EntryID");
		TradeId = Find(names, "TradeId", "TradeID", "ExecutionId", "ExecutionID");
		Sequence = Find(names, "Sequence", "SequenceNumber", "SeqNum", "MsgSeqNum");
		Participant = Find(names, "Participant", "ParticipantId", "MarketMaker", "BrokerCode");

		BidPrice = Find(names, "BidPrice", "BestBidPrice", "Bid");
		BidSize = Find(names, "BidSize", "BidQuantity", "BestBidSize", "BidVolume");
		BidOrderCount = Find(names, "BidOrderCount", "BidOrders", "BestBidOrderCount");
		AskPrice = Find(names, "AskPrice", "BestAskPrice", "OfferPrice", "Ask");
		AskSize = Find(names, "AskSize", "AskQuantity", "BestAskSize", "AskVolume",
			"OfferSize");
		AskOrderCount = Find(names, "AskOrderCount", "AskOrders", "BestAskOrderCount");
		TradePrice = Find(names, "TradePrice", "LastTradePrice", "LastPrice");
		TradeSize = Find(names, "TradeSize", "TradeQuantity", "LastTradeQuantity",
			"LastSize");
		TradingStatus = Find(names, "TradingStatus", "MarketStatus", "SecurityStatus");
		Open = Find(names, "OpenPrice", "DailyOpen", "Open");
		High = Find(names, "HighPrice", "DailyHigh", "High");
		Low = Find(names, "LowPrice", "DailyLow", "Low");
		Close = Find(names, "ClosePrice", "DailyClose", "Close");
		CumulativeVolume = Find(names, "CumulativeVolume", "TotalVolume", "DailyVolume");
		OpenInterest = Find(names, "OpenInterest");
		Condition = Find(names, "Condition", "QuoteCondition", "TradeCondition");

		Kind = Classify();
	}

	public ExegyFileKinds Kind { get; }
	public int InstrumentId { get; }
	public int Symbol { get; }
	public int Venue { get; }
	public int Name { get; }
	public int SecurityType { get; }
	public int Currency { get; }
	public int Isin { get; }
	public int Expiration { get; }
	public int Strike { get; }
	public int OptionType { get; }
	public int PriceStep { get; }
	public int Multiplier { get; }
	public int ExchangeTimestamp { get; }
	public int SourceTimestamp { get; }
	public int CaptureTimestamp { get; }
	public int Timestamp { get; }
	public int Date { get; }
	public int Time { get; }
	public int MessageType { get; }
	public int Action { get; }
	public int Side { get; }
	public int Level { get; }
	public int Price { get; }
	public int Size { get; }
	public int OrderCount { get; }
	public int OrderId { get; }
	public int TradeId { get; }
	public int Sequence { get; }
	public int Participant { get; }
	public int BidPrice { get; }
	public int BidSize { get; }
	public int BidOrderCount { get; }
	public int AskPrice { get; }
	public int AskSize { get; }
	public int AskOrderCount { get; }
	public int TradePrice { get; }
	public int TradeSize { get; }
	public int TradingStatus { get; }
	public int Open { get; }
	public int High { get; }
	public int Low { get; }
	public int Close { get; }
	public int CumulativeVolume { get; }
	public int OpenInterest { get; }
	public int Condition { get; }

	public static ExegyCsvHeader Create(string[] names)
		=> new(names ?? throw new ArgumentNullException(nameof(names)));

	private ExegyFileKinds Classify()
	{
		var hasKey = InstrumentId >= 0 || Symbol >= 0;
		var hasTimestamp = ExchangeTimestamp >= 0 || SourceTimestamp >= 0 ||
			CaptureTimestamp >= 0 || Timestamp >= 0 || Date >= 0 && Time >= 0;
		var hasMarket = MessageType >= 0 || Price >= 0 || BidPrice >= 0 ||
			AskPrice >= 0 || TradePrice >= 0 || OrderId >= 0 || TradingStatus >= 0 ||
			Open >= 0 || High >= 0 || Low >= 0 || Close >= 0 ||
			CumulativeVolume >= 0 || OpenInterest >= 0;
		if (hasKey && hasTimestamp && hasMarket)
			return ExegyFileKinds.MarketData;
		var hasReference = Name >= 0 || SecurityType >= 0 || Currency >= 0 ||
			Isin >= 0 || Expiration >= 0 || PriceStep >= 0 || Multiplier >= 0;
		return hasKey && hasReference ? ExegyFileKinds.Reference : ExegyFileKinds.Unknown;
	}

	private static int Find(string[] names, params string[] candidates)
	{
		for (var index = 0; index < names.Length; index++)
		{
			var name = Normalize(names[index]);
			foreach (var candidate in candidates)
			{
				if (name.Equals(Normalize(candidate), StringComparison.OrdinalIgnoreCase))
					return index;
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
		foreach (var prefix in new[] { "Exegy", "XCAPI", "MarketData", "Field" })
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

readonly record struct ExegySecurityKey(string InstrumentId, string Symbol, string Venue)
{
	private const string Prefix = "EX1";

	public string SecurityCode => Symbol.IsEmpty(InstrumentId);

	public string ToNative()
		=> string.Join('|', Prefix, Escape(InstrumentId), Escape(Symbol), Escape(Venue));

	public bool Matches(ExegySecurityKey other)
	{
		bool codeMatches;
		if (!InstrumentId.IsEmpty())
		{
			codeMatches = !other.InstrumentId.IsEmpty()
				? InstrumentId.EqualsIgnoreCase(other.InstrumentId)
				: InstrumentId.EqualsIgnoreCase(other.Symbol);
		}
		else if (!Symbol.IsEmpty())
		{
			codeMatches = !other.Symbol.IsEmpty()
				? Symbol.EqualsIgnoreCase(other.Symbol)
				: Symbol.EqualsIgnoreCase(other.InstrumentId);
		}
		else
			codeMatches = true;
		return codeMatches && (Venue.IsEmpty() || other.Venue.IsEmpty() ||
			Venue.EqualsIgnoreCase(other.Venue));
	}

	public static bool TryParse(string value, out ExegySecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 4 || !parts[0].Equals(Prefix, StringComparison.Ordinal))
			return false;
		try
		{
			key = new(Uri.UnescapeDataString(parts[1]), Uri.UnescapeDataString(parts[2]),
				Uri.UnescapeDataString(parts[3]));
			return !key.SecurityCode.IsEmpty();
		}
		catch (UriFormatException)
		{
			return false;
		}
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);
}

readonly record struct ExegyReferenceRow(
	string InstrumentId,
	string Symbol,
	string Venue,
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
	public ExegySecurityKey ToKey() => new(InstrumentId, Symbol, Venue);
}

readonly record struct ExegyMarketRow(
	string InstrumentId,
	string Symbol,
	string Venue,
	DateTime? ExchangeTimestamp,
	DateTime? SourceTimestamp,
	DateTime? CaptureTimestamp,
	DateTime? Timestamp,
	string MessageType,
	string Action,
	string Side,
	int? Level,
	decimal? Price,
	decimal? Size,
	int? OrderCount,
	string OrderId,
	string TradeId,
	long? Sequence,
	string Participant,
	decimal? BidPrice,
	decimal? BidSize,
	int? BidOrderCount,
	decimal? AskPrice,
	decimal? AskSize,
	int? AskOrderCount,
	decimal? TradePrice,
	decimal? TradeSize,
	string TradingStatus,
	decimal? Open,
	decimal? High,
	decimal? Low,
	decimal? Close,
	decimal? CumulativeVolume,
	decimal? OpenInterest,
	string Condition)
{
	public DateTime EventTime => ExchangeTimestamp ?? SourceTimestamp ?? CaptureTimestamp ??
		Timestamp ?? throw new InvalidOperationException("Exegy event has no timestamp.");
	public bool IsTrade => TradePrice != null || !TradeId.IsEmpty() || HasType("trade") ||
		HasType("execution") || IsActionCode("T", "F");
	public bool IsBid => BidPrice != null || HasType("bid") ||
		!IsTrade && IsSide("B", "BID", "BUY", "1");
	public bool IsAsk => AskPrice != null || HasType("ask") || HasType("offer") ||
		!IsTrade && IsSide("A", "ASK", "OFFER", "S", "SELL", "2");
	public bool IsCancellation => HasAction("cancel") || HasAction("delete") ||
		HasType("cancel") || IsActionCode("C", "D", "X");
	public bool IsReset => HasAction("clear") || HasAction("reset") || HasAction("empty") ||
		HasType("bookreset") || IsActionCode("R");
	public decimal? EffectiveTradePrice => TradePrice ?? (IsTrade ? Price : null);
	public decimal? EffectiveTradeSize => TradeSize ?? (IsTrade ? Size : null);

	public ExegySecurityKey ToKey() => new(InstrumentId, Symbol, Venue);

	private bool HasType(string value)
		=> !MessageType.IsEmpty() && MessageType.Contains(value, StringComparison.OrdinalIgnoreCase);
	private bool HasAction(string value)
		=> !Action.IsEmpty() && Action.Contains(value, StringComparison.OrdinalIgnoreCase);
	private bool IsActionCode(params string[] values)
	{
		foreach (var value in values)
		{
			if (Action.EqualsIgnoreCase(value))
				return true;
		}
		return false;
	}
	private bool IsSide(params string[] values)
	{
		foreach (var value in values)
		{
			if (Side.EqualsIgnoreCase(value))
				return true;
		}
		return false;
	}
}

readonly record struct ExegyDepthLevel(decimal Price, decimal Volume, int? OrderCount);
