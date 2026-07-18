namespace StockSharp.AlgoSeek.Native;

sealed class AlgoSeekCsvReader(TextReader reader, string sourceName)
{
	private readonly TextReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
	private readonly string _sourceName = sourceName.ThrowIfEmpty(nameof(sourceName));
	private long _lineNumber;

	public async ValueTask<AlgoSeekCsvHeader> ReadHeaderAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			var line = await _reader.ReadLineAsync(cancellationToken);
			if (line == null)
				return null;
			_lineNumber++;
			if (line.IsEmptyOrWhiteSpace())
				continue;
			return AlgoSeekCsvHeader.Create(ParseLine(line, _lineNumber));
		}
	}

	public async IAsyncEnumerable<AlgoSeekCsvRow> ReadRowsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		while (true)
		{
			var line = await _reader.ReadLineAsync(cancellationToken);
			if (line == null)
				yield break;
			_lineNumber++;
			if (line.IsEmptyOrWhiteSpace())
				continue;
			yield return new(ParseLine(line, _lineNumber), _lineNumber);
		}
	}

	private string[] ParseLine(string line, long lineNumber)
	{
		var values = new List<string>();
		var value = new StringBuilder();
		var quoted = false;
		for (var i = 0; i < line.Length; i++)
		{
			var character = line[i];
			if (character == '"')
			{
				if (quoted && i + 1 < line.Length && line[i + 1] == '"')
				{
					value.Append('"');
					i++;
				}
				else
					quoted = !quoted;
			}
			else if (character == ',' && !quoted)
			{
				values.Add(value.ToString());
				value.Clear();
			}
			else
				value.Append(character);
		}
		if (quoted)
			throw Error(lineNumber, "contains an unterminated quoted CSV field");
		values.Add(value.ToString());
		return [.. values];
	}

	public FormatException Error(long lineNumber, string message)
		=> new($"AlgoSeek source '{_sourceName}', line {lineNumber}: {message}.");
}

static class AlgoSeekRowParser
{
	public static AlgoSeekEquityTickRow ParseEquityTick(AlgoSeekCsvHeader header,
		AlgoSeekCsvRow row, string source)
		=> new(
			Date(row, header.Date, source, "Date"),
			Required(row, header.Timestamp, source, "Timestamp"),
			Required(row, header.EventType, source, "EventType"),
			Required(row, header.Ticker, source, "Ticker"),
			Decimal(row, header.Price, source, "Price"),
			Long(row, header.Quantity, source, "Quantity"),
			row.Get(header.Exchange),
			row.Get(header.Conditions));

	public static AlgoSeekOptionTickRow ParseOptionTick(AlgoSeekCsvHeader header,
		AlgoSeekCsvRow row, string source)
		=> new(
			Date(row, header.Date, source, "Date"),
			Required(row, header.Timestamp, source, "Timestamp"),
			Required(row, header.Ticker, source, "Ticker"),
			OptionType(row, header.CallPut, source),
			Decimal(row, header.Strike, source, "StrikePrice"),
			Date(row, header.ExpirationDate, source, "ExpirationDate"),
			Int(row, header.EventType, source, "EventType"),
			row.Get(header.Side),
			Required(row, header.Action, source, "Action"),
			Decimal(row, header.Price, source, "Price"),
			Long(row, header.Quantity, source, "Quantity"),
			row.Get(header.Exchange),
			row.Get(header.Conditions),
			NullableDecimal(row, header.UnderBidPrice, source, "UnderBidPrice"),
			NullableDecimal(row, header.UnderAskPrice, source, "UnderAskPrice"));

	public static AlgoSeekFuturesTickRow ParseFuturesTick(AlgoSeekCsvHeader header,
		AlgoSeekCsvRow row, string source)
		=> new(
			Date(row, header.UtcDate, source, "UTCDate"),
			Required(row, header.UtcTime, source, "UTCTime"),
			NullableDate(row, header.LocalDate, source, "LocalDate") ?? default,
			row.Get(header.LocalTime),
			Required(row, header.Ticker, source, "Ticker"),
			NullableOptionType(row, header.CallPut, source),
			NullableDecimal(row, header.Strike, source, "Strike"),
			row.Get(header.Month),
			NullableInt(row, header.ExpirationYear, source, "ExpirationYear"),
			row.Get(header.SecurityId),
			Int(row, header.TypeMask, source, "TypeMask"),
			Required(row, header.Type, source, "Type"),
			Decimal(row, header.Price, source, "Price"),
			Long(row, header.Quantity, source, "Quantity"),
			NullableInt(row, header.Orders, source, "Orders"),
			NullableInt(row, header.Flags, source, "Flags") ?? 0);

	public static AlgoSeekEquityMinuteRow ParseEquityMinute(AlgoSeekCsvHeader header,
		AlgoSeekCsvRow row, string source)
		=> new(
			Date(row, header.Date, source, "Date"),
			Required(row, header.Ticker, source, "Ticker"),
			Required(row, header.TimeBarStart, source, "TimeBarStart"),
			Decimal(row, header.FirstTradePrice, source, "FirstTradePrice"),
			Decimal(row, header.HighTradePrice, source, "HighTradePrice"),
			Decimal(row, header.LowTradePrice, source, "LowTradePrice"),
			Decimal(row, header.LastTradePrice, source, "LastTradePrice"),
			NullableDecimal(row, header.VolumeWeightPrice, source, "VolumeWeightPrice"),
			Long(row, header.Volume, source, "Volume"),
			NullableLong(row, header.TotalTrades, source, "TotalTrades"));

	public static AlgoSeekOptionMinuteRow ParseOptionMinute(AlgoSeekCsvHeader header,
		AlgoSeekCsvRow row, string source)
		=> new(
			Date(row, header.Date, source, "Date"),
			Required(row, header.TimeBarStart, source, "TimeBarStart"),
			Required(row, header.Ticker, source, "Ticker"),
			OptionType(row, header.CallPut, source),
			Decimal(row, header.Strike, source, "Strike"),
			Date(row, header.ExpirationDate, source, "ExpirationDate"),
			NullableDecimal(row, header.FirstTradePrice, source, "OpenTradePrice"),
			NullableDecimal(row, header.HighTradePrice, source, "HighTradePrice"),
			NullableDecimal(row, header.LowTradePrice, source, "LowTradePrice"),
			NullableDecimal(row, header.LastTradePrice, source, "CloseTradePrice"),
			NullableLong(row, header.Volume, source, "Volume") ?? 0,
			NullableLong(row, header.TotalTrades, source, "TotalTrades"),
			row.Get(header.CloseBidTime),
			NullableDecimal(row, header.CloseBidPrice, source, "CloseBidPrice"),
			NullableLong(row, header.CloseBidSize, source, "CloseBidSize"),
			row.Get(header.CloseAskTime),
			NullableDecimal(row, header.CloseAskPrice, source, "CloseAskPrice"),
			NullableLong(row, header.CloseAskSize, source, "CloseAskSize"));

	public static AlgoSeekEquityDailyRow ParseEquityDaily(AlgoSeekCsvHeader header,
		AlgoSeekCsvRow row, string source)
		=> new(
			Date(row, header.TradeDate, source, "TradeDate"),
			row.Get(header.SecId),
			Required(row, header.Ticker, source, "Ticker"),
			Decimal(row, header.Open, source, "Open"),
			Decimal(row, header.High, source, "High"),
			Decimal(row, header.Low, source, "Low"),
			Decimal(row, header.Close, source, "Close"),
			Long(row, header.MarketHoursVolume, source, "MarketHoursVolume"));

	private static string Required(AlgoSeekCsvRow row, int index, string source, string field)
	{
		var value = row.Get(index);
		if (value.IsEmpty())
			throw Error(row, source, $"field '{field}' is empty");
		return value;
	}

	private static DateTime Date(AlgoSeekCsvRow row, int index, string source, string field)
		=> NullableDate(row, index, source, field) ??
			throw Error(row, source, $"field '{field}' is empty");

	private static DateTime? NullableDate(AlgoSeekCsvRow row, int index, string source,
		string field)
	{
		var value = row.Get(index);
		if (value.IsEmpty())
			return null;
		if (!DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var result))
		{
			throw Error(row, source, $"field '{field}' has invalid date '{value}'");
		}
		return DateTime.SpecifyKind(result.Date, DateTimeKind.Unspecified);
	}

	private static decimal Decimal(AlgoSeekCsvRow row, int index, string source, string field)
		=> NullableDecimal(row, index, source, field) ??
			throw Error(row, source, $"field '{field}' is empty");

	private static decimal? NullableDecimal(AlgoSeekCsvRow row, int index, string source,
		string field)
	{
		var value = row.Get(index);
		if (value.IsEmpty())
			return null;
		if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var result))
		{
			throw Error(row, source, $"field '{field}' has invalid decimal '{value}'");
		}
		return result;
	}

	private static long Long(AlgoSeekCsvRow row, int index, string source, string field)
		=> NullableLong(row, index, source, field) ??
			throw Error(row, source, $"field '{field}' is empty");

	private static long? NullableLong(AlgoSeekCsvRow row, int index, string source,
		string field)
	{
		var value = row.Get(index);
		if (value.IsEmpty())
			return null;
		if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var result))
		{
			throw Error(row, source, $"field '{field}' has invalid integer '{value}'");
		}
		return result;
	}

	private static int Int(AlgoSeekCsvRow row, int index, string source, string field)
		=> NullableInt(row, index, source, field) ??
			throw Error(row, source, $"field '{field}' is empty");

	private static int? NullableInt(AlgoSeekCsvRow row, int index, string source, string field)
	{
		var value = row.Get(index);
		if (value.IsEmpty())
			return null;
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var result))
		{
			throw Error(row, source, $"field '{field}' has invalid integer '{value}'");
		}
		return result;
	}

	private static OptionTypes OptionType(AlgoSeekCsvRow row, int index, string source)
		=> NullableOptionType(row, index, source) ??
			throw Error(row, source, "field 'CallPut' is empty");

	private static OptionTypes? NullableOptionType(AlgoSeekCsvRow row, int index, string source)
	{
		var value = row.Get(index);
		if (value.IsEmpty())
			return null;
		return value.EqualsIgnoreCase("C") || value.EqualsIgnoreCase("CALL")
			? OptionTypes.Call
			: value.EqualsIgnoreCase("P") || value.EqualsIgnoreCase("PUT")
				? OptionTypes.Put
				: throw Error(row, source, $"field 'CallPut' has invalid value '{value}'");
	}

	private static FormatException Error(AlgoSeekCsvRow row, string source, string message)
		=> new($"AlgoSeek source '{source}', line {row.LineNumber}: {message}.");
}
