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
		if (code.IsEmpty() && isin.IsEmpty())
			return default;
		return new()
		{
			SecurityCode = code,
			BoardCode = requested.BoardCode.IsEmpty(BoardCode),
			Isin = isin,
		};
	}

	public static string NormalizeSymbol(this SecurityId securityId)
	{
		var symbol = securityId.SecurityCode?.Trim();
		if (symbol.IsEmpty())
			return null;
		if (symbol.Any(character => char.IsControl(character) ||
			character is '/' or '\\' or '?' or '#'))
		{
			throw new ArgumentException(
				$"MT Newswires symbol '{symbol}' is invalid.", nameof(securityId));
		}
		return symbol;
	}
}
