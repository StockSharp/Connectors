namespace StockSharp.AlgoSeek.Native.Model;

sealed class AlgoSeekCsvRow(string[] values, long lineNumber)
{
	private readonly string[] _values = values;

	public long LineNumber { get; } = lineNumber;

	public string Get(int index)
		=> index >= 0 && index < _values.Length ? _values[index]?.Trim() : null;
}

sealed class AlgoSeekCsvHeader
{
	private AlgoSeekCsvHeader(string[] names)
	{
		Date = Find(names, "Date");
		Timestamp = Find(names, "Timestamp");
		EventType = Find(names, "EventType");
		Ticker = Find(names, "Ticker");
		Price = Find(names, "Price");
		Quantity = Find(names, "Quantity");
		Exchange = Find(names, "Exchange");
		Conditions = Find(names, "Conditions");
		CallPut = Find(names, "CallPut");
		Strike = Find(names, "StrikePrice", "Strike");
		ExpirationDate = Find(names, "ExpirationDate");
		Side = Find(names, "Side");
		Action = Find(names, "Action");
		UnderBidPrice = Find(names, "UnderBidPrice");
		UnderAskPrice = Find(names, "UnderAskPrice");

		UtcDate = Find(names, "UTCDate");
		UtcTime = Find(names, "UTCTime");
		LocalDate = Find(names, "LocalDate");
		LocalTime = Find(names, "LocalTime");
		Month = Find(names, "Month");
		ExpirationYear = Find(names, "ExpirationYear");
		SecurityId = Find(names, "SecurityID", "SecurityId");
		TypeMask = Find(names, "TypeMask");
		Type = Find(names, "Type");
		Orders = Find(names, "Orders");
		Flags = Find(names, "Flags");

		TimeBarStart = Find(names, "TimeBarStart");
		FirstTradePrice = Find(names, "FirstTradePrice", "OpenTradePrice");
		HighTradePrice = Find(names, "HighTradePrice");
		LowTradePrice = Find(names, "LowTradePrice");
		LastTradePrice = Find(names, "LastTradePrice", "CloseTradePrice");
		VolumeWeightPrice = Find(names, "VolumeWeightPrice");
		Volume = Find(names, "Volume");
		TotalTrades = Find(names, "TotalTrades");
		OpenBidPrice = Find(names, "OpenBidPrice");
		OpenBidSize = Find(names, "OpenBidSize");
		OpenAskPrice = Find(names, "OpenAskPrice");
		OpenAskSize = Find(names, "OpenAskSize");
		CloseBidTime = Find(names, "CloseBidTime");
		CloseBidPrice = Find(names, "CloseBidPrice");
		CloseBidSize = Find(names, "CloseBidSize");
		CloseAskTime = Find(names, "CloseAskTime");
		CloseAskPrice = Find(names, "CloseAskPrice");
		CloseAskSize = Find(names, "CloseAskSize");

		TradeDate = Find(names, "TradeDate");
		SecId = Find(names, "SecId", "SecurityID", "SecurityId");
		Open = Find(names, "Open");
		High = Find(names, "High");
		Low = Find(names, "Low");
		Close = Find(names, "Close");
		MarketHoursVolume = Find(names, "MarketHoursVolume", "Volume");

		Kind = Classify();
	}

	public AlgoSeekFileKinds Kind { get; }
	public int Date { get; }
	public int Timestamp { get; }
	public int EventType { get; }
	public int Ticker { get; }
	public int Price { get; }
	public int Quantity { get; }
	public int Exchange { get; }
	public int Conditions { get; }
	public int CallPut { get; }
	public int Strike { get; }
	public int ExpirationDate { get; }
	public int Side { get; }
	public int Action { get; }
	public int UnderBidPrice { get; }
	public int UnderAskPrice { get; }
	public int UtcDate { get; }
	public int UtcTime { get; }
	public int LocalDate { get; }
	public int LocalTime { get; }
	public int Month { get; }
	public int ExpirationYear { get; }
	public int SecurityId { get; }
	public int TypeMask { get; }
	public int Type { get; }
	public int Orders { get; }
	public int Flags { get; }
	public int TimeBarStart { get; }
	public int FirstTradePrice { get; }
	public int HighTradePrice { get; }
	public int LowTradePrice { get; }
	public int LastTradePrice { get; }
	public int VolumeWeightPrice { get; }
	public int Volume { get; }
	public int TotalTrades { get; }
	public int OpenBidPrice { get; }
	public int OpenBidSize { get; }
	public int OpenAskPrice { get; }
	public int OpenAskSize { get; }
	public int CloseBidTime { get; }
	public int CloseBidPrice { get; }
	public int CloseBidSize { get; }
	public int CloseAskTime { get; }
	public int CloseAskPrice { get; }
	public int CloseAskSize { get; }
	public int TradeDate { get; }
	public int SecId { get; }
	public int Open { get; }
	public int High { get; }
	public int Low { get; }
	public int Close { get; }
	public int MarketHoursVolume { get; }

	public static AlgoSeekCsvHeader Create(string[] names)
		=> new(names ?? throw new ArgumentNullException(nameof(names)));

	private AlgoSeekFileKinds Classify()
	{
		if (UtcDate >= 0 && UtcTime >= 0 && Ticker >= 0 && TypeMask >= 0 &&
			Type >= 0 && Price >= 0 && Quantity >= 0)
		{
			return AlgoSeekFileKinds.FuturesTick;
		}
		if (Date >= 0 && TimeBarStart >= 0 && Ticker >= 0 && CallPut >= 0 &&
			Strike >= 0 && ExpirationDate >= 0 && (FirstTradePrice >= 0 ||
			OpenBidPrice >= 0 || CloseBidPrice >= 0))
		{
			return AlgoSeekFileKinds.OptionMinute;
		}
		if (Date >= 0 && Timestamp >= 0 && Ticker >= 0 && CallPut >= 0 &&
			Strike >= 0 && ExpirationDate >= 0 && Action >= 0 && Price >= 0 &&
			Quantity >= 0)
		{
			return AlgoSeekFileKinds.OptionTick;
		}
		if (Date >= 0 && TimeBarStart >= 0 && Ticker >= 0 &&
			FirstTradePrice >= 0 && HighTradePrice >= 0 && LowTradePrice >= 0 &&
			LastTradePrice >= 0 && Volume >= 0)
		{
			return AlgoSeekFileKinds.EquityMinute;
		}
		if (TradeDate >= 0 && Ticker >= 0 && Open >= 0 && High >= 0 &&
			Low >= 0 && Close >= 0 && MarketHoursVolume >= 0)
		{
			return AlgoSeekFileKinds.EquityDaily;
		}
		if (Date >= 0 && Timestamp >= 0 && EventType >= 0 && Ticker >= 0 &&
			Price >= 0 && Quantity >= 0)
		{
			return AlgoSeekFileKinds.EquityTick;
		}
		return AlgoSeekFileKinds.Unknown;
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
		return builder.ToString();
	}
}

readonly record struct AlgoSeekEquityTickRow(
	DateTime Date,
	string Timestamp,
	string EventType,
	string Ticker,
	decimal Price,
	long Quantity,
	string Exchange,
	string Conditions)
{
	public bool IsTrade => EventType.StartsWith("TRADE", StringComparison.OrdinalIgnoreCase);
	public bool IsCancellation => EventType.Contains("CANCEL", StringComparison.OrdinalIgnoreCase);
	public bool IsNationalBest => EventType.EndsWith(" NB", StringComparison.OrdinalIgnoreCase) ||
		EventType.Contains(" NB ", StringComparison.OrdinalIgnoreCase);
	public bool IsBid => EventType.StartsWith("QUOTE BID", StringComparison.OrdinalIgnoreCase);
	public bool IsAsk => EventType.StartsWith("QUOTE ASK", StringComparison.OrdinalIgnoreCase);
}

readonly record struct AlgoSeekOptionTickRow(
	DateTime Date,
	string Timestamp,
	string Ticker,
	OptionTypes OptionType,
	decimal Strike,
	DateTime Expiration,
	int EventType,
	string Side,
	string Action,
	decimal Price,
	long Quantity,
	string Exchange,
	string Conditions,
	decimal? UnderBidPrice,
	decimal? UnderAskPrice)
{
	public bool IsTrade => (HasAction("T") || HasAction("C")) && !HasAction("TI");
	public bool IsCancellation => HasAction("C") || (EventType & 16) != 0;
	public bool IsOpenInterest => HasAction("OI") || (EventType & 15) == 2;
	public bool IsQuote => HasAction("FQ") || HasAction("TI") || HasAction("NQ") ||
		(EventType & 15) is 3 or 4 or 5 or 8;
	public bool IsBid => Side.EqualsIgnoreCase("B") || ((EventType & 128) != 0 && IsQuote);
	public bool IsAsk => Side.EqualsIgnoreCase("S") || Side.EqualsIgnoreCase("A") ||
		((EventType & 128) == 0 && IsQuote);

	private bool HasAction(string value)
		=> Action?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Any(part => part.Equals(value, StringComparison.OrdinalIgnoreCase)) == true;
}

readonly record struct AlgoSeekFuturesTickRow(
	DateTime UtcDate,
	string UtcTime,
	DateTime LocalDate,
	string LocalTime,
	string Ticker,
	OptionTypes? OptionType,
	decimal? Strike,
	string Month,
	int? ExpirationYear,
	string SecurityId,
	int TypeMask,
	string Type,
	decimal Price,
	long Quantity,
	int? Orders,
	int Flags)
{
	public int MessageType => TypeMask & 31;
	public bool IsTrade => MessageType == 2 || Type.StartsWith("TRADE", StringComparison.OrdinalIgnoreCase);
	public bool IsQuote => MessageType == 1 || Type.StartsWith("QUOTE", StringComparison.OrdinalIgnoreCase);
	public bool IsBid => IsQuote && ((TypeMask & 128) != 0 || Type.Contains("BID", StringComparison.OrdinalIgnoreCase));
	public bool IsAsk => IsQuote && ((TypeMask & 64) != 0 || Type.Contains("SELL", StringComparison.OrdinalIgnoreCase) ||
		Type.Contains("ASK", StringComparison.OrdinalIgnoreCase));
	public bool IsOpenInterest => MessageType == 11 || Type.Contains("OPEN INTEREST", StringComparison.OrdinalIgnoreCase);
	public bool IsEmptyBook => MessageType == 12 || Type.Contains("EMPTY BOOK", StringComparison.OrdinalIgnoreCase);
	public Sides? OriginSide => !IsTrade ? null : (TypeMask & 128) != 0 ? Sides.Buy :
		(TypeMask & 64) != 0 ? Sides.Sell : null;
}

readonly record struct AlgoSeekEquityMinuteRow(
	DateTime Date,
	string Ticker,
	string TimeBarStart,
	decimal Open,
	decimal High,
	decimal Low,
	decimal Close,
	decimal? Vwap,
	long Volume,
	long? TotalTrades);

readonly record struct AlgoSeekOptionMinuteRow(
	DateTime Date,
	string TimeBarStart,
	string Ticker,
	OptionTypes OptionType,
	decimal Strike,
	DateTime Expiration,
	decimal? OpenTradePrice,
	decimal? HighTradePrice,
	decimal? LowTradePrice,
	decimal? CloseTradePrice,
	long Volume,
	long? TotalTrades,
	string CloseBidTime,
	decimal? CloseBidPrice,
	long? CloseBidSize,
	string CloseAskTime,
	decimal? CloseAskPrice,
	long? CloseAskSize);

readonly record struct AlgoSeekEquityDailyRow(
	DateTime Date,
	string SecurityId,
	string Ticker,
	decimal Open,
	decimal High,
	decimal Low,
	decimal Close,
	long Volume);
