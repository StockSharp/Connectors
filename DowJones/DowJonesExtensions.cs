namespace StockSharp.DowJones;

static class DowJonesExtensions
{
	public const string BoardCode = "DJ";

	public static string BuildQuery(this SecurityId securityId, string configuredQuery,
		DateTime from, DateTime to)
	{
		var filters = new List<string>();
		if (!configuredQuery.IsEmpty())
			filters.Add($"({configuredQuery.Trim()})");

		var identifier = securityId.SecurityCode;
		if (identifier.IsEmpty())
			identifier = securityId.Isin;
		if (!identifier.IsEmpty())
		{
			identifier = identifier.Trim();
			if (identifier.Any(character => char.IsWhiteSpace(character) ||
				char.IsControl(character) || character is '(' or ')' or '"' or '\''))
			{
				throw new ArgumentException(
					$"Dow Jones security identifier '{identifier}' is invalid.",
					nameof(securityId));
			}
			filters.Add($"djn:djnabout:{identifier.ToLowerInvariant()}");
		}

		filters.Add($"(pdt:>{from.FormatQueryTime()} and pdt:<{to.FormatQueryTime()})");
		return string.Join(" and ", filters);
	}

	public static string FormatQueryTime(this DateTime value)
		=> value.ToUtc().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static bool TryGetTime(this DowJonesContentResource resource,
		out DateTime result)
	{
		result = default;
		var attributes = resource?.Attributes;
		foreach (var value in new[]
		{
			attributes?.PublicationTime,
			attributes?.DistributionPublishTime,
			attributes?.ModificationTime,
			attributes?.LoadTime,
		})
		{
			if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
				out var parsed))
			{
				continue;
			}
			result = parsed.UtcDateTime;
			return true;
		}
		return false;
	}

	public static string GetHeadline(this DowJonesContentResource resource)
		=> resource?.Attributes?.Headline?.Main.GetText()
			.IsEmpty(resource?.Attributes?.Summary?.Headline?.Main.GetText());

	public static string GetSnippet(this DowJonesContentResource resource)
		=> GetText(resource?.Attributes?.Snippet?.Content);

	public static string GetArticleText(this DowJonesContentResource resource)
	{
		var body = GetText(resource?.Attributes?.Body);
		return body.IsEmpty(GetText(resource?.Attributes?.Summary?.Body))
			.IsEmpty(resource.GetSnippet());
	}

	public static string GetText(this DowJonesContentNode node)
	{
		if (node == null)
			return null;
		var builder = new StringBuilder();
		AppendText(node, builder);
		return builder.ToString().Trim();
	}

	public static string GetText(DowJonesContentNode[] nodes)
		=> nodes == null ? null : string.Join(Environment.NewLine,
			nodes.Select(GetText).Where(text => !text.IsEmpty())).Trim();

	public static SecurityId GetSecurityId(this DowJonesContentResource resource,
		SecurityId requested)
	{
		var result = requested;
		if (!result.SecurityCode.IsEmpty() || !result.Isin.IsEmpty())
		{
			result.BoardCode = result.BoardCode.IsEmpty(BoardCode);
			return result;
		}

		string ticker = null;
		string isin = null;
		foreach (var code in (resource?.Meta?.CodeSets ?? [])
			.SelectMany(set => set?.Codes ?? []))
		{
			if (code == null)
				continue;
			var scheme = code.CodeScheme.IsEmpty(code.LegacyCodeScheme);
			var value = code.Symbol.IsEmpty(code.Code)?.Trim();
			if (value.IsEmpty())
				continue;
			if (scheme.EqualsIgnoreCase("Ticker") && ticker.IsEmpty())
				ticker = value;
			else if (scheme.EqualsIgnoreCase("ISIN") && isin.IsEmpty())
				isin = value;
		}

		if (ticker.IsEmpty() && isin.IsEmpty())
			return default;
		return new()
		{
			SecurityCode = ticker,
			BoardCode = BoardCode,
			Isin = isin,
		};
	}

	public static NewsPriorities? GetPriority(this DowJonesContentResource resource)
	{
		if (resource?.Meta?.Emphasis is { } emphasis &&
			(emphasis.IsHot || emphasis.IsDominant))
		{
			return NewsPriorities.High;
		}
		return (resource?.Meta?.CodeSets ?? []).SelectMany(set => set?.Codes ?? [])
			.Any(code => code?.Significance.EqualsIgnoreCase("Significant") == true)
			? NewsPriorities.High : null;
	}

	public static string GetEventKey(this DowJonesContentResource resource)
		=> resource?.Id.IsEmpty(resource?.Meta?.OriginalDocumentId)
			.IsEmpty(resource?.Meta?.AlternateDocumentReference);

	private static void AppendText(DowJonesContentNode node, StringBuilder builder)
	{
		if (node == null)
			return;
		if (!node.Text.IsEmpty())
			builder.Append(node.Text);
		foreach (var child in node.Content ?? [])
			AppendText(child, builder);
	}
}
