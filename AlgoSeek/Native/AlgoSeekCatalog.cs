namespace StockSharp.AlgoSeek.Native;

sealed class AlgoSeekCatalog
{
	private readonly AlgoSeekDataSource[] _sources;

	private AlgoSeekCatalog(AlgoSeekDataSource[] sources)
	{
		_sources = sources;
	}

	public IReadOnlyList<AlgoSeekDataSource> Sources => _sources;

	public static async ValueTask<AlgoSeekCatalog> LoadAsync(string dataDirectory,
		bool isRecursive, CancellationToken cancellationToken)
	{
		dataDirectory.ThrowIfEmpty(nameof(dataDirectory));
		var root = Path.GetFullPath(dataDirectory);
		if (!Directory.Exists(root))
			throw new DirectoryNotFoundException($"AlgoSeek data directory '{root}' was not found.");

		var sources = new List<AlgoSeekDataSource>();
		foreach (var path in EnumerateFiles(root, isRecursive))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var relative = Path.GetRelativePath(root, path);
			if (AlgoSeekDataSource.IsCsvName(path))
				sources.Add(new(path, null, relative, AlgoSeekContainers.File));
			else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				LoadZip(path, relative, sources);
			else if (AlgoSeekDataSource.IsTarName(path))
				await LoadTarAsync(path, relative, sources, cancellationToken);
		}

		if (sources.Count == 0)
		{
			throw new InvalidOperationException(
				$"AlgoSeek data directory '{root}' contains no CSV, CSV.GZ, ZIP, TAR, TAR.GZ, or TGZ delivery files.");
		}

		return new([.. sources.OrderBy(source => source.SortKey,
			StringComparer.OrdinalIgnoreCase)]);
	}

	public IEnumerable<AlgoSeekDataSource> GetCandidates(AlgoSeekSecurityKey key,
		DateTime? fromDate, DateTime? toDate)
	{
		foreach (var source in _sources)
		{
			if (source.DateHint is { } date &&
				(fromDate != null && date < fromDate.Value.Date ||
				toDate != null && date > toDate.Value.Date))
			{
				continue;
			}
			if (!source.MatchesSymbol(key.Symbol))
				continue;
			yield return source;
		}
	}

	public async ValueTask<AlgoSeekDataFile> OpenAsync(AlgoSeekDataSource source,
		CancellationToken cancellationToken)
	{
		var opened = await source.OpenAsync(cancellationToken);
		try
		{
			var streamReader = new StreamReader(opened.Stream, Encoding.UTF8, true,
				bufferSize: 64 * 1024, leaveOpen: true);
			var csvReader = new AlgoSeekCsvReader(streamReader, source.DisplayName);
			var header = await csvReader.ReadHeaderAsync(cancellationToken);
			return new(source, opened, streamReader, csvReader, header);
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
		List<AlgoSeekDataSource> sources)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
			FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
		foreach (var entry in archive.Entries)
		{
			if (entry.Length >= 0 && AlgoSeekDataSource.IsCsvName(entry.FullName))
			{
				sources.Add(new(path, entry.FullName,
					$"{relative}!{entry.FullName}", AlgoSeekContainers.Zip));
			}
		}
	}

	private static async ValueTask LoadTarAsync(string path, string relative,
		List<AlgoSeekDataSource> sources, CancellationToken cancellationToken)
	{
		await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
			FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
		await using var payload = AlgoSeekDataSource.IsCompressedTarName(path)
			? new GZipStream(file, CompressionMode.Decompress, leaveOpen: true)
			: null;
		Stream stream = payload == null ? file : payload;
		using var archive = new TarReader(stream, leaveOpen: true);
		while (await archive.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
		{
			if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile &&
				AlgoSeekDataSource.IsCsvName(entry.Name))
			{
				sources.Add(new(path, entry.Name,
					$"{relative}!{entry.Name}", AlgoSeekContainers.Tar));
			}
		}
	}
}

enum AlgoSeekContainers
{
	File,
	Zip,
	Tar,
}

sealed class AlgoSeekDataSource
{
	private readonly string _path;
	private readonly string _entryName;
	private readonly AlgoSeekContainers _container;
	private readonly string _leafName;

	public AlgoSeekDataSource(string path, string entryName, string displayName,
		AlgoSeekContainers container)
	{
		_path = path.ThrowIfEmpty(nameof(path));
		_entryName = entryName;
		DisplayName = displayName.ThrowIfEmpty(nameof(displayName));
		_container = container;
		_leafName = StripExtensions(Path.GetFileName(entryName.IsEmpty(path)));
		DateHint = FindDateHint(displayName);
	}

	public string DisplayName { get; }
	public string SortKey => DisplayName;
	public DateTime? DateHint { get; }

	public bool MatchesSymbol(string symbol)
	{
		if (symbol.IsEmpty() || ContainsToken(_leafName, symbol))
			return true;
		return IsGenericName(_leafName);
	}

	public async ValueTask<AlgoSeekOpenedSource> OpenAsync(
		CancellationToken cancellationToken)
	{
		var owners = new List<IDisposable>();
		try
		{
			var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
				64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
			owners.Add(file);
			Stream data;

			switch (_container)
			{
				case AlgoSeekContainers.File:
					data = file;
					break;
				case AlgoSeekContainers.Zip:
				{
					var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
					owners.Add(archive);
					var entry = archive.Entries.FirstOrDefault(candidate =>
						candidate.FullName.Equals(_entryName, StringComparison.Ordinal));
					if (entry == null)
						throw new InvalidDataException($"ZIP entry '{_entryName}' was not found in '{_path}'.");
					data = entry.Open();
					owners.Add(data);
					break;
				}
				case AlgoSeekContainers.Tar:
				{
					Stream archiveStream = file;
					if (IsCompressedTarName(_path))
					{
						archiveStream = new GZipStream(file, CompressionMode.Decompress,
							leaveOpen: true);
						owners.Add(archiveStream);
					}
					var archive = new TarReader(archiveStream, leaveOpen: true);
					owners.Add(archive);
					TarEntry entry = null;
					while (await archive.GetNextEntryAsync(copyData: false,
						cancellationToken) is { } candidate)
					{
						if (candidate.Name.Equals(_entryName, StringComparison.Ordinal))
						{
							entry = candidate;
							break;
						}
					}
					if (entry?.DataStream == null)
						throw new InvalidDataException($"TAR entry '{_entryName}' was not found in '{_path}'.");
					data = entry.DataStream;
					owners.Add(data);
					break;
				}
				default:
					throw new ArgumentOutOfRangeException();
			}

			if ((_entryName.IsEmpty(_path)).EndsWith(".gz",
				StringComparison.OrdinalIgnoreCase))
			{
				data = new GZipStream(data, CompressionMode.Decompress, leaveOpen: true);
				owners.Add(data);
			}
			return new(data, owners);
		}
		catch
		{
			for (var i = owners.Count - 1; i >= 0; i--)
				owners[i].Dispose();
			throw;
		}
	}

	public static bool IsCsvName(string value)
		=> value.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
			value.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase);

	public static bool IsTarName(string value)
		=> value.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
			IsCompressedTarName(value);

	public static bool IsCompressedTarName(string value)
		=> value.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
			value.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

	private static string StripExtensions(string value)
	{
		if (value.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
			value = Path.GetFileNameWithoutExtension(value);
		if (value.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
			value = Path.GetFileNameWithoutExtension(value);
		return value;
	}

	private static bool ContainsToken(string value, string token)
	{
		if (value.IsEmpty() || token.IsEmpty())
			return false;
		for (var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
			index >= 0;
			index = value.IndexOf(token, index + 1, StringComparison.OrdinalIgnoreCase))
		{
			var before = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
			var afterIndex = index + token.Length;
			var after = afterIndex == value.Length || !char.IsLetterOrDigit(value[afterIndex]);
			if (before && after)
				return true;
		}
		return false;
	}

	private static bool IsGenericName(string value)
	{
		if (value.IsEmpty())
			return true;
		var hasSpecificToken = false;
		foreach (var token in value.Split(['_', '-', '.', ' '],
			StringSplitOptions.RemoveEmptyEntries))
		{
			if (token.All(char.IsDigit) || IsGenericToken(token))
				continue;
			hasSpecificToken = true;
			break;
		}
		return !hasSpecificToken;
	}

	private static bool IsGenericToken(string value)
		=> value.EqualsIgnoreCase("data") || value.EqualsIgnoreCase("part") ||
			value.EqualsIgnoreCase("chunk") || value.EqualsIgnoreCase("export") ||
			value.EqualsIgnoreCase("rows") || value.EqualsIgnoreCase("equity") ||
			value.EqualsIgnoreCase("equities") || value.EqualsIgnoreCase("option") ||
			value.EqualsIgnoreCase("options") || value.EqualsIgnoreCase("future") ||
			value.EqualsIgnoreCase("futures") || value.EqualsIgnoreCase("trade") ||
			value.EqualsIgnoreCase("trades") || value.EqualsIgnoreCase("quote") ||
			value.EqualsIgnoreCase("quotes") || value.EqualsIgnoreCase("taq") ||
			value.EqualsIgnoreCase("tanq") || value.EqualsIgnoreCase("minute") ||
			value.EqualsIgnoreCase("daily") || value.EqualsIgnoreCase("ohlc") ||
			value.EqualsIgnoreCase("std") || value.EqualsIgnoreCase("standard");

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
			date = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
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

sealed class AlgoSeekOpenedSource(Stream stream, List<IDisposable> owners) : IAsyncDisposable
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

sealed class AlgoSeekDataFile(
	AlgoSeekDataSource source,
	AlgoSeekOpenedSource opened,
	StreamReader streamReader,
	AlgoSeekCsvReader reader,
	AlgoSeekCsvHeader header) : IAsyncDisposable
{
	private readonly AlgoSeekOpenedSource _opened = opened;
	private readonly StreamReader _streamReader = streamReader;

	public AlgoSeekDataSource Source { get; } = source;
	public AlgoSeekCsvReader Reader { get; } = reader;
	public AlgoSeekCsvHeader Header { get; } = header;

	public async ValueTask DisposeAsync()
	{
		_streamReader.Dispose();
		await _opened.DisposeAsync();
	}
}
