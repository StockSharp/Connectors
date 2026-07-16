namespace StockSharp.IQFeed;

using System.Net.Sockets;

static class IQFeedHelper
{
	public static string EnsureProtocolField(this string value, string name, bool isEmptyAllowed = false)
	{
		if (!isEmptyAllowed && value.IsEmpty())
			throw new ArgumentException("An IQFeed protocol field cannot be empty.", name);
		if (value?.IndexOfAny([',', '\r', '\n']) >= 0)
			throw new ArgumentException("An IQFeed protocol field contains a reserved delimiter.", name);
		return value;
	}

	public static string EnsureProtocolLine(this string value, string name)
	{
		if (value?.IndexOfAny(['\r', '\n']) >= 0)
			throw new ArgumentException("An IQFeed command contains a line delimiter.", name);
		return value;
	}

	public static SecurityTypes? ToSecurityType(this string value)
	{
		switch (value)
		{
			case "EQUITY":
			case "SPOT":
			case "JACOBSEN":
			case "PETROCHEMWIRE":
			case "GENERICRPT":
				return SecurityTypes.Stock;
			case "FUTURE":
			case "COMBINED_FUTURE":
			case "ARGUSFC":
			case "DAILY_FUTURE":
				return SecurityTypes.Future;
			case "FORWARD":
				return SecurityTypes.Forward;
			case "IEOPTION": // Index/Equity Option
			case "FOPTION": // Future Option
			case "COMBINED_FOPTION":
			case "FOPTION_IV":
				return SecurityTypes.Option;
			case "BONDS":
			case "TREASURIES":
				return SecurityTypes.Bond;
			case "SPREAD": // Future Spread
			case "STRATSPREAD": // Strategy Spread
			case "ICSPREAD": // Inter-Commodity Future Spread
				return SecurityTypes.Spread;
			case "INDEX":
			case "MKTSTATS": // Market Statistic
			case "MKTRPT": // Market Reports
			case "CALC": // DTN Calculated Statistic
			case "STRIP":
				return SecurityTypes.Index;
			case "MONEY": // Money Market Fund
			case "MUTUAL": // Mutual Fund
				return SecurityTypes.Fund;
			case "FOREX": // Foreign Monetary Exchange
				return SecurityTypes.Currency;
			case "PRECMTL": // Precious Metals
			case "ARGUS": // Argus Energy
			case "RACKS": // Racks Energy
			case "RFSPOT": // Refined Fuel Spot
			case "SNL_NG": // SNL Natural Gas
			case "SNL_ELEC": // SNL Electricity
			case "NP_FLOW": // Nord Pool-N2EX Flow
			case "NP_POWER": // Nord Pool-N2EX Power Prices
			case "NP_CAPACITY": // Nord Pool-N2EX Capacity
			case "COMM3": // Commodity 3
			case "ISO":
			case "FAST_RACKS":
				return SecurityTypes.Commodity;
			case "SWAPS": // Interest Rate Swap
				return SecurityTypes.Swap;
			default:
				return null;
		}
	}

	public static object Convert(this IQFeedLevel1Column column, string value)
	{
		if (column == null)
			throw new ArgumentNullException(nameof(column));

		if (value.IsEmpty())
			return null;

		var convValue = value.To(column.Type);

		if (column.Type.IsNumeric() && convValue.To<decimal>() == 0)
			convValue = null;

		return convValue;
	}

	public static DateTime CurrentTimeUtc => DateTime.UtcNow;

	public static DateTime FromEst(this DateTime time)
		=> TimeZoneInfo.ConvertTimeToUtc(
			DateTime.SpecifyKind(time, DateTimeKind.Unspecified), TimeHelper.Est);

	public static DateTime? TryConvertToDateTime(IQFeedLevel1Column dateColumn, string dateValue, IQFeedLevel1Column timeColumn, string timeValue)
	{
		if(timeValue.IsEmptyOrWhiteSpace())
			return null;

		var ts = timeColumn.ConvertToTimeSpan(timeValue);
		if(ts == null)
			return null;

		DateTime day;
		static DateTime currentDateEst()
			=> TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeHelper.Est).Date;

		if(dateColumn == null || dateValue.IsEmptyOrWhiteSpace())
			day = currentDateEst();
		else
			day = dateValue.TryToDateTime(dateColumn.Format) ?? currentDateEst();

		return (day.Date + ts.Value).FromEst();
	}

	public static TimeSpan? ConvertToTimeSpan(this IQFeedLevel1Column column, string value)
	{
		if (column == null)
			throw new ArgumentNullException(nameof(column));

		// http://stocksharp.com/forum/yaf_postsm32150_API-4-2-2-24--InvalidCastException.aspx#post32150
		if (value.ContainsIgnoreCase("99:99:99"))
			return null;

		return value.TryToTimeSpan(column.Format);
	}

	public static DateTime ToEst(this DateTime time) => time.To(destination: TimeHelper.Est);

	public static async Task TryConnect(this Socket socket, EndPoint addr, int numTries, TimeSpan delay, ILogReceiver logger, CancellationToken token)
	{
		for (var i = 0; i < numTries; ++i)
		{
			try
			{
				await socket.ConnectAsync(addr, token);
				break;
			}
			catch (SocketException e)
			{
				if(i == numTries - 1)
					throw;

				logger.AddWarningLog("unable to connect to socket '{0}': {1}", addr, e);
				await delay.Delay(token);
			}
		}
	}

	public static int IndexOfNth(this string str, char value, int startIndex, int nth)
	{
		if (nth < 1)
			throw new ArgumentException("Param 'nth' must be greater than 0!");

		var n = 0;
		var len = str.Length;
		int idx;

		for (var start = startIndex; start < len; start = idx + 1)
		{
			idx = str.IndexOf(value, start);
			if(idx < 0)
				return -1;

			if(++n == nth)
				return idx;
		}

		return -1;
	}

	public static void GetCandleParams(this DataType dataType, out string strArg, out string intervalType)
	{
		if (dataType is null)
			throw new ArgumentNullException(nameof(dataType));

		if (dataType.MessageType == typeof(TimeFrameCandleMessage))
		{
			intervalType = "s";
			strArg = dataType.GetTimeFrame().TotalSeconds.To<int>().To<string>();
		}
		else if (dataType.MessageType == typeof(TickCandleMessage))
		{
			intervalType = "t";
			strArg = dataType.Arg.To<string>();
		}
		else if (dataType.MessageType == typeof(VolumeCandleMessage))
		{
			intervalType = "v";
			strArg = dataType.Arg.To<string>();
		}
		else
			throw new ArgumentOutOfRangeException(nameof(dataType), dataType, LocalizedStrings.WrongCandleType);
	}
}
