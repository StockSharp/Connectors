namespace StockSharp.VeloData.Native;

static class VeloDataCsv
{
    private const int _maximumLineLength = 1024 * 1024;
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    public static VeloDataInstrument[] ParseInstruments(byte[] content,
        VeloDataMarketTypes marketType)
    {
        if (marketType == VeloDataMarketTypes.Unknown)
            throw new ArgumentOutOfRangeException(nameof(marketType));
        using var reader = CreateReader(content);
        var columns = ReadHeader(reader);
        EnsureColumns(columns, VeloDataColumns.Exchange, VeloDataColumns.Coin,
            VeloDataColumns.Product, VeloDataColumns.Begin);

        var result = new List<VeloDataInstrument>();
        for (var lineNumber = 2; ; lineNumber++)
        {
            var line = reader.ReadLine();
            if (line is null)
                break;
            if (line.IsEmpty())
                continue;
            var fields = ParseLine(line, lineNumber);
            EnsureFieldCount(columns, fields, lineNumber);
            var instrument = new VeloDataInstrument { MarketType = marketType };
            for (var index = 0; index < columns.Length; index++)
            {
                var field = fields[index];
                switch (columns[index])
                {
                    case VeloDataColumns.Exchange:
                        instrument.Exchange = ParseText(field, "exchange", lineNumber);
                        break;
                    case VeloDataColumns.Coin:
                        instrument.Coin = ParseText(field, "coin", lineNumber);
                        break;
                    case VeloDataColumns.Product:
                        instrument.Product = ParseText(field, "product", lineNumber);
                        break;
                    case VeloDataColumns.Begin:
                        instrument.BeginMilliseconds = ParseLong(field, "begin",
                            lineNumber);
                        break;
                    case VeloDataColumns.Depth:
                        instrument.IsDepth = ParseBoolean(field, "depth", lineNumber);
                        break;
                    default:
                        throw UnexpectedColumn(columns[index], lineNumber);
                }
            }
            ValidateInstrument(instrument, lineNumber);
            result.Add(instrument);
        }
        return [.. result];
    }

    public static VeloDataRow[] ParseRows(byte[] content)
    {
        using var reader = CreateReader(content);
        var columns = ReadHeader(reader);
        EnsureColumns(columns, VeloDataColumns.Time, VeloDataColumns.Exchange,
            VeloDataColumns.Product);

        var result = new List<VeloDataRow>();
        for (var lineNumber = 2; ; lineNumber++)
        {
            var line = reader.ReadLine();
            if (line is null)
                break;
            if (line.IsEmpty())
                continue;
            var fields = ParseLine(line, lineNumber);
            EnsureFieldCount(columns, fields, lineNumber);
            var row = new VeloDataRow();
            for (var index = 0; index < columns.Length; index++)
                Apply(row, columns[index], fields[index], lineNumber);
            ValidateRow(row, lineNumber);
            result.Add(row);
        }
        return [.. result];
    }

    private static StringReader CreateReader(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            return new(_strictUtf8.GetString(content));
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException(
                "Velo Data returned invalid UTF-8 CSV.", error);
        }
    }

    private static VeloDataColumns[] ReadHeader(StringReader reader)
    {
        var line = reader.ReadLine();
        if (line.IsEmpty())
            throw new InvalidDataException("Velo Data returned empty CSV.");
        var fields = ParseLine(line, 1);
        var columns = new VeloDataColumns[fields.Length];
        for (var index = 0; index < fields.Length; index++)
        {
            var column = fields[index].ToVeloColumn();
            if (column == VeloDataColumns.Unknown)
                throw new InvalidDataException(
                    $"Velo Data returned unsupported CSV column '{fields[index]}'.");
            if (columns.Take(index).Contains(column))
                throw new InvalidDataException(
                    $"Velo Data returned duplicate CSV column '{fields[index]}'.");
            columns[index] = column;
        }
        return columns;
    }

    private static string[] ParseLine(string line, int lineNumber)
    {
        if (line.Length > _maximumLineLength)
            throw new InvalidDataException(
                $"Velo Data CSV line {lineNumber} exceeds 1 MiB.");
        var fields = new List<string>();
        var value = new StringBuilder();
        var isQuoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (isQuoted)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        value.Append('"');
                        index++;
                    }
                    else
                        isQuoted = false;
                }
                else
                    value.Append(character);
                continue;
            }

            if (character == ',')
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else if (character == '"' && value.Length == 0)
                isQuoted = true;
            else
                value.Append(character);
        }
        if (isQuoted)
            throw new InvalidDataException(
                $"Velo Data CSV line {lineNumber} has an unterminated quote.");
        fields.Add(value.ToString());
        return [.. fields];
    }

    private static void EnsureColumns(VeloDataColumns[] columns,
        params VeloDataColumns[] required)
    {
        foreach (var column in required)
        {
            if (!columns.Contains(column))
                throw new InvalidDataException(
                    $"Velo Data CSV is missing '{column.ToWire()}'.");
        }
    }

    private static void EnsureFieldCount(VeloDataColumns[] columns,
        string[] fields, int lineNumber)
    {
        if (fields.Length != columns.Length)
            throw new InvalidDataException(
                $"Velo Data CSV line {lineNumber} has {fields.Length} fields instead of {columns.Length}.");
    }

    private static void Apply(VeloDataRow row, VeloDataColumns column,
        string field, int lineNumber)
    {
        switch (column)
        {
            case VeloDataColumns.Time:
                row.Time = ParseLong(field, "time", lineNumber);
                break;
            case VeloDataColumns.Exchange:
                row.Exchange = ParseText(field, "exchange", lineNumber);
                break;
            case VeloDataColumns.Coin:
                row.Coin = ParseText(field, "coin", lineNumber);
                break;
            case VeloDataColumns.Product:
                row.Product = ParseText(field, "product", lineNumber);
                break;
            case VeloDataColumns.OpenPrice:
                row.OpenPrice = ParseDecimal(field, "open_price", lineNumber);
                break;
            case VeloDataColumns.HighPrice:
                row.HighPrice = ParseDecimal(field, "high_price", lineNumber);
                break;
            case VeloDataColumns.LowPrice:
                row.LowPrice = ParseDecimal(field, "low_price", lineNumber);
                break;
            case VeloDataColumns.ClosePrice:
                row.ClosePrice = ParseDecimal(field, "close_price", lineNumber);
                break;
            case VeloDataColumns.CoinVolume:
                row.CoinVolume = ParseDecimal(field, "coin_volume", lineNumber);
                break;
            case VeloDataColumns.TotalTrades:
                row.TotalTrades = ParseDecimal(field, "total_trades", lineNumber);
                break;
            case VeloDataColumns.CoinOpenInterestClose:
                row.CoinOpenInterestClose = ParseDecimal(field,
                    "coin_open_interest_close", lineNumber);
                break;
            case VeloDataColumns.DvolOpen:
                row.DvolOpen = ParseDecimal(field, "dvol_open", lineNumber);
                break;
            case VeloDataColumns.DvolHigh:
                row.DvolHigh = ParseDecimal(field, "dvol_high", lineNumber);
                break;
            case VeloDataColumns.DvolLow:
                row.DvolLow = ParseDecimal(field, "dvol_low", lineNumber);
                break;
            case VeloDataColumns.DvolClose:
                row.DvolClose = ParseDecimal(field, "dvol_close", lineNumber);
                break;
            case VeloDataColumns.IndexPrice:
                row.IndexPrice = ParseDecimal(field, "index_price", lineNumber);
                break;
            default:
                throw UnexpectedColumn(column, lineNumber);
        }
    }

    private static string ParseText(string value, string name, int lineNumber)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            throw InvalidValue(name, lineNumber);
        return value;
    }

    private static long ParseLong(string value, string name, int lineNumber)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var result) || result < 0)
            throw InvalidValue(name, lineNumber);
        return result;
    }

    private static decimal? ParseDecimal(string value, string name,
        int lineNumber)
    {
        if (value.IsEmpty())
            return null;
        if (!decimal.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result))
            throw InvalidValue(name, lineNumber);
        return result;
    }

    private static bool ParseBoolean(string value, string name, int lineNumber)
    {
        if (!bool.TryParse(value, out var result))
            throw InvalidValue(name, lineNumber);
        return result;
    }

    private static void ValidateInstrument(VeloDataInstrument instrument,
        int lineNumber)
    {
        if (instrument.Exchange.IsEmpty() || instrument.Coin.IsEmpty() ||
            instrument.Product.IsEmpty())
            throw new InvalidDataException(
                $"Velo Data CSV line {lineNumber} has an incomplete instrument.");
        _ = instrument.Begin;
    }

    private static void ValidateRow(VeloDataRow row, int lineNumber)
    {
        if (row.Exchange.IsEmpty() || row.Product.IsEmpty())
            throw new InvalidDataException(
                $"Velo Data CSV line {lineNumber} has incomplete row identity.");
        _ = row.Time.FromVeloMilliseconds();
    }

    private static InvalidDataException InvalidValue(string name, int lineNumber)
        => new($"Velo Data CSV line {lineNumber} has invalid '{name}'.");

    private static InvalidDataException UnexpectedColumn(VeloDataColumns column,
        int lineNumber)
        => new($"Velo Data CSV line {lineNumber} contains unexpected '{column.ToWire()}'.");
}
