namespace StockSharp.OptionMetrics.Native.Model;

readonly ref struct IvyDbFields
{
	private readonly ReadOnlySpan<char> _line;

	public IvyDbFields(string line)
	{
		_line = line.AsSpan();
	}

	public int Count
	{
		get
		{
			if (_line.IsEmpty)
				return 0;
			var count = 1;
			foreach (var character in _line)
			{
				if (character == '\t')
					count++;
			}
			return count;
		}
	}

	public string Text(int index)
		=> Field(index).Trim().ToString();

	public char? Character(int index)
	{
		var field = Field(index).Trim();
		return field.IsEmpty ? null : field[0];
	}

	public long RequiredLong(int index, string name)
		=> Long(index) ?? throw new FormatException($"Missing {name}.");

	public long? Long(int index)
	{
		var field = Field(index).Trim();
		if (field.IsEmpty)
			return null;
		if (!long.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var value))
		{
			throw new FormatException($"Invalid integer in column {index + 1}.");
		}
		return value;
	}

	public int? Integer(int index)
	{
		var value = Long(index);
		return value == null ? null : checked((int)value.Value);
	}

	public decimal? Decimal(int index)
	{
		var field = Field(index).Trim();
		if (field.IsEmpty)
			return null;
		if (!decimal.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var value))
		{
			throw new FormatException($"Invalid decimal in column {index + 1}.");
		}
		return value;
	}

	public DateTime RequiredDate(int index, string name)
		=> Date(index) ?? throw new FormatException($"Missing {name}.");

	public DateTime? Date(int index)
	{
		var field = Field(index).Trim();
		if (field.IsEmpty || field.SequenceEqual("0"))
			return null;
		if (!DateTime.TryParseExact(field, "yyyyMMdd", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var value))
		{
			throw new FormatException($"Invalid date in column {index + 1}.");
		}
		return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
	}

	private ReadOnlySpan<char> Field(int index)
	{
		if (index < 0)
			return default;

		var current = 0;
		var start = 0;
		for (var position = 0; position <= _line.Length; position++)
		{
			if (position < _line.Length && _line[position] != '\t')
				continue;
			if (current == index)
				return _line[start..position];
			current++;
			start = position + 1;
		}
		return default;
	}
}

sealed record IvyDbSecurityRow(
	long SecurityId,
	string Cusip,
	string Ticker,
	string ClassCode,
	string Sic,
	bool IsIndex,
	int ExchangeFlags,
	IvyDbIssueTypes IssueType,
	string IndustryGroup)
{
	public static IvyDbSecurityRow Parse(string line)
	{
		var fields = new IvyDbFields(line);
		if (fields.Count < 9)
			throw new FormatException("IvyDB Security rows require nine columns.");
		return new(
			fields.RequiredLong(0, "Security ID"),
			fields.Text(1),
			fields.Text(2),
			fields.Text(6),
			fields.Text(3),
			IsTrue(fields.Text(4)),
			fields.Integer(5) ?? 0,
			ParseIssueType(fields.Character(7)),
			fields.Text(8));
	}

	private static IvyDbIssueTypes ParseIssueType(char? value)
		=> value switch
		{
			'0' => IvyDbIssueTypes.CommonStock,
			'A' or 'a' => IvyDbIssueTypes.Index,
			'7' => IvyDbIssueTypes.Fund,
			'F' or 'f' => IvyDbIssueTypes.DepositaryReceipt,
			'%' => IvyDbIssueTypes.ExchangeTradedFund,
			'S' or 's' => IvyDbIssueTypes.StructuredProduct,
			'U' or 'u' => IvyDbIssueTypes.Unit,
			_ => IvyDbIssueTypes.Unknown,
		};

	private static bool IsTrue(string value)
		=> value == "1" || value.EqualsIgnoreCase("Y") || value.EqualsIgnoreCase("TRUE");
}

sealed record IvyDbSecurityNameRow(
	long SecurityId,
	DateTime EffectiveDate,
	string Cusip,
	string Ticker,
	string ClassCode,
	string IssuerDescription,
	string IssueDescription,
	string Sic)
{
	public static IvyDbSecurityNameRow Parse(string line)
	{
		var fields = new IvyDbFields(line);
		if (fields.Count < 8)
			throw new FormatException("IvyDB Security Name rows require eight columns.");
		return new(
			fields.RequiredLong(0, "Security ID"),
			fields.RequiredDate(1, "effective date"),
			fields.Text(2),
			fields.Text(3),
			fields.Text(4),
			fields.Text(5),
			fields.Text(6),
			fields.Text(7));
	}

	public string GetDescription()
		=> string.Join(' ', new[] { IssuerDescription, IssueDescription }
			.Where(value => !value.IsEmpty())).Trim();

	public string GetSymbol()
	{
		var ticker = Ticker?.Trim().ToUpperInvariant();
		var classCode = ClassCode?.Trim().ToUpperInvariant();
		if (ticker.IsEmpty() || classCode.IsEmpty() ||
			ticker.EndsWith('.' + classCode, StringComparison.OrdinalIgnoreCase))
		{
			return ticker;
		}
		return $"{ticker}.{classCode}";
	}
}

sealed record IvyDbSecurityPriceRow(
	long SecurityId,
	DateTime Date,
	decimal? BidOrLow,
	decimal? AskOrHigh,
	decimal? Close,
	long? Volume,
	decimal? TotalReturn,
	decimal? AdjustmentFactor,
	decimal? Open,
	long? SharesOutstanding,
	decimal? TotalReturnAdjustmentFactor)
{
	public static IvyDbSecurityPriceRow Parse(string line)
	{
		var fields = new IvyDbFields(line);
		if (fields.Count < 2)
			throw new FormatException("IvyDB Security Price rows require an ID and date.");
		return new(
			fields.RequiredLong(0, "Security ID"),
			fields.RequiredDate(1, "trade date"),
			fields.Decimal(2),
			fields.Decimal(3),
			fields.Decimal(4),
			fields.Long(5),
			fields.Decimal(6),
			fields.Decimal(7),
			fields.Decimal(8),
			fields.Long(9),
			fields.Decimal(10));
	}
}

sealed record IvyDbOptionPriceRow(
	long SecurityId,
	DateTime Date,
	string Symbol,
	IvyDbSymbolFormats SymbolFormat,
	decimal Strike,
	DateTime Expiration,
	OptionTypes OptionType,
	decimal? Bid,
	decimal? Ask,
	DateTime? LastTradeDate,
	long? Volume,
	long? OpenInterest,
	string SpecialSettlement,
	decimal? ImpliedVolatility,
	decimal? Delta,
	decimal? Gamma,
	decimal? Vega,
	decimal? Theta,
	long OptionId,
	decimal? AdjustmentFactor,
	bool? IsAmSettlement,
	decimal? ContractSize,
	string ExpiryIndicator)
{
	public static IvyDbOptionPriceRow Parse(string line)
	{
		var fields = new IvyDbFields(line);
		if (fields.Count < 19)
			throw new FormatException("IvyDB Option Price rows require at least 19 columns.");
		var callPut = char.ToUpperInvariant(fields.Character(6) ?? '\0');
		var optionType = callPut switch
		{
			'C' => OptionTypes.Call,
			'P' => OptionTypes.Put,
			_ => throw new FormatException("Invalid call/put indicator."),
		};
		var strike = fields.RequiredLong(4, "scaled strike") / 1000m;
		if (strike < 0)
			throw new FormatException("Option strike cannot be negative.");
		var symbol = fields.Text(2);
		if (symbol.IsEmpty())
			throw new FormatException("Missing option symbol.");

		return new(
			fields.RequiredLong(0, "Security ID"),
			fields.RequiredDate(1, "trade date"),
			symbol,
			ParseSymbolFormat(fields.Character(3)),
			strike,
			fields.RequiredDate(5, "expiration date"),
			optionType,
			PositiveOrZero(fields.Decimal(7)),
			PositiveOrZero(fields.Decimal(8)),
			fields.Date(9),
			NonNegative(fields.Long(10)),
			NonNegative(fields.Long(11)),
			fields.Text(12),
			Metric(fields.Decimal(13)),
			Metric(fields.Decimal(14)),
			Metric(fields.Decimal(15)),
			Metric(fields.Decimal(16)),
			Metric(fields.Decimal(17)),
			fields.RequiredLong(18, "Option ID"),
			fields.Decimal(19),
			ParseBoolean(fields.Text(20)),
			Positive(fields.Decimal(21)),
			fields.Text(22));
	}

	public bool MatchesSymbol(string value)
	{
		if (value.IsEmpty())
			return true;
		value = value.Trim();
		return Symbol.EqualsIgnoreCase(value) ||
			Symbol.Replace(" ", string.Empty).EqualsIgnoreCase(
				value.Replace(" ", string.Empty));
	}

	private static IvyDbSymbolFormats ParseSymbolFormat(char? value)
		=> value switch
		{
			'0' => IvyDbSymbolFormats.Legacy,
			'1' => IvyDbSymbolFormats.Osi,
			_ => IvyDbSymbolFormats.Unknown,
		};

	private static bool? ParseBoolean(string value)
		=> value.IsEmpty() ? null : value == "1" || value.EqualsIgnoreCase("Y")
			? true : value == "0" || value.EqualsIgnoreCase("N") ? false : null;

	private static decimal? Metric(decimal? value)
		=> value is <= -99m ? null : value;

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static decimal? PositiveOrZero(decimal? value)
		=> value is >= 0 ? value : null;

	private static long? NonNegative(long? value)
		=> value is >= 0 ? value : null;
}
