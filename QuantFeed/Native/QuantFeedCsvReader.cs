namespace StockSharp.QuantFeed.Native;

sealed class QuantFeedCsvReader(TextReader reader, string sourceName)
{
	private readonly TextReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
	private readonly string _sourceName = sourceName.ThrowIfEmpty(nameof(sourceName));
	private long _lineNumber;
	private char? _delimiter;

	public async ValueTask<QuantFeedCsvHeader> ReadHeaderAsync(
		CancellationToken cancellationToken)
	{
		while (true)
		{
			var line = await _reader.ReadLineAsync(cancellationToken);
			if (line == null)
				return null;
			_lineNumber++;
			if (line.IsEmptyOrWhiteSpace() || line.TrimStart().StartsWith('#'))
				continue;
			if (line.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && line.Length == 5)
			{
				_delimiter = line[4];
				continue;
			}
			_delimiter ??= DetectDelimiter(line);
			return QuantFeedCsvHeader.Create(ParseLine(line, _lineNumber));
		}
	}

	public async IAsyncEnumerable<QuantFeedCsvRow> ReadRowsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (_delimiter == null)
			throw new InvalidOperationException("QuantHouse CSV header has not been read.");
		while (true)
		{
			var line = await _reader.ReadLineAsync(cancellationToken);
			if (line == null)
				yield break;
			_lineNumber++;
			if (line.IsEmptyOrWhiteSpace() || line.TrimStart().StartsWith('#'))
				continue;
			yield return new(ParseLine(line, _lineNumber), _lineNumber);
		}
	}

	private string[] ParseLine(string line, long lineNumber)
	{
		var delimiter = _delimiter ?? throw new InvalidOperationException();
		var values = new List<string>();
		var value = new StringBuilder();
		var quoted = false;
		for (var index = 0; index < line.Length; index++)
		{
			var character = line[index];
			if (character == '"')
			{
				if (quoted && index + 1 < line.Length && line[index + 1] == '"')
				{
					value.Append('"');
					index++;
				}
				else
					quoted = !quoted;
			}
			else if (character == delimiter && !quoted)
			{
				values.Add(value.ToString());
				value.Clear();
			}
			else
				value.Append(character);
		}
		if (quoted)
			throw Error(lineNumber, "contains an unterminated quoted field");
		values.Add(value.ToString());
		return [.. values];
	}

	private static char DetectDelimiter(string line)
	{
		var candidates = new[] { ',', ';', '\t', '|' };
		var counts = new int[candidates.Length];
		var quoted = false;
		for (var index = 0; index < line.Length; index++)
		{
			var character = line[index];
			if (character == '"')
			{
				if (quoted && index + 1 < line.Length && line[index + 1] == '"')
					index++;
				else
					quoted = !quoted;
				continue;
			}
			if (quoted)
				continue;
			for (var candidate = 0; candidate < candidates.Length; candidate++)
			{
				if (character == candidates[candidate])
					counts[candidate]++;
			}
		}
		var best = 0;
		for (var index = 1; index < counts.Length; index++)
		{
			if (counts[index] > counts[best])
				best = index;
		}
		if (counts[best] == 0)
			throw new FormatException("QuantHouse CSV header contains no supported delimiter.");
		return candidates[best];
	}

	public FormatException Error(long lineNumber, string message)
		=> new($"QuantHouse source '{_sourceName}', line {lineNumber}: {message}.");
}

static class QuantFeedRowParser
{
	public static QuantFeedReferenceRow ParseReference(QuantFeedCsvHeader header,
		QuantFeedCsvRow row, string source, TimeZoneInfo defaultTimeZone)
	{
		var instrumentCode = row.Get(header.InstrumentCode);
		var symbol = row.Get(header.Symbol);
		EnsureKey(instrumentCode, symbol, row, source);
		return new(
			instrumentCode,
			symbol,
			row.Get(header.Mic),
			row.Get(header.Name),
			row.Get(header.SecurityType),
			row.Get(header.Currency),
			row.Get(header.Isin),
			NullableDate(row.Get(header.Expiration), defaultTimeZone, row, source,
				"ExpirationDate"),
			NullableDecimal(row, header.Strike, source, "Strike"),
			row.Get(header.OptionType),
			NullableDecimal(row, header.PriceStep, source, "PriceStep"),
			NullableDecimal(row, header.Multiplier, source, "Multiplier"));
	}

	public static QuantFeedMarketRow ParseMarket(QuantFeedCsvHeader header,
		QuantFeedCsvRow row, string source, TimeZoneInfo defaultTimeZone)
	{
		var instrumentCode = row.Get(header.InstrumentCode);
		var symbol = row.Get(header.Symbol);
		EnsureKey(instrumentCode, symbol, row, source);
		var marketTimestamp = Timestamp(row, header.MarketTimestamp, header.MarketDate,
			header.MarketTime, defaultTimeZone, source, "MarketTimestamp");
		var serverTimestamp = Timestamp(row, header.ServerTimestamp, header.ServerDate,
			header.ServerTime, defaultTimeZone, source, "ServerTimestamp");
		var captureTimestamp = Timestamp(row, header.CaptureTimestamp, header.CaptureDate,
			header.CaptureTime, defaultTimeZone, source, "CaptureTimestamp");
		var timestamp = Timestamp(row, header.Timestamp, header.Date, header.Time,
			defaultTimeZone, source, "Timestamp");
		if (marketTimestamp == null && serverTimestamp == null && captureTimestamp == null &&
			timestamp == null)
		{
			throw Error(row, source, "contains no event timestamp");
		}

		return new(
			instrumentCode,
			symbol,
			row.Get(header.Mic),
			marketTimestamp,
			serverTimestamp,
			captureTimestamp,
			timestamp,
			row.Get(header.EventType),
			row.Get(header.Side),
			NullableInt(row, header.Level, source, "Level"),
			row.Get(header.Action),
			NullableDecimal(row, header.Price, source, "Price"),
			NullableDecimal(row, header.Quantity, source, "Quantity"),
			NullableInt(row, header.OrderCount, source, "OrderCount"),
			NullableLong(row, header.Sequence, source, "Sequence"),
			NullableDecimal(row, header.BidPrice, source, "BidPrice"),
			NullableDecimal(row, header.BidSize, source, "BidSize"),
			NullableInt(row, header.BidOrderCount, source, "BidOrderCount"),
			NullableDecimal(row, header.AskPrice, source, "AskPrice"),
			NullableDecimal(row, header.AskSize, source, "AskSize"),
			NullableInt(row, header.AskOrderCount, source, "AskOrderCount"),
			NullableDecimal(row, header.LastTradePrice, source, "LastTradePrice"),
			NullableDecimal(row, header.LastTradeQuantity, source, "LastTradeQuantity"),
			row.Get(header.TradingStatus),
			NullableDecimal(row, header.Open, source, "Open"),
			NullableDecimal(row, header.High, source, "High"),
			NullableDecimal(row, header.Low, source, "Low"),
			NullableDecimal(row, header.Close, source, "Close"),
			NullableDecimal(row, header.Volume, source, "Volume"),
			NullableDecimal(row, header.OpenInterest, source, "OpenInterest"));
	}

	private static DateTime? Timestamp(QuantFeedCsvRow row, int timestampIndex,
		int dateIndex, int timeIndex, TimeZoneInfo defaultTimeZone, string source,
		string field)
	{
		var value = row.Get(timestampIndex);
		var date = row.Get(dateIndex);
		var time = row.Get(timeIndex);
		if (!value.IsEmpty() && !date.IsEmpty() && IsTimeOnly(value))
			value = $"{date} {value}";
		else if (value.IsEmpty())
		{
			if (!date.IsEmpty() && !time.IsEmpty())
				value = $"{date} {time}";
		}
		return NullableTimestamp(value, defaultTimeZone, row, source, field);
	}

	private static DateTime? NullableTimestamp(string value, TimeZoneInfo defaultTimeZone,
		QuantFeedCsvRow row, string source, string field)
	{
		if (value.IsEmpty())
			return null;
		if (IsTimeOnly(value))
			throw Error(row, source, $"field '{field}' has time without a date '{value}'");

		if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var epoch))
		{
			try
			{
				var absolute = Math.Abs(epoch);
				var seconds = absolute >= 100000000000000000m ? epoch / 1000000000m :
					absolute >= 100000000000000m ? epoch / 1000000m :
					absolute >= 100000000000m ? epoch / 1000m : epoch;
				var ticks = checked((long)(seconds * TimeSpan.TicksPerSecond));
				return DateTime.UnixEpoch.AddTicks(ticks);
			}
			catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
			{
				throw Error(row, source, $"field '{field}' has out-of-range epoch '{value}'", ex);
			}
		}

		if (!DateTime.TryParseExact(value,
			[
				"yyyyMMdd HHmmss", "yyyyMMdd HHmmss.FFFFFFF",
				"yyyyMMdd HH:mm:ss", "yyyyMMdd HH:mm:ss.FFFFFFF",
				"yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF",
				"yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss.FFFFFFF",
			], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) &&
			!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out parsed))
		{
			throw Error(row, source, $"field '{field}' has invalid timestamp '{value}'");
		}
		return parsed.Kind switch
		{
			DateTimeKind.Utc => parsed,
			DateTimeKind.Local => parsed.ToUniversalTime(),
			_ => TimeZoneInfo.ConvertTimeToUtc(parsed, defaultTimeZone),
		};
	}

	private static bool IsTimeOnly(string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return false;
		if (value.Contains(':') && !value.Contains('T') && !value.Contains('-') &&
			!value.Contains('/'))
		{
			return true;
		}
		var separator = value.IndexOf('.');
		var digits = separator < 0 ? value : value[..separator];
		return digits.Length == 6 && digits.All(char.IsDigit);
	}

	private static DateTime? NullableDate(string value, TimeZoneInfo defaultTimeZone,
		QuantFeedCsvRow row, string source, string field)
	{
		if (value.IsEmpty())
			return null;
		if (DateTime.TryParseExact(value,
			["yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy"],
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
		{
			return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
		}
		return NullableTimestamp(value, defaultTimeZone, row, source, field)?.Date;
	}

	private static decimal? NullableDecimal(QuantFeedCsvRow row, int index,
		string source, string field)
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

	private static int? NullableInt(QuantFeedCsvRow row, int index,
		string source, string field)
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

	private static long? NullableLong(QuantFeedCsvRow row, int index,
		string source, string field)
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

	private static void EnsureKey(string instrumentCode, string symbol,
		QuantFeedCsvRow row, string source)
	{
		if (instrumentCode.IsEmpty() && symbol.IsEmpty())
		{
			throw Error(row, source,
				"contains neither FeedOS instrument code nor local symbol");
		}
	}

	private static FormatException Error(QuantFeedCsvRow row, string source,
		string message, Exception inner = null)
		=> new($"QuantHouse source '{source}', line {row.LineNumber}: {message}.", inner);
}
