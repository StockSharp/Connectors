namespace StockSharp.SecApi;

static class SecApiExtensions
{
    public const string DefaultBoard = "SECAPI";

    public static SecurityMessage ToSecurityMessage(
        this SecApiMapping mapping,
        long originalTransactionId)
    {
        var ticker = mapping.Ticker?
            .Trim()
            .ToUpperInvariant()
            .ThrowIfEmpty(nameof(mapping.Ticker));
        var cik = NormalizeCik(mapping.Cik);
        return new SecurityMessage
        {
            OriginalTransactionId = originalTransactionId,
            SecurityId = new SecurityId
            {
                SecurityCode = ticker,
                BoardCode = DefaultBoard,
                Native = cik,
                Cusip = mapping.GetPrimaryCusip(),
            },
            Name = mapping.Name.IsEmpty(ticker),
            ShortName = mapping.Name.IsEmpty(ticker),
            Class = mapping.Exchange
                .IsEmpty(mapping.Category)
                .IsEmpty(mapping.Industry),
            SecurityType = mapping.Category.ToSecurityType(),
            VolumeStep = 1,
            Multiplier = 1,
        };
    }

    public static SecurityTypes ToSecurityType(this string category)
    {
        category = category?.Trim().ToLowerInvariant();
        if (category?.Contains("etf") == true ||
            category?.Contains("exchange traded fund") == true)
        {
            return SecurityTypes.Etf;
        }
        if (category?.Contains("etn") == true ||
            category?.Contains("note") == true)
        {
            return SecurityTypes.Bond;
        }
        if (category?.Contains("warrant") == true)
            return SecurityTypes.Warrant;
        if (category?.Contains("adr") == true)
            return SecurityTypes.Adr;
        if (category?.Contains("gdr") == true)
            return SecurityTypes.Gdr;
        if (category?.Contains("fund") == true)
            return SecurityTypes.Fund;
        return SecurityTypes.Stock;
    }

    public static string GetPrimaryCusip(this SecApiMapping mapping)
        => mapping?.Cusip?
            .Split(
                [' ', ',', ';', '|'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    public static string GetTicker(this SecurityId securityId)
    {
        var ticker = securityId.SecurityCode?
            .Trim()
            .ToUpperInvariant();
        if (ticker.IsEmpty())
        {
            throw new InvalidOperationException(
                "SEC-API.io security identifier requires a ticker.");
        }
        return ValidateTicker(ticker);
    }

    public static string GetCik(this SecurityId securityId)
    {
        var native = securityId.Native as string;
        return IsCik(native)
            ? NormalizeCik(native)
            : null;
    }

    public static SecurityId Normalize(
        this SecurityId securityId,
        string ticker,
        string cik = null,
        string cusip = null)
        => new()
        {
            SecurityCode = ValidateTicker(ticker),
            BoardCode = securityId.BoardCode
                .IsEmpty(DefaultBoard),
            Native = cik.IsEmpty(securityId.Native as string),
            Cusip = securityId.Cusip.IsEmpty(cusip),
            Isin = securityId.Isin,
        };

    public static string[] ParseFormTypes(string value)
    {
        var forms = value?
            .Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(form => form.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (forms.Length == 0)
        {
            throw new InvalidOperationException(
                "At least one SEC form type is required.");
        }
        foreach (var form in forms)
        {
            if (form.Length > 20 ||
                form.Any(character =>
                    !char.IsLetterOrDigit(character) &&
                    character is not '-' and not '/'))
            {
                throw new InvalidOperationException(
                    $"SEC form type '{form}' is invalid.");
            }
        }
        return forms;
    }

    public static string BuildFormFilter(
        IEnumerable<string> formTypes)
    {
        var values = formTypes
            .Select(form => $"\"{form}\"")
            .ToArray();
        return values.Length == 1
            ? $"formType:{values[0]}"
            : $"formType:({string.Join(" OR ", values)})";
    }

    public static string ValidateTicker(string ticker)
    {
        ticker = ticker?
            .Trim()
            .ToUpperInvariant();
        if (ticker.IsEmpty() ||
            ticker.Length > 32 ||
            ticker.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not '.' and not '-'))
        {
            throw new InvalidOperationException(
                "SEC-API.io ticker is invalid.");
        }
        return ticker;
    }

    public static string NormalizeCik(string cik)
    {
        cik = cik?.Trim();
        if (!IsCik(cik))
            throw new InvalidOperationException("SEC CIK is invalid.");
        cik = cik.TrimStart('0');
        return cik.IsEmpty() ? "0" : cik;
    }

    public static bool IsCik(string value)
        => !value.IsEmpty() &&
            value.Length <= 10 &&
            value.All(char.IsDigit);

    public static bool IsAccessionNumber(string value)
    {
        value = value?.Trim();
        return value?.Length == 20 &&
            value[10] == '-' &&
            value[13] == '-' &&
            value.Where((_, index) =>
                index is not 10 and not 13)
                .All(char.IsDigit);
    }

    public static bool TryParseUtc(
        string value,
        out DateTime result)
    {
        result = default;
        if (!DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces |
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return false;
        }
        result = parsed.UtcDateTime;
        return true;
    }

    public static DateTime ToUtcSafe(this DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static string FormatDate(DateTime value)
        => value.ToUtcSafe().ToString(
            "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
