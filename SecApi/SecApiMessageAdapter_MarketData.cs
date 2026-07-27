namespace StockSharp.SecApi;

public partial class SecApiMessageAdapter
{
    private static readonly string[] _xbrlFormTypes =
    [
        "10-K",
        "10-Q",
        "20-F",
        "40-F",
        "S-1",
    ];

    /// <inheritdoc />
    protected override async ValueTask MarketDataAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        if (SecApiDataTypes.TryGetKind(
            mdMsg.DataType2, out var kind))
        {
            await OnDatasetSubscriptionAsync(
                mdMsg, kind, cancellationToken);
            return;
        }
        await base.MarketDataAsync(mdMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask SecurityLookupAsync(
        SecurityLookupMessage lookupMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            lookupMsg.TransactionId, cancellationToken);
        if (lookupMsg.Skip is < 0)
            throw new ArgumentOutOfRangeException(nameof(lookupMsg.Skip));
        if (lookupMsg.Count is <= 0)
        {
            await SendSubscriptionResultAsync(
                lookupMsg, cancellationToken);
            return;
        }

        var types = lookupMsg.GetSecurityTypes();
        var (kind, value) = GetMappingLookup(lookupMsg);
        var mappings = await SafeClient().GetMapping(
            kind, value, cancellationToken);
        var skip = lookupMsg.Skip ?? 0;
        var remaining = lookupMsg.Count ?? long.MaxValue;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings ?? [])
        {
            if (mapping is null ||
                mapping.Ticker.IsEmpty() ||
                (ActiveOnly && mapping.IsDelisted == true))
            {
                continue;
            }
            var key = mapping.Id
                .IsEmpty($"{mapping.Ticker}:{mapping.Cik}:{mapping.Cusip}");
            if (!seen.Add(key))
                continue;
            var security = mapping.ToSecurityMessage(
                lookupMsg.TransactionId);
            if (!security.IsMatch(lookupMsg, types))
                continue;
            if (skip > 0)
            {
                skip--;
                continue;
            }
            if (remaining <= 0)
                break;
            await SendOutMessageAsync(
                security, cancellationToken);
            remaining--;
        }

        await SendSubscriptionResultAsync(
            lookupMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask OnNewsSubscriptionAsync(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(
                mdMsg, cancellationToken);
            return;
        }
        ValidateRange(mdMsg.From, mdMsg.To, "news");

        var limit = checked((int)Math.Min(
            mdMsg.Count ?? ResultLimit,
            ResultLimit));
        var query = BuildFilingQuery(
            mdMsg.SecurityId,
            mdMsg.From,
            mdMsg.To,
            SafeFormTypes(),
            false);
        var response = await SafeClient().SearchFilings(
            query, 0, limit, cancellationToken);
        foreach (var item in (response.Filings ?? [])
            .Where(item =>
                item is not null &&
                SecApiExtensions.TryParseUtc(
                    item.FiledAt, out _))
            .Select(item => new
            {
                Value = item,
                Time = ParseUtc(item.FiledAt),
            })
            .Where(item =>
                (mdMsg.From is null ||
                    item.Time >= mdMsg.From) &&
                (mdMsg.To is null ||
                    item.Time <= mdMsg.To))
            .OrderBy(item => item.Time)
            .Take(limit))
        {
            var securityId = GetFilingSecurityId(
                mdMsg.SecurityId, item.Value);
            await SendOutMessageAsync(
                new NewsMessage
                {
                    OriginalTransactionId = mdMsg.TransactionId,
                    ServerTime = item.Time,
                    Id = item.Value.AccessionNumber
                        .IsEmpty(item.Value.Id),
                    Headline = BuildHeadline(item.Value),
                    Story = item.Value.Description,
                    Source = "SEC-API.io",
                    Url = GetFilingUrl(item.Value),
                    SecurityId = securityId,
                },
                cancellationToken);
        }

        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private async ValueTask OnDatasetSubscriptionAsync(
        MarketDataMessage mdMsg,
        SecApiDataKinds kind,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionReplyAsync(
            mdMsg.TransactionId, cancellationToken);
        if (!mdMsg.IsSubscribe)
        {
            await SendSubscriptionResultAsync(
                mdMsg, cancellationToken);
            return;
        }
        if (mdMsg.Count is <= 0)
        {
            await CompleteSubscription(
                mdMsg, cancellationToken);
            return;
        }
        ValidateRange(mdMsg.From, mdMsg.To, "dataset");

        var limit = checked((int)Math.Min(
            mdMsg.Count ?? ResultLimit,
            ResultLimit));
        SecApiRawResponse response;
        switch (kind)
        {
            case SecApiDataKinds.Filings:
                response = await SafeClient().SearchRaw(
                    string.Empty,
                    BuildFilingQuery(
                        mdMsg.SecurityId,
                        mdMsg.From,
                        mdMsg.To,
                        SafeFormTypes(),
                        false),
                    0,
                    limit,
                    "filing search",
                    cancellationToken);
                break;

            case SecApiDataKinds.Xbrl:
                var accessionNumber =
                    await ResolveAccessionNumber(
                        mdMsg, cancellationToken);
                response = await SafeClient().GetXbrl(
                    accessionNumber, cancellationToken);
                break;

            case SecApiDataKinds.InstitutionalHoldings:
                response = await SafeClient().SearchRaw(
                    "form-13f/holdings",
                    BuildInstitutionalQuery(
                        mdMsg.SecurityId,
                        mdMsg.From,
                        mdMsg.To),
                    0,
                    limit,
                    "Form 13F holdings",
                    cancellationToken);
                break;

            case SecApiDataKinds.InsiderTrades:
                response = await SafeClient().SearchRaw(
                    "insider-trading",
                    BuildInsiderQuery(
                        mdMsg.SecurityId,
                        mdMsg.From,
                        mdMsg.To),
                    0,
                    limit,
                    "insider trading",
                    cancellationToken);
                break;

            case SecApiDataKinds.BeneficialOwnership:
                var cusip = await ResolveCusip(
                    mdMsg.SecurityId, cancellationToken);
                response = await SafeClient().SearchRaw(
                    "form-13d-13g",
                    BuildBeneficialQuery(
                        cusip,
                        mdMsg.From,
                        mdMsg.To),
                    0,
                    limit,
                    "Form 13D/13G ownership",
                    cancellationToken);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, null);
        }

        await SendOutMessageAsync(
            new SecApiDataMessage
            {
                OriginalTransactionId = mdMsg.TransactionId,
                Dataset = kind,
                SecurityId = NormalizeRequested(mdMsg.SecurityId),
                ServerTime = DateTime.UtcNow,
                Resource = response.Resource,
                Payload = response.Payload,
            },
            cancellationToken);
        await CompleteSubscription(mdMsg, cancellationToken);
    }

    private (string Kind, string Value) GetMappingLookup(
        SecurityLookupMessage lookupMsg)
    {
        if (!lookupMsg.SecurityId.Cusip.IsEmpty())
            return ("cusip", lookupMsg.SecurityId.Cusip);
        var native = lookupMsg.SecurityId.Native as string;
        if (SecApiExtensions.IsCik(native))
        {
            return (
                "cik",
                SecApiExtensions.NormalizeCik(native));
        }
        if (!lookupMsg.SecurityId.SecurityCode.IsEmpty())
        {
            return (
                "ticker",
                SecApiExtensions.ValidateTicker(
                    lookupMsg.SecurityId.SecurityCode));
        }
        var name = lookupMsg.Name
            .IsEmpty(lookupMsg.ShortName);
        return name.IsEmpty()
            ? ("exchange", DefaultExchange.Trim())
            : ("name", name.Trim());
    }

    private string BuildFilingQuery(
        SecurityId securityId,
        DateTime? from,
        DateTime? to,
        IEnumerable<string> forms,
        bool requireXbrl)
    {
        var parts = new List<string>();
        var cik = securityId.GetCik();
        if (!cik.IsEmpty())
            parts.Add($"cik:{cik}");
        else if (!securityId.SecurityCode.IsEmpty())
        {
            parts.Add(
                $"ticker:{SecApiExtensions.ValidateTicker(securityId.SecurityCode)}");
        }
        else
            parts.Add("id:*");

        parts.Add(SecApiExtensions.BuildFormFilter(forms));
        AddDateRange(parts, "filedAt", from, to);
        if (requireXbrl)
        {
            parts.Add(
                "dataFiles.description:\"XBRL INSTANCE DOCUMENT\"");
        }
        return string.Join(" AND ", parts);
    }

    private static string BuildInstitutionalQuery(
        SecurityId securityId,
        DateTime? from,
        DateTime? to)
    {
        var parts = new List<string>();
        if (!securityId.SecurityCode.IsEmpty())
        {
            parts.Add(
                $"holdings.ticker:{SecApiExtensions.ValidateTicker(securityId.SecurityCode)}");
        }
        else if (!securityId.Cusip.IsEmpty())
            parts.Add($"holdings.cusip:\"{ValidateCusip(securityId.Cusip)}\"");
        else if (!securityId.GetCik().IsEmpty())
            parts.Add($"cik:{securityId.GetCik()}");
        else
            parts.Add("accessionNo:*");
        AddDateRange(parts, "periodOfReport", from, to);
        return string.Join(" AND ", parts);
    }

    private static string BuildInsiderQuery(
        SecurityId securityId,
        DateTime? from,
        DateTime? to)
    {
        var parts = new List<string>();
        if (!securityId.SecurityCode.IsEmpty())
        {
            parts.Add(
                $"issuer.tradingSymbol:{SecApiExtensions.ValidateTicker(securityId.SecurityCode)}");
        }
        else if (!securityId.GetCik().IsEmpty())
            parts.Add($"issuer.cik:{securityId.GetCik()}");
        else
            parts.Add("accessionNo:*");
        AddDateRange(parts, "filedAt", from, to);
        return string.Join(" AND ", parts);
    }

    private static string BuildBeneficialQuery(
        string cusip,
        DateTime? from,
        DateTime? to)
    {
        var parts = new List<string>
        {
            cusip.IsEmpty()
                ? "accessionNo:*"
                : $"cusip:\"{ValidateCusip(cusip)}\"",
        };
        AddDateRange(parts, "filedAt", from, to);
        return string.Join(" AND ", parts);
    }

    private async Task<string> ResolveAccessionNumber(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        var native = mdMsg.SecurityId.Native as string;
        if (SecApiExtensions.IsAccessionNumber(native))
            return native.Trim();
        if (SecApiExtensions.IsAccessionNumber(
            mdMsg.SecurityId.SecurityCode))
        {
            return mdMsg.SecurityId.SecurityCode.Trim();
        }

        var query = BuildFilingQuery(
            mdMsg.SecurityId,
            mdMsg.From,
            mdMsg.To,
            _xbrlFormTypes,
            true);
        var response = await SafeClient().SearchFilings(
            query, 0, 1, cancellationToken);
        var accessionNumber = response.Filings?
            .FirstOrDefault(item =>
                item is not null &&
                SecApiExtensions.IsAccessionNumber(
                    item.AccessionNumber))
            ?.AccessionNumber;
        if (accessionNumber.IsEmpty())
        {
            throw new InvalidOperationException(
                "No XBRL filing was found for the requested security and date range.");
        }
        return accessionNumber;
    }

    private async Task<string> ResolveCusip(
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        if (!securityId.Cusip.IsEmpty())
            return ValidateCusip(securityId.Cusip);
        if (securityId.SecurityCode.IsEmpty())
            return null;

        var mappings = await SafeClient().GetMapping(
            "ticker",
            SecApiExtensions.ValidateTicker(
                securityId.SecurityCode),
            cancellationToken);
        var mapping = mappings?
            .Where(item =>
                item is not null &&
                (!ActiveOnly || item.IsDelisted != true))
            .FirstOrDefault(item =>
                !item.GetPrimaryCusip().IsEmpty());
        var cusip = mapping?.GetPrimaryCusip();
        if (cusip.IsEmpty())
        {
            throw new InvalidOperationException(
                "No CUSIP mapping was found for the requested security.");
        }
        return ValidateCusip(cusip);
    }

    private static void AddDateRange(
        ICollection<string> parts,
        string field,
        DateTime? from,
        DateTime? to)
    {
        if (from is null && to is null)
            return;
        parts.Add(
            $"{field}:[" +
            $"{(from is null ? "*" : SecApiExtensions.FormatDate(from.Value))} " +
            "TO " +
            $"{(to is null ? "*" : SecApiExtensions.FormatDate(to.Value))}]");
    }

    private static string ValidateCusip(string value)
    {
        value = value?.Trim().ToUpperInvariant();
        if (value.IsEmpty() ||
            value.Length > 16 ||
            value.Any(character =>
                !char.IsLetterOrDigit(character)))
        {
            throw new InvalidOperationException("CUSIP is invalid.");
        }
        return value;
    }

    private static SecurityId GetFilingSecurityId(
        SecurityId requested,
        SecApiFiling filing)
    {
        if (!requested.SecurityCode.IsEmpty())
        {
            return requested.Normalize(
                requested.SecurityCode,
                requested.GetCik());
        }
        if (filing.Ticker.IsEmpty())
            return default;
        return new SecurityId
        {
            SecurityCode = SecApiExtensions.ValidateTicker(
                filing.Ticker),
            BoardCode = SecApiExtensions.DefaultBoard,
            Native = SecApiExtensions.IsCik(filing.Cik)
                ? SecApiExtensions.NormalizeCik(filing.Cik)
                : null,
        };
    }

    private static SecurityId NormalizeRequested(
        SecurityId securityId)
    {
        if (securityId.SecurityCode.IsEmpty())
            return securityId;
        return securityId.Normalize(
            securityId.SecurityCode,
            securityId.GetCik(),
            securityId.Cusip);
    }

    private static string BuildHeadline(SecApiFiling filing)
    {
        var form = filing.FormType.IsEmpty("SEC filing");
        return filing.CompanyName.IsEmpty()
            ? form
            : $"{form} — {filing.CompanyName}";
    }

    private static string GetFilingUrl(SecApiFiling filing)
        => NormalizeUrl(
            filing.LinkToFilingDetails
                .IsEmpty(filing.LinkToHtml)
                .IsEmpty(filing.LinkToText));

    private static string NormalizeUrl(string value)
    {
        if (value.IsEmpty())
            return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
                uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }
        return uri.AbsoluteUri;
    }

    private static DateTime ParseUtc(string value)
    {
        SecApiExtensions.TryParseUtc(value, out var result);
        return result;
    }

    private static void ValidateRange(
        DateTime? from,
        DateTime? to,
        string operation)
    {
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(
                nameof(from),
                from,
                $"The SEC-API.io {operation} start time is after its end time.");
        }
    }

    private async Task CompleteSubscription(
        MarketDataMessage mdMsg,
        CancellationToken cancellationToken)
    {
        await SendSubscriptionResultAsync(
            mdMsg, cancellationToken);
        await SendSubscriptionFinishedAsync(
            mdMsg.TransactionId, cancellationToken);
    }
}
