namespace StockSharp.B3Up2Data.Native;

sealed class B3CsvTable
{
    private const int _maxRows = 2_000_000;

    private readonly Dictionary<string, int> _columns;

    private B3CsvTable(
        Dictionary<string, int> columns,
        B3CsvRow[] rows)
    {
        _columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<B3CsvRow> Rows { get; }

    public bool HasColumn(string name)
        => _columns.ContainsKey(name);

    public static B3CsvTable Parse(byte[] content)
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content));
        if (content.Length == 0)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA returned an empty CSV file.");
        }

        var text = Decode(content);
        var records = ParseRecords(text).GetEnumerator();
        using (records)
        {
            if (!records.MoveNext())
            {
                throw new InvalidOperationException(
                    "B3 UP2DATA CSV file has no header.");
            }

            var header = records.Current;
            var columns = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < header.Length; index++)
            {
                var name = header[index]?.Trim();
                if (!name.IsEmpty() && !columns.TryAdd(name, index))
                {
                    throw new InvalidOperationException(
                        $"B3 UP2DATA CSV contains duplicate column '{name}'.");
                }
            }
            if (columns.Count == 0)
            {
                throw new InvalidOperationException(
                    "B3 UP2DATA CSV header is empty.");
            }

            var rows = new List<B3CsvRow>();
            while (records.MoveNext())
            {
                if (rows.Count >= _maxRows)
                {
                    throw new InvalidOperationException(
                        "B3 UP2DATA CSV exceeds two million rows.");
                }
                var values = records.Current;
                if (values.All(value => value.IsEmpty()))
                    continue;
                rows.Add(new B3CsvRow(columns, values));
            }
            return new B3CsvTable(columns, [.. rows]);
        }
    }

    private static string Decode(byte[] content)
    {
        if (content.Length >= 3 &&
            content[0] == 0xEF &&
            content[1] == 0xBB &&
            content[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);
        }

        try
        {
            return new UTF8Encoding(
                false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    private static IEnumerable<string[]> ParseRecords(
        string text)
    {
        var row = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length &&
                        text[index + 1] == '"')
                    {
                        value.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    value.Append(character);
                }
                continue;
            }

            switch (character)
            {
                case '"' when value.Length == 0:
                    quoted = true;
                    break;
                case ';':
                    row.Add(value.ToString());
                    value.Clear();
                    break;
                case '\r':
                    if (index + 1 < text.Length &&
                        text[index + 1] == '\n')
                    {
                        index++;
                    }
                    row.Add(value.ToString());
                    value.Clear();
                    yield return [.. row];
                    row.Clear();
                    break;
                case '\n':
                    row.Add(value.ToString());
                    value.Clear();
                    yield return [.. row];
                    row.Clear();
                    break;
                default:
                    value.Append(character);
                    break;
            }
        }

        if (quoted)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA CSV contains an unterminated quoted value.");
        }
        if (value.Length > 0 || row.Count > 0)
        {
            row.Add(value.ToString());
            yield return [.. row];
        }
    }
}

sealed class B3CsvRow
{
    private readonly IReadOnlyDictionary<string, int> _columns;
    private readonly string[] _values;

    public B3CsvRow(
        IReadOnlyDictionary<string, int> columns,
        string[] values)
    {
        _columns = columns ??
            throw new ArgumentNullException(nameof(columns));
        _values = values ??
            throw new ArgumentNullException(nameof(values));
    }

    public string Get(string name)
        => _columns.TryGetValue(name, out var index) &&
            index >= 0 &&
            index < _values.Length
                ? _values[index]?.Trim()
                : null;

    public decimal? GetDecimal(string name)
        => decimal.TryParse(
            Get(name),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;

    public long? GetLong(string name)
        => long.TryParse(
            Get(name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;

    public DateTime? GetDate(string name)
        => DateTime.TryParseExact(
            Get(name),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result)
                ? DateTime.SpecifyKind(result, DateTimeKind.Utc)
                : null;
}
