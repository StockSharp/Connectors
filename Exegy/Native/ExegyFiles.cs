namespace StockSharp.Exegy.Native;

sealed class ExegyCatalog
{
	private readonly ExegyDataSource[] _sources;

	private ExegyCatalog(ExegyDataSource[] sources)
	{
		_sources = sources;
	}

	public IReadOnlyList<ExegyDataSource> Sources => _sources;

	public static ExegyCatalog Load(string dataDirectory, bool isRecursive,
		CancellationToken cancellationToken)
	{
		dataDirectory.ThrowIfEmpty(nameof(dataDirectory));
		var root = Path.GetFullPath(dataDirectory);
		if (!Directory.Exists(root))
			throw new DirectoryNotFoundException($"Exegy data directory '{root}' was not found.");

		var sources = new List<ExegyDataSource>();
		foreach (var path in EnumerateFiles(root, isRecursive))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var relative = Path.GetRelativePath(root, path);
			if (ExegyDataSource.IsCsvName(path))
				sources.Add(new(path, null, relative));
			else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				LoadZip(path, relative, sources);
		}
		if (sources.Count == 0)
		{
			throw new InvalidOperationException(
				$"Exegy data directory '{root}' contains no CSV, CSV.GZ, or ZIP delivery files.");
		}
		return new([.. sources.OrderBy(source => source.DisplayName,
			StringComparer.OrdinalIgnoreCase)]);
	}

	public IEnumerable<ExegyDataSource> GetSources(MarketDataMessage message)
	{
		var from = message.From is { } fromTime
			? ExegyExtensions.ToUtc(fromTime).Date.AddDays(-1) : (DateTime?)null;
		var to = message.To is { } toTime
			? ExegyExtensions.ToUtc(toTime).Date.AddDays(1) : (DateTime?)null;
		foreach (var source in _sources)
		{
			if (source.DateHint is { } date &&
				(from != null && date < from.Value || to != null && date > to.Value))
			{
				continue;
			}
			yield return source;
		}
	}

	public async ValueTask<ExegyDataFile> OpenAsync(ExegyDataSource source,
		CancellationToken cancellationToken)
	{
		var opened = source.Open(cancellationToken);
		try
		{
			var streamReader = new StreamReader(opened.Stream, Encoding.UTF8, true,
				64 * 1024, leaveOpen: true);
			var csvReader = new ExegyCsvReader(streamReader, source.DisplayName);
			var header = await csvReader.ReadHeaderAsync(cancellationToken);
			return new(opened, streamReader, csvReader, header);
		}
		catch
		{
			await opened.DisposeAsync();
			throw;
		}
	}

	private static IEnumerable<string> EnumerateFiles(string root, bool isRecursive)
	{
		var pending = new Stack<DirectoryInfo>();
		pending.Push(new(root));
		while (pending.Count > 0)
		{
			var directory = pending.Pop();
			foreach (var file in directory.EnumerateFiles())
				yield return file.FullName;
			if (!isRecursive)
				continue;
			foreach (var child in directory.EnumerateDirectories())
			{
				if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
					pending.Push(child);
			}
		}
	}

	private static void LoadZip(string path, string relative,
		List<ExegyDataSource> sources)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
			FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
		foreach (var entry in archive.Entries)
		{
			if (ExegyDataSource.IsCsvName(entry.FullName))
				sources.Add(new(path, entry.FullName, $"{relative}!{entry.FullName}"));
		}
	}
}

sealed class ExegyDataSource
{
	private readonly string _path;
	private readonly string _entryName;

	public ExegyDataSource(string path, string entryName, string displayName)
	{
		_path = path.ThrowIfEmpty(nameof(path));
		_entryName = entryName;
		DisplayName = displayName.ThrowIfEmpty(nameof(displayName));
		DateHint = FindDateHint(displayName);
	}

	public string DisplayName { get; }
	public DateTime? DateHint { get; }

	public ExegyOpenedSource Open(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var owners = new List<IDisposable>();
		try
		{
			var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
				64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
			owners.Add(file);
			Stream data = file;
			if (!_entryName.IsEmpty())
			{
				var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
				owners.Add(archive);
				var entry = archive.Entries.FirstOrDefault(candidate =>
					candidate.FullName.Equals(_entryName, StringComparison.Ordinal));
				if (entry == null)
					throw new InvalidDataException($"ZIP entry '{_entryName}' was not found in '{_path}'.");
				data = entry.Open();
				owners.Add(data);
			}
			if ((_entryName.IsEmpty(_path)).EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
			{
				data = new GZipStream(data, CompressionMode.Decompress, leaveOpen: true);
				owners.Add(data);
			}
			return new(data, owners);
		}
		catch
		{
			for (var index = owners.Count - 1; index >= 0; index--)
				owners[index].Dispose();
			throw;
		}
	}

	public static bool IsCsvName(string value)
		=> value.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
			value.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase);

	private static DateTime? FindDateHint(string value)
	{
		DateTime? result = null;
		for (var index = 0; index <= value.Length - 8; index++)
		{
			var candidate = value.AsSpan(index, 8);
			if (!IsDigits(candidate) ||
				!DateTime.TryParseExact(candidate, "yyyyMMdd", CultureInfo.InvariantCulture,
					DateTimeStyles.None, out var date))
			{
				continue;
			}
			date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
			if (result != null && result.Value != date)
				return null;
			result = date;
			index += 7;
		}
		return result;
	}

	private static bool IsDigits(ReadOnlySpan<char> value)
	{
		foreach (var character in value)
		{
			if (!char.IsDigit(character))
				return false;
		}
		return true;
	}
}

sealed class ExegyOpenedSource(Stream stream, List<IDisposable> owners) : IAsyncDisposable
{
	private readonly List<IDisposable> _owners = owners;
	public Stream Stream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));

	public ValueTask DisposeAsync()
	{
		for (var index = _owners.Count - 1; index >= 0; index--)
			_owners[index].Dispose();
		_owners.Clear();
		return ValueTask.CompletedTask;
	}
}

sealed class ExegyDataFile(ExegyOpenedSource opened, StreamReader streamReader,
	ExegyCsvReader reader, ExegyCsvHeader header) : IAsyncDisposable
{
	private readonly ExegyOpenedSource _opened = opened;
	private readonly StreamReader _streamReader = streamReader;
	public ExegyCsvReader Reader { get; } = reader;
	public ExegyCsvHeader Header { get; } = header;

	public async ValueTask DisposeAsync()
	{
		_streamReader.Dispose();
		await _opened.DisposeAsync();
	}
}

sealed class ExegyCsvReader(TextReader reader, string sourceName)
{
	private readonly TextReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
	private readonly string _sourceName = sourceName.ThrowIfEmpty(nameof(sourceName));
	private long _lineNumber;
	private char? _delimiter;

	public async ValueTask<ExegyCsvHeader> ReadHeaderAsync(CancellationToken cancellationToken)
	{
		while (await _reader.ReadLineAsync(cancellationToken) is { } line)
		{
			_lineNumber++;
			if (line.IsEmptyOrWhiteSpace() || line.TrimStart().StartsWith('#'))
				continue;
			if (line.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && line.Length == 5)
			{
				_delimiter = line[4];
				continue;
			}
			_delimiter ??= DetectDelimiter(line);
			return ExegyCsvHeader.Create(ParseLine(line, _lineNumber));
		}
		return null;
	}

	public async IAsyncEnumerable<ExegyCsvRow> ReadRowsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (_delimiter == null)
			throw new InvalidOperationException("Exegy CSV header has not been read.");
		while (await _reader.ReadLineAsync(cancellationToken) is { } line)
		{
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
			throw new FormatException("Exegy CSV header contains no supported delimiter.");
		return candidates[best];
	}

	private FormatException Error(long lineNumber, string message)
		=> new($"Exegy source '{_sourceName}', line {lineNumber}: {message}.");
}

static class ExegyRowParser
{
	public static ExegyReferenceRow ParseReference(ExegyCsvHeader header, ExegyCsvRow row,
		string source, TimeZoneInfo defaultTimeZone)
	{
		var instrumentId = row.Get(header.InstrumentId);
		var symbol = row.Get(header.Symbol);
		EnsureKey(instrumentId, symbol, row, source);
		return new(instrumentId, symbol, row.Get(header.Venue), row.Get(header.Name),
			row.Get(header.SecurityType), row.Get(header.Currency), row.Get(header.Isin),
			Date(row.Get(header.Expiration), defaultTimeZone, row, source, "ExpirationDate"),
			Decimal(row, header.Strike, source, "Strike"), row.Get(header.OptionType),
			Decimal(row, header.PriceStep, source, "PriceStep"),
			Decimal(row, header.Multiplier, source, "Multiplier"));
	}

	public static ExegyMarketRow ParseMarket(ExegyCsvHeader header, ExegyCsvRow row,
		string source, TimeZoneInfo defaultTimeZone)
	{
		var instrumentId = row.Get(header.InstrumentId);
		var symbol = row.Get(header.Symbol);
		EnsureKey(instrumentId, symbol, row, source);
		var date = row.Get(header.Date);
		var exchangeTime = Timestamp(WithDate(row.Get(header.ExchangeTimestamp), date), null,
			defaultTimeZone,
			row, source, "ExchangeTimestamp");
		var sourceTime = Timestamp(WithDate(row.Get(header.SourceTimestamp), date), null,
			defaultTimeZone,
			row, source, "SourceTimestamp");
		var captureTime = Timestamp(WithDate(row.Get(header.CaptureTimestamp), date), null,
			defaultTimeZone,
			row, source, "CaptureTimestamp");
		var timestampValue = row.Get(header.Timestamp).IsEmpty(row.Get(header.Time));
		var timestamp = Timestamp(WithDate(timestampValue, date), null,
			defaultTimeZone, row, source, "Timestamp");
		if (exchangeTime == null && sourceTime == null && captureTime == null && timestamp == null)
			throw Error(row, source, "contains no event timestamp");
		return new(instrumentId, symbol, row.Get(header.Venue), exchangeTime, sourceTime,
			captureTime, timestamp, row.Get(header.MessageType), row.Get(header.Action),
			row.Get(header.Side), Int(row, header.Level, source, "Level"),
			Decimal(row, header.Price, source, "Price"),
			Decimal(row, header.Size, source, "Size"),
			Int(row, header.OrderCount, source, "OrderCount"), row.Get(header.OrderId),
			row.Get(header.TradeId), Long(row, header.Sequence, source, "Sequence"),
			row.Get(header.Participant), Decimal(row, header.BidPrice, source, "BidPrice"),
			Decimal(row, header.BidSize, source, "BidSize"),
			Int(row, header.BidOrderCount, source, "BidOrderCount"),
			Decimal(row, header.AskPrice, source, "AskPrice"),
			Decimal(row, header.AskSize, source, "AskSize"),
			Int(row, header.AskOrderCount, source, "AskOrderCount"),
			Decimal(row, header.TradePrice, source, "TradePrice"),
			Decimal(row, header.TradeSize, source, "TradeSize"),
			row.Get(header.TradingStatus), Decimal(row, header.Open, source, "Open"),
			Decimal(row, header.High, source, "High"),
			Decimal(row, header.Low, source, "Low"),
			Decimal(row, header.Close, source, "Close"),
			Decimal(row, header.CumulativeVolume, source, "CumulativeVolume"),
			Decimal(row, header.OpenInterest, source, "OpenInterest"),
			row.Get(header.Condition));
	}

	private static DateTime? Timestamp(string value, string fallback,
		TimeZoneInfo defaultTimeZone, ExegyCsvRow row, string source, string field)
	{
		value = value.IsEmpty(fallback);
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
				return DateTime.UnixEpoch.AddTicks(
					checked((long)(seconds * TimeSpan.TicksPerSecond)));
			}
			catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
			{
				throw Error(row, source, $"field '{field}' has out-of-range epoch '{value}'", ex);
			}
		}
		if (!DateTime.TryParseExact(value,
			["yyyyMMdd HHmmss", "yyyyMMdd HHmmss.FFFFFFF", "yyyyMMdd HH:mm:ss.FFFFFFF",
			 "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy/MM/dd HH:mm:ss.FFFFFFF"],
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) &&
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

	private static DateTime? Date(string value, TimeZoneInfo defaultTimeZone,
		ExegyCsvRow row, string source, string field)
	{
		if (value.IsEmpty())
			return null;
		if (DateTime.TryParseExact(value, ["yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd"],
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
		{
			return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
		}
		return Timestamp(value, null, defaultTimeZone, row, source, field)?.Date;
	}

	private static decimal? Decimal(ExegyCsvRow row, int index, string source, string field)
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

	private static int? Int(ExegyCsvRow row, int index, string source, string field)
	{
		var value = row.Get(index);
		if (value.IsEmpty())
			return null;
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var result))
			throw Error(row, source, $"field '{field}' has invalid integer '{value}'");
		return result;
	}

	private static long? Long(ExegyCsvRow row, int index, string source, string field)
	{
		var value = row.Get(index);
		if (value.IsEmpty())
			return null;
		if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var result))
			throw Error(row, source, $"field '{field}' has invalid integer '{value}'");
		return result;
	}

	private static bool IsTimeOnly(string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return false;
		if (value.Contains(':') && !value.Contains('T') && !value.Contains('-') &&
			!value.Contains('/'))
			return true;
		var separator = value.IndexOf('.');
		var digits = separator < 0 ? value : value[..separator];
		return digits.Length == 6 && digits.All(char.IsDigit);
	}

	private static string WithDate(string value, string date)
		=> !date.IsEmpty() && IsTimeOnly(value) ? $"{date} {value}" : value;

	private static void EnsureKey(string instrumentId, string symbol,
		ExegyCsvRow row, string source)
	{
		if (instrumentId.IsEmpty() && symbol.IsEmpty())
			throw Error(row, source, "contains neither instrument ID nor symbol");
	}

	private static FormatException Error(ExegyCsvRow row, string source, string message,
		Exception inner = null)
		=> new($"Exegy source '{source}', line {row.LineNumber}: {message}.", inner);
}
