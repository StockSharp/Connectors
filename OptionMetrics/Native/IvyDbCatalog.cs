namespace StockSharp.OptionMetrics.Native;

sealed class IvyDbCatalog
{
	private static readonly (string Prefix, IvyDbFileKinds Kind)[] _prefixes =
	[
		("IVYSECUR", IvyDbFileKinds.Security),
		("IVYSECNM", IvyDbFileKinds.SecurityName),
		("IVYSECPR", IvyDbFileKinds.SecurityPrice),
		("IVYOPPRC", IvyDbFileKinds.OptionPrice),
	];

	private readonly IvyDbDataFile[] _files;

	private IvyDbCatalog(IvyDbDataFile[] files)
	{
		_files = files;
	}

	public static Task<IvyDbCatalog> LoadAsync(string dataDirectory,
		CancellationToken cancellationToken)
	{
		dataDirectory.ThrowIfEmpty(nameof(dataDirectory));
		return Task.Run(() => Load(dataDirectory, cancellationToken), cancellationToken);
	}

	private static IvyDbCatalog Load(string dataDirectory,
		CancellationToken cancellationToken)
	{
		var root = Path.GetFullPath(dataDirectory);
		if (!Directory.Exists(root))
			throw new DirectoryNotFoundException($"IvyDB data directory '{root}' was not found.");

		var found = new List<IvyDbDataFile>();
		var options = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = false,
			ReturnSpecialDirectories = false,
			AttributesToSkip = FileAttributes.ReparsePoint,
		};
		foreach (var path in Directory.EnumerateFiles(root, "*", options))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var extension = Path.GetExtension(path);
			if (extension.EqualsIgnoreCase(".txt"))
			{
				if (TryParseFileName(Path.GetFileName(path), out var kind, out var date))
					found.Add(new(path, null, kind, date));
			}
			else if (extension.EqualsIgnoreCase(".zip"))
			{
				AddArchiveFiles(path, found, cancellationToken);
			}
		}

		var files = found
			.GroupBy(file => (file.Kind, file.Date))
			.Select(group => group
				.OrderBy(file => file.EntryName == null ? 0 : 1)
				.ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
				.ThenBy(file => file.EntryName, StringComparer.OrdinalIgnoreCase)
				.First())
			.OrderBy(file => file.Date)
			.ThenBy(file => file.Kind)
			.ToArray();
		if (files.Length == 0)
		{
			throw new InvalidDataException(
				$"Directory '{root}' contains no recognized IvyDB US data files.");
		}
		return new(files);
	}

	private static void AddArchiveFiles(string path, ICollection<IvyDbDataFile> target,
		CancellationToken cancellationToken)
	{
		try
		{
			using var stream = OpenContainer(path);
			using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
			foreach (var entry in archive.Entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (entry.Length <= 0 || !TryParseFileName(
					Path.GetFileName(entry.FullName.Replace('/', Path.DirectorySeparatorChar)),
					out var kind, out var date))
				{
					continue;
				}
				target.Add(new(path, entry.FullName, kind, date));
			}
		}
		catch (InvalidDataException error)
		{
			throw new InvalidDataException($"Invalid IvyDB ZIP archive '{path}'.", error);
		}
	}

	private static bool TryParseFileName(string fileName, out IvyDbFileKinds kind,
		out DateTime date)
	{
		kind = default;
		date = default;
		if (!Path.GetExtension(fileName).EqualsIgnoreCase(".txt"))
			return false;
		var stem = Path.GetFileNameWithoutExtension(fileName);
		foreach (var value in _prefixes)
		{
			var prefix = value.Prefix + ".";
			if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				continue;
			var suffix = stem[prefix.Length..];
			if (suffix.Length == 9 && char.ToUpperInvariant(suffix[^1]) == 'D')
				suffix = suffix[..^1];
			else if (suffix.Length != 8)
				return false;
			if (!DateTime.TryParseExact(suffix, "yyyyMMdd", CultureInfo.InvariantCulture,
				DateTimeStyles.None, out var parsed))
			{
				return false;
			}
			kind = value.Kind;
			date = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
			return true;
		}
		return false;
	}

	public IvyDbDataFile GetLatest(IvyDbFileKinds kind, DateTime? onOrBefore = null)
		=> _files.LastOrDefault(file => file.Kind == kind &&
			(onOrBefore == null || file.Date <= onOrBefore.Value.Date));

	public IvyDbDataFile[] GetFiles(IvyDbFileKinds kind, DateTime? from,
		DateTime? to, long? latestCount)
	{
		IEnumerable<IvyDbDataFile> files = _files.Where(file => file.Kind == kind &&
			(from == null || file.Date >= from.Value.Date) &&
			(to == null || file.Date <= to.Value.Date));
		var values = files.OrderBy(file => file.Date).ToArray();
		if (latestCount is >= 0 && latestCount.Value < values.LongLength)
		{
			var skip = checked(values.Length - (int)latestCount.Value);
			values = values.Skip(skip).ToArray();
		}
		return values;
	}

	public async Task<IvyDbSecurityMaster> LoadSecurityMasterAsync(
		CancellationToken cancellationToken)
	{
		var securityFile = GetLatest(IvyDbFileKinds.Security) ??
			throw new InvalidDataException("The IvyDB Security table is missing.");
		var securities = new List<IvyDbSecurityRow>();
		await foreach (var row in ReadSecurities(securityFile, cancellationToken))
			securities.Add(row);
		if (securities.Count == 0)
			throw new InvalidDataException($"IvyDB Security table '{securityFile}' is empty.");

		var names = new Dictionary<long, IvyDbSecurityNameRow>();
		var aliases = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
		var nameFile = GetLatest(IvyDbFileKinds.SecurityName);
		if (nameFile != null)
		{
			await foreach (var row in ReadSecurityNames(nameFile, cancellationToken))
			{
				var alias = row.GetSymbol();
				if (!alias.IsEmpty())
				{
					if (!aliases.TryGetValue(alias, out var securityIds))
						aliases.Add(alias, securityIds = []);
					securityIds.Add(row.SecurityId);
				}
				if (!names.TryGetValue(row.SecurityId, out var current) ||
					row.EffectiveDate >= current.EffectiveDate)
				{
					names[row.SecurityId] = row;
				}
			}
		}
		return new(securities, names, aliases);
	}

	public async Task<IvyDbAdjustmentFactors> FindLatestFactorsAsync(long securityId,
		CancellationToken cancellationToken)
	{
		foreach (var file in _files.Where(file => file.Kind == IvyDbFileKinds.SecurityPrice)
			.OrderByDescending(file => file.Date))
		{
			await foreach (var row in ReadSecurityPrices(file, cancellationToken))
			{
				if (row.SecurityId == securityId)
				{
					return new(row.AdjustmentFactor.GetValueOrDefault(1m),
						row.TotalReturnAdjustmentFactor.GetValueOrDefault(
							row.AdjustmentFactor.GetValueOrDefault(1m)));
				}
			}
		}
		return new(1m, 1m);
	}

	public IAsyncEnumerable<IvyDbSecurityRow> ReadSecurities(IvyDbDataFile file,
		CancellationToken cancellationToken)
		=> ReadRows(file, "Security", IvyDbSecurityRow.Parse, cancellationToken);

	public IAsyncEnumerable<IvyDbSecurityNameRow> ReadSecurityNames(IvyDbDataFile file,
		CancellationToken cancellationToken)
		=> ReadRows(file, "Security Name", IvyDbSecurityNameRow.Parse, cancellationToken);

	public IAsyncEnumerable<IvyDbSecurityPriceRow> ReadSecurityPrices(IvyDbDataFile file,
		CancellationToken cancellationToken)
		=> ReadRows(file, "Security Price", IvyDbSecurityPriceRow.Parse, cancellationToken);

	public IAsyncEnumerable<IvyDbOptionPriceRow> ReadOptionPrices(IvyDbDataFile file,
		CancellationToken cancellationToken)
		=> ReadRows(file, "Option Price", IvyDbOptionPriceRow.Parse, cancellationToken);

	private static async IAsyncEnumerable<T> ReadRows<T>(IvyDbDataFile file,
		string table, Func<string, T> parser,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var source = file.Open();
		long lineNumber = 0;
		while (await source.Reader.ReadLineAsync(cancellationToken) is { } line)
		{
			lineNumber++;
			if (IsIgnoredLine(line))
				continue;
			T row;
			try
			{
				row = parser(line);
			}
			catch (Exception error) when (error is FormatException or OverflowException)
			{
				throw new InvalidDataException(
					$"Invalid IvyDB {table} row in '{file}' at line {lineNumber}.", error);
			}
			yield return row;
		}
	}

	private static bool IsIgnoredLine(string line)
	{
		var value = line.AsSpan().Trim();
		if (value.IsEmpty || value[0] == '#')
			return true;
		var tab = value.IndexOf('\t');
		var first = (tab < 0 ? value : value[..tab]).Trim();
		return first.Equals("SecurityID", StringComparison.OrdinalIgnoreCase) ||
			first.Equals("SecurityId", StringComparison.OrdinalIgnoreCase);
	}

	internal static FileStream OpenContainer(string path)
		=> new(path, FileMode.Open, FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
}

sealed record IvyDbDataFile(string FullPath, string EntryName,
	IvyDbFileKinds Kind, DateTime Date)
{
	public IvyDbFileReader Open()
		=> new(this);

	public override string ToString()
		=> EntryName == null ? FullPath : $"{FullPath}::{EntryName}";
}

sealed class IvyDbFileReader : IDisposable
{
	private readonly FileStream _container;
	private readonly ZipArchive _archive;

	public IvyDbFileReader(IvyDbDataFile file)
	{
		_container = IvyDbCatalog.OpenContainer(file.FullPath);
		Stream data;
		if (file.EntryName == null)
			data = _container;
		else
		{
			_archive = new(_container, ZipArchiveMode.Read, true);
			var entry = _archive.GetEntry(file.EntryName) ??
				throw new InvalidDataException(
					$"IvyDB ZIP entry '{file.EntryName}' is missing from '{file.FullPath}'.");
			data = entry.Open();
		}
		Reader = new(data, Encoding.UTF8, true, 64 * 1024, file.EntryName != null);
	}

	public StreamReader Reader { get; }

	public void Dispose()
	{
		Reader.Dispose();
		_archive?.Dispose();
		_container.Dispose();
	}
}

readonly record struct IvyDbAdjustmentFactors(decimal Split, decimal TotalReturn);

sealed class IvyDbSecurityMaster
{
	private readonly Dictionary<long, IvyDbSecurityRow> _byId;
	private readonly Dictionary<string, IvyDbSecurityRow[]> _byCode;
	private readonly IReadOnlyDictionary<long, IvyDbSecurityNameRow> _names;

	public IvyDbSecurityMaster(IEnumerable<IvyDbSecurityRow> securities,
		IReadOnlyDictionary<long, IvyDbSecurityNameRow> names,
		IReadOnlyDictionary<string, HashSet<long>> aliases)
	{
		_byId = new();
		foreach (var security in securities)
			_byId[security.SecurityId] = security;

		var codes = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
		foreach (var security in _byId.Values)
		{
			var code = GetSymbol(security);
			if (!code.IsEmpty())
			{
				if (!codes.TryGetValue(code, out var securityIds))
					codes.Add(code, securityIds = []);
				securityIds.Add(security.SecurityId);
			}
		}
		_names = names;
		foreach (var alias in aliases)
		{
			if (!codes.TryGetValue(alias.Key, out var securityIds))
				codes.Add(alias.Key, securityIds = []);
			foreach (var securityId in alias.Value)
			{
				if (_byId.ContainsKey(securityId))
					securityIds.Add(securityId);
			}
		}
		_byCode = codes.ToDictionary(pair => pair.Key,
			pair => pair.Value.Select(Find).Where(value => value != null).ToArray(),
			StringComparer.OrdinalIgnoreCase);
		Securities = _byId.Values.OrderBy(GetSymbol,
			StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public IReadOnlyList<IvyDbSecurityRow> Securities { get; }

	public IvyDbSecurityRow Find(long securityId)
		=> _byId.TryGetValue(securityId, out var value) ? value : null;

	public IvyDbSecurityRow Find(string securityCode)
		=> FindAll(securityCode) is { Count: 1 } values ? values[0] : null;

	public IReadOnlyList<IvyDbSecurityRow> FindAll(string securityCode)
		=> !securityCode.IsEmpty() &&
			_byCode.TryGetValue(securityCode.Trim(), out var values) ? values : [];

	public IvyDbSecurityNameRow GetName(long securityId)
		=> _names.TryGetValue(securityId, out var value) ? value : null;

	public string GetSymbol(long securityId)
		=> Find(securityId) is { } value ? GetSymbol(value) : null;

	public static string GetSymbol(IvyDbSecurityRow value)
	{
		var ticker = value?.Ticker?.Trim().ToUpperInvariant();
		var classCode = value?.ClassCode?.Trim().ToUpperInvariant();
		if (ticker.IsEmpty() || classCode.IsEmpty() ||
			ticker.EndsWith('.' + classCode, StringComparison.OrdinalIgnoreCase))
		{
			return ticker;
		}
		return $"{ticker}.{classCode}";
	}
}
