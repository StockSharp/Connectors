namespace StockSharp.MtNewswires;

static class MtNewswiresExtensions
{
	public const string BoardCode = "MTNW";

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static bool TryGetTime(this MtNewswiresArticle article,
		out DateTime result)
	{
		result = default;
		if (!article.ReleaseTime.IsEmpty() &&
			DateTimeOffset.TryParse(article.ReleaseTime, CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
				out var releaseTime))
		{
			result = releaseTime.UtcDateTime;
			return true;
		}
		if (!article.Date.IsEmpty() &&
			DateTime.TryParseExact(article.Date, "yyyy-MM-dd",
				CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
		{
			result = DateTime.SpecifyKind(date, DateTimeKind.Utc);
			return true;
		}
		return false;
	}

	public static string GetEventKey(this MtNewswiresArticle article,
		DateTime serverTime)
		=> article.Subkey.IsEmpty(
			$"{serverTime:O}|{article.Key}|{article.Headline}");

	public static SecurityId GetSecurityId(this MtNewswiresArticle article,
		SecurityId requested)
	{
		var code = article.Key.IsEmpty(requested.SecurityCode)?.Trim();
		var isin = article.Isin.IsEmpty(requested.Isin)?.Trim();
		if (code.IsEmpty() && isin.IsEmpty() && requested.Bloomberg.IsEmpty())
			return default;
		return new()
		{
			SecurityCode = code,
			BoardCode = requested.BoardCode.IsEmpty(BoardCode),
			Isin = isin,
			Bloomberg = requested.Bloomberg,
		};
	}

	public static string NormalizeIdentifier(this SecurityId securityId)
	{
		var identifier = securityId.SecurityCode
			.IsEmpty(securityId.Bloomberg)
			.IsEmpty(securityId.Isin)?.Trim();
		if (identifier.IsEmpty())
			return null;
		if (identifier.Any(character => char.IsControl(character) ||
			character is '/' or '\\' or '?' or '#'))
		{
			throw new ArgumentException(
				$"MT Newswires identifier '{identifier}' is invalid.", nameof(securityId));
		}
		return identifier;
	}
}
