namespace StockSharp.Quodd;

static class QuoddExtensions
{
	public const string BoardCode = "QUODD";
	public const string OptionsBoardCode = "QUODD_OPT";

	private static readonly TimeZoneInfo _easternTimeZone = ResolveEasternTimeZone();

	public static string ToHeader(this QuoddAssetTypes assetType)
		=> assetType switch
		{
			QuoddAssetTypes.Equities => "Equities",
			QuoddAssetTypes.Options => "Options",
			_ => throw new ArgumentOutOfRangeException(nameof(assetType), assetType, null),
		};

	public static QuoddAssetTypes GetQuoddAssetType(this SecurityId securityId)
		=> securityId.BoardCode.EqualsIgnoreCase(OptionsBoardCode)
			? QuoddAssetTypes.Options : QuoddAssetTypes.Equities;

	public static string GetQuoddTicker(this SecurityId securityId)
	{
		var ticker = (securityId.Native as string).IsEmpty(securityId.SecurityCode)?.Trim();
		return ticker.ThrowIfEmpty(nameof(securityId.SecurityCode));
	}

	public static SecurityId NormalizeQuodd(this SecurityId securityId, string ticker,
		QuoddAssetTypes assetType)
	{
		securityId.SecurityCode = securityId.SecurityCode.IsEmpty(ticker);
		securityId.BoardCode = assetType == QuoddAssetTypes.Options
			? OptionsBoardCode : securityId.BoardCode.IsEmpty(BoardCode);
		securityId.Native = ticker;
		return securityId;
	}

	public static SecurityMessage ToSecurityMessage(this SnapMessage snap,
		QuoddAssetTypes assetType, TickerInfoMessage info, long originalTransactionId)
	{
		var ticker = snap.Ticker.ThrowIfEmpty(nameof(snap.Ticker));
		var isOption = assetType == QuoddAssetTypes.Options;
		var securityId = new SecurityId
		{
			SecurityCode = ticker,
			BoardCode = isOption ? OptionsBoardCode : BoardCode,
			Native = ticker,
		};
		var underlying = snap.UnderlyingTicker;
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			Name = (info?.Name).IsEmpty(ticker),
			ShortName = ticker,
			Class = snap.ListingMarket.IsEmpty(info?.Sector),
			SecurityType = isOption ? SecurityTypes.Option : SecurityTypes.Stock,
			Currency = ToCurrency(snap.Currency),
			IssueSize = info?.SharesOutstandingWeightedAdj is > 0
				? info.SharesOutstandingWeightedAdj : null,
			ExpiryDate = isOption ? ParseDate(snap.ExpirationDate) : null,
			Strike = isOption ? Positive(ParseDecimal(snap.StrikePrice)) : null,
			OptionType = isOption ? ToOptionType(snap.OptionType) : null,
			UnderlyingSecurityId = isOption && !underlying.IsEmpty()
				? new SecurityId
				{
					SecurityCode = underlying,
					BoardCode = BoardCode,
					Native = underlying,
				}
				: default,
		};
	}

	public static SecurityMessage ToSecurityMessage(
		this global::StockSharp.Quodd.Native.Grpc.Options.Option option,
		long originalTransactionId)
	{
		var ticker = option.Ticker.ThrowIfEmpty(nameof(option.Ticker));
		var underlying = option.UnderlyingTicker;
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new()
			{
				SecurityCode = ticker,
				BoardCode = OptionsBoardCode,
				Native = ticker,
			},
			Name = ticker,
			ShortName = ticker,
			SecurityType = SecurityTypes.Option,
			ExpiryDate = ParseDate(option.ExpirationDate),
			Strike = option.StrikePrice > 0 ? (decimal)option.StrikePrice : null,
			OptionType = ToOptionType(option.OptionType),
			UnderlyingSecurityId = underlying.IsEmpty()
				? default
				: new SecurityId
				{
					SecurityCode = underlying,
					BoardCode = BoardCode,
					Native = underlying,
				},
		};
	}

	public static Level1ChangeMessage ToLevel1Message(this SnapMessage snap,
		long originalTransactionId, SecurityId securityId)
	{
		var lastTime = ParseTimestamp(snap.LastTimestamp);
		var quoteTime = ParseTimestamp(snap.QuoteTimestamp);
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			ServerTime = quoteTime ?? lastTime ?? DateTime.UtcNow,
		}
		.TryAdd(Level1Fields.BestAskPrice, Positive(ParseDecimal(snap.Ask)))
		.TryAdd(Level1Fields.BestAskVolume, Positive(ParseDecimal(snap.AskSize)))
		.TryAdd(Level1Fields.BestBidPrice, Positive(ParseDecimal(snap.Bid)))
		.TryAdd(Level1Fields.BestBidVolume, Positive(ParseDecimal(snap.BidSize)))
		.TryAdd(Level1Fields.OpenPrice, Positive(ParseDecimal(snap.Open)))
		.TryAdd(Level1Fields.HighPrice, Positive(ParseDecimal(snap.High)))
		.TryAdd(Level1Fields.LowPrice, Positive(ParseDecimal(snap.Low)))
		.TryAdd(Level1Fields.SettlementPrice, Positive(ParseDecimal(snap.PreviousClose)))
		.TryAdd(Level1Fields.LastTradePrice, Positive(ParseDecimal(snap.Last)))
		.TryAdd(Level1Fields.LastTradeVolume, Positive(ParseDecimal(snap.LastSize)))
		.TryAdd(Level1Fields.LastTradeTime, lastTime)
		.TryAdd(Level1Fields.TradesCount, NonNegative(ParseLong(snap.NumberOfTrades)))
		.TryAdd(Level1Fields.Volume, NonNegative(ParseDecimal(snap.TotalVolume)))
		.TryAdd(Level1Fields.AveragePrice, Positive(ParseDecimal(snap.VWAP)))
		.TryAdd(Level1Fields.HighPrice52Week, Positive(ParseDecimal(snap.YearHigh)))
		.TryAdd(Level1Fields.LowPrice52Week, Positive(ParseDecimal(snap.YearLow)))
		.TryAdd(Level1Fields.Change, ParseDecimal(snap.ChangePct))
		.TryAdd(Level1Fields.State, ToSecurityState(snap.TradingStatus));
		return message;
	}

	public static DateTime? ParseDate(string value)
	{
		if (value.IsEmpty())
			return null;
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var result))
			return null;
		return DateTime.SpecifyKind(result.Date, DateTimeKind.Utc);
	}

	private static DateTime? ParseTimestamp(string value)
	{
		if (value.IsEmpty())
			return null;

		var text = value.Trim();
		var offsetIndex = Math.Max(text.LastIndexOf('+'), text.LastIndexOf('-'));
		if ((text.EndsWith('Z') || offsetIndex > 9) &&
			DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces, out var offsetValue))
		{
			return offsetValue.UtcDateTime;
		}

		if (!DateTime.TryParse(text, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var localValue))
			return null;
		localValue = DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified);
		return TimeZoneInfo.ConvertTimeToUtc(localValue, _easternTimeZone);
	}

	private static decimal? ParseDecimal(string value)
	{
		if (value.IsEmpty())
			return null;
		var text = value.Trim().TrimEnd('%');
		return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var result) ? result : null;
	}

	private static long? ParseLong(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var result) ? result : null;

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static decimal? NonNegative(decimal? value)
		=> value is >= 0 ? value : null;

	private static long? NonNegative(long? value)
		=> value is >= 0 ? value : null;

	private static CurrencyTypes? ToCurrency(string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	private static OptionTypes? ToOptionType(string value)
		=> value.EqualsIgnoreCase("C") || value.EqualsIgnoreCase("CALL")
			? OptionTypes.Call
			: value.EqualsIgnoreCase("P") || value.EqualsIgnoreCase("PUT")
				? OptionTypes.Put : null;

	private static SecurityStates? ToSecurityState(string value)
	{
		if (value.IsEmpty())
			return null;
		var normalized = new string(value.Where(char.IsLetterOrDigit)
			.Select(char.ToLowerInvariant).ToArray());
		if (normalized.Contains("halt", StringComparison.Ordinal) ||
			normalized.Contains("suspend", StringComparison.Ordinal) ||
			normalized is "closed" or "stopped" or "nottrading")
			return SecurityStates.Stoped;
		if (normalized.Contains("trad", StringComparison.Ordinal) ||
			normalized is "open" or "continuous")
			return SecurityStates.Trading;
		return null;
	}

	private static TimeZoneInfo ResolveEasternTimeZone()
	{
		foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(id);
			}
			catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
			{
			}
		}
		throw new TimeZoneNotFoundException("The US Eastern time zone is not available on this system.");
	}
}
