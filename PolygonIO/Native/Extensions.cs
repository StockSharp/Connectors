namespace StockSharp.PolygonIO.Native;

static class Extensions
{
	private static readonly Dictionary<string, SecurityTypes?> _secTypesMap = new(StringComparer.InvariantCultureIgnoreCase)
	{
		{ "CS", SecurityTypes.Stock }, // Common Stock
		{ "PFD", SecurityTypes.Stock }, // Preferred Stock
		{ "WARRANT", SecurityTypes.Warrant },
		{ "RIGHT", SecurityTypes.Receipt },
		{ "BOND", SecurityTypes.Bond },
		{ "ETF", SecurityTypes.Etf },
		{ "ETN", SecurityTypes.Etf }, // Exchange Traded Note
		{ "ETV", SecurityTypes.Commodity }, // Exchange Traded Vehicle
		{ "SP", SecurityTypes.MultiLeg }, // Structured Product
		{ "ADRC", SecurityTypes.Adr }, // American Depository Receipt Common
		{ "ADRP", SecurityTypes.Adr }, // American Depository Receipt Preferred
		{ "ADRW", SecurityTypes.Warrant }, // American Depository Receipt Warrants
		{ "ADRR", SecurityTypes.Receipt }, // American Depository Receipt Rights
		{ "FUND", SecurityTypes.Fund },
		{ "BASKET", SecurityTypes.MultiLeg },
		{ "UNIT", SecurityTypes.Indicator },
		{ "LT", SecurityTypes.Fund }, // Liquidating Trust
		{ "OS", SecurityTypes.Stock }, // Ordinary Shares
		{ "GDR", SecurityTypes.Gdr },
		{ "OTHER", null },
		{ "NYRS", SecurityTypes.Stock }, // New York Registry Shares
		{ "AGEN", SecurityTypes.Bond }, // Agency Bond
		{ "EQLK", SecurityTypes.Bond }, // Equity Linked Bond
		{ "ETS", SecurityTypes.Etf }, // Single-security ETF
		{ "indices", SecurityTypes.Index }
	};

	public static SecurityTypes? ToSecurityType(this string type)
	{
		if (_secTypesMap.TryGetValue(type, out var secType))
			return secType;

		return null;
	}

	public static string ToNative(this SecurityTypes secType)
		=> _secTypesMap.FirstOrDefault(p => p.Value == secType).Key;

	public static string ToNative(this TimeSpan timeFrame, out int multiplier)
	{
		if (timeFrame.TotalSeconds < 1)
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
		else if (timeFrame.TotalSeconds < 1)
		{
			multiplier = (int)timeFrame.TotalSeconds;
			return "second";
		}
		else if (timeFrame.TotalHours < 1)
		{
			multiplier = (int)timeFrame.TotalMinutes;
			return "minute";
		}
		else if (timeFrame.TotalDays < 1)
		{
			multiplier = (int)timeFrame.TotalHours;
			return "hour";
		}
		else if (timeFrame.TotalWeeks() < 1)
		{
			multiplier = (int)timeFrame.TotalDays;
			return "day";
		}
		else if (timeFrame.TotalMonths() < 1)
		{
			multiplier = (int)timeFrame.TotalWeeks();
			return "week";
		}
		else if (timeFrame.TotalYears() < 1)
		{
			multiplier = (int)timeFrame.TotalMonths();
			return "month";
		}
		else
		{
			multiplier = (int)timeFrame.TotalYears();
			return "year";
		}
	}
}