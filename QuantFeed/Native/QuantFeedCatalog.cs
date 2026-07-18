namespace StockSharp.QuantFeed.Native;

sealed class QuantFeedCatalog
{
	private readonly QuantFeedDataSource[] _sources;

	private QuantFeedCatalog(QuantFeedDataSource[] sources)
	{
		_sources = sources;
	}

	public IReadOnlyList<QuantFeedDataSource> Sources => _sources;

	public static ValueTask<QuantFeedCatalog> LoadAsync(string dataDirectory,
		bool isRecursive, CancellationToken cancellationToken)
	{
		dataDirectory.ThrowIfEmpty(nameof(dataDirectory));
		var root = Path.GetFullPath(dataDirectory);
		if (!Directory.Exists(root))
			throw new DirectoryNotFoundException($"QuantHouse data directory '{root}' was not found.");

		var sources = new List<QuantFeedDataSource>();
		foreach (var path in EnumerateFiles(root, isRecursive))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var relative = Path.GetRelativePath(root, path);
			if (QuantFeedDataSource.IsCsvName(path))
				sources.Add(new(path, null, relative));
			else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				LoadZip(path, relative, sources);
		}

		if (sources.Count == 0)
		{
			throw new InvalidOperationException(
				$"QuantHouse data directory '{root}' contains no CSV, CSV.GZ, or ZIP delivery files.");
		}

		return ValueTask.FromResult(new QuantFeedCatalog(
			[.. sources.OrderBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)]));
	}

	public IEnumerable<QuantFeedDataSource> GetSources(MarketDataMessage message)
	{
		var from = message.From is { } fromTime
			? QuantFeedExtensions.ToUtc(fromTime).Date.AddDays(-1) : (DateTime?)null;
		var to = message.To is { } toTime
			? QuantFeedExtensions.ToUtc(toTime).Date.AddDays(1) : (DateTime?)null;
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

	public async ValueTask<QuantFeedDataFile> OpenAsync(QuantFeedDataSource source,
		CancellationToken cancellationToken)
	{
		var opened = await source.OpenAsync(cancellationToken);
		try
		{
			var streamReader = new StreamReader(opened.Stream, Encoding.UTF8, true,
				64 * 1024, leaveOpen: true);
			var csvReader = new QuantFeedCsvReader(streamReader, source.DisplayName);
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
		List<QuantFeedDataSource> sources)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
			FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
		foreach (var entry in archive.Entries)
		{
			if (QuantFeedDataSource.IsCsvName(entry.FullName))
			{
				sources.Add(new(path, entry.FullName,
					$"{relative}!{entry.FullName}"));
			}
		}
	}
}

sealed class QuantFeedDataSource
{
	private readonly string _path;
	private readonly string _entryName;

	public QuantFeedDataSource(string path, string entryName, string displayName)
	{
		_path = path.ThrowIfEmpty(nameof(path));
		_entryName = entryName;
		DisplayName = displayName.ThrowIfEmpty(nameof(displayName));
		DateHint = FindDateHint(displayName);
	}

	public string DisplayName { get; }
	public DateTime? DateHint { get; }

	public ValueTask<QuantFeedOpenedSource> OpenAsync(
		CancellationToken cancellationToken)
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
				{
					throw new InvalidDataException(
						$"ZIP entry '{_entryName}' was not found in '{_path}'.");
				}
				data = entry.Open();
				owners.Add(data);
			}
			if ((_entryName.IsEmpty(_path)).EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
			{
				data = new GZipStream(data, CompressionMode.Decompress, leaveOpen: true);
				owners.Add(data);
			}
			return ValueTask.FromResult(new QuantFeedOpenedSource(data, owners));
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

sealed class QuantFeedOpenedSource(Stream stream, List<IDisposable> owners) : IAsyncDisposable
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

sealed class QuantFeedDataFile(QuantFeedOpenedSource opened, StreamReader streamReader,
	QuantFeedCsvReader reader, QuantFeedCsvHeader header) : IAsyncDisposable
{
	private readonly QuantFeedOpenedSource _opened = opened;
	private readonly StreamReader _streamReader = streamReader;

	public QuantFeedCsvReader Reader { get; } = reader;
	public QuantFeedCsvHeader Header { get; } = header;

	public async ValueTask DisposeAsync()
	{
		_streamReader.Dispose();
		await _opened.DisposeAsync();
	}
}
