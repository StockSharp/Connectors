namespace StockSharp.Connectors.Common;

using System.IO;
using System.Net.Http;
using System.Xml.Linq;

using Ecng.Common;

/// <summary>
/// Helper for working with AWS S3 buckets via direct HTTP (without AWS SDK).
/// </summary>
static class S3BucketHelper
{
	/// <summary>
	/// Gets the date range from S3 bucket by listing folder prefixes.
	/// Uses folder-based date extraction (e.g., "prefix/202401/" -> January 2024).
	/// </summary>
	/// <param name="client">HTTP client to use.</param>
	/// <param name="bucketName">S3 bucket name.</param>
	/// <param name="region">AWS region (e.g., "ap-northeast-1").</param>
	/// <param name="prefix">Prefix to search (e.g., "spot/deals/").</param>
	/// <param name="dateFormat">Date format in folder names: "yyyyMM" for monthly, "yyyyMMdd" for daily.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <param name="baseUrl">Optional custom base URL for S3-compatible endpoints. If null, uses virtual-hosted style.</param>
	/// <returns>Min and max dates, or null if no data found or error occurred.</returns>
	public static async ValueTask<(DateTime min, DateTime max)?> GetDateRangeFromFoldersAsync(
		HttpClient client,
		string bucketName,
		string region,
		string prefix,
		string dateFormat,
		CancellationToken cancellationToken,
		string baseUrl = null)
	{
		var list = new List<DateTime>();
		string continuationToken = null;
		var s3Url = baseUrl ?? $"https://{bucketName}.s3.{region}.amazonaws.com/";

		while (true)
		{
			var url = $"{s3Url}?list-type=2&prefix={prefix.DataEscape()}&delimiter=/&max-keys=1000";
			if (!continuationToken.IsEmpty())
			{
				if (continuationToken.StartsWith("MARKER:"))
					url += $"&marker={continuationToken[7..].DataEscape()}";
				else
					url += $"&continuation-token={continuationToken.DataEscape()}";
			}

			string xml;
			try
			{
				xml = await client.GetStringAsync(url, cancellationToken);
			}
			catch (HttpRequestException)
			{
				// Return null on HTTP errors (403, 404, etc.)
				return null;
			}

			var doc = XDocument.Parse(xml);
			var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

			// Parse CommonPrefixes (folder names)
			foreach (var prefixElement in doc.Descendants(ns + "CommonPrefixes"))
			{
				var folderPath = prefixElement.Element(ns + "Prefix")?.Value?.TrimEnd('/');
				if (folderPath.IsEmpty())
					continue;

				var lastSlash = folderPath.LastIndexOf('/');
				var dateStr = lastSlash >= 0 ? folderPath[(lastSlash + 1)..] : folderPath;

				if (TryParseDate(dateStr, dateFormat, out var date))
					list.Add(date);
			}

			// Check if truncated
			var isTruncated = doc.Descendants(ns + "IsTruncated").FirstOrDefault()?.Value;
			if (!string.Equals(isTruncated, "true", StringComparison.OrdinalIgnoreCase))
				break;

			// Get continuation token (new-style) or marker (old-style)
			continuationToken = doc.Descendants(ns + "NextContinuationToken").FirstOrDefault()?.Value;
			if (continuationToken.IsEmpty())
			{
				// Try old-style marker pagination - use the last prefix as the next marker
				var lastPrefix = doc.Descendants(ns + "CommonPrefixes").LastOrDefault()?.Element(ns + "Prefix")?.Value;
				if (lastPrefix.IsEmpty())
					break;
				continuationToken = $"MARKER:{lastPrefix}";
			}
		}

		if (list.Count > 0)
			return (list.Min(), list.Max());

		return null;
	}

	/// <summary>
	/// Gets the date range from S3 bucket by listing files.
	/// Uses file-based date extraction (e.g., "prefix/BTCUSDT-2024-01-01.zip" -> January 1, 2024).
	/// </summary>
	/// <param name="client">HTTP client to use.</param>
	/// <param name="bucketName">S3 bucket name.</param>
	/// <param name="region">AWS region (e.g., "ap-northeast-1").</param>
	/// <param name="prefix">Prefix to search (e.g., "data/spot/daily/klines/BTCUSDT/1m/BTCUSDT-1m-").</param>
	/// <param name="dateFormat">Date format in filenames (e.g., "yyyy-MM-dd").</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <param name="baseUrl">Optional custom base URL for S3-compatible endpoints. If null, uses virtual-hosted style.</param>
	/// <returns>Min and max dates, or null if no data found or error occurred.</returns>
	public static async ValueTask<(DateTime min, DateTime max)?> GetDateRangeFromFilesAsync(
		HttpClient client,
		string bucketName,
		string region,
		string prefix,
		string dateFormat,
		CancellationToken cancellationToken,
		string baseUrl = null)
	{
		var list = new List<DateTime>();
		string continuationToken = null;
		var s3Url = baseUrl ?? $"https://{bucketName}.s3.{region}.amazonaws.com/";

		while (true)
		{
			var url = $"{s3Url}?list-type=2&prefix={prefix.DataEscape()}&max-keys=1000";
			if (!continuationToken.IsEmpty())
			{
				if (continuationToken.StartsWith("MARKER:"))
					url += $"&marker={continuationToken[7..].DataEscape()}";
				else
					url += $"&continuation-token={continuationToken.DataEscape()}";
			}

			string xml;
			try
			{
				xml = await client.GetStringAsync(url, cancellationToken);
			}
			catch (HttpRequestException)
			{
				// Return null on HTTP errors (403, 404, etc.)
				return null;
			}

			var doc = XDocument.Parse(xml);
			var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

			// Parse Contents (file keys)
			foreach (var content in doc.Descendants(ns + "Contents"))
			{
				var key = content.Element(ns + "Key")?.Value;
				if (key.IsEmpty() || key.EndsWith("CHECKSUM", StringComparison.OrdinalIgnoreCase))
					continue;

				// Remove prefix and extension to get date part
				// Example: prefix = "data/.../BTCUSDT-trades-", key = "data/.../BTCUSDT-trades-2024-06-01.zip"
				// After removing prefix: "2024-06-01"
				var fileName = Path.GetFileNameWithoutExtension(key);
				var prefixFileName = Path.GetFileName(prefix.TrimEnd('-'));
				if (fileName.StartsWith(prefixFileName))
					fileName = fileName[prefixFileName.Length..].TrimStart('-');

				// Try to parse the date from the beginning of the remaining string
				// For "2024-06-01" with format "yyyy-MM-dd", take first 10 characters
				var dateStr = fileName.Length >= dateFormat.Length
					? fileName[..dateFormat.Length]
					: fileName;

				if (TryParseDate(dateStr, dateFormat, out var date) && date != default)
					list.Add(date);
			}

			// Check if truncated
			var isTruncated = doc.Descendants(ns + "IsTruncated").FirstOrDefault()?.Value;
			if (!string.Equals(isTruncated, "true", StringComparison.OrdinalIgnoreCase))
				break;

			// Get continuation token (new-style) or marker (old-style)
			continuationToken = doc.Descendants(ns + "NextContinuationToken").FirstOrDefault()?.Value;
			if (continuationToken.IsEmpty())
			{
				// Try old-style marker pagination - use the last key as the next marker
				var lastKey = doc.Descendants(ns + "Contents").LastOrDefault()?.Element(ns + "Key")?.Value;
				if (lastKey.IsEmpty())
					break;
				// For marker-based pagination, we need to use &marker= instead of &continuation-token=
				continuationToken = $"MARKER:{lastKey}";
			}
		}

		if (list.Count > 0)
			return (list.Min(), list.Max());

		return null;
	}

	private static bool TryParseDate(string dateStr, string format, out DateTime date)
	{
		date = default;

		if (dateStr.IsEmpty())
			return false;

		// Handle numeric formats directly for better performance
		if (format == "yyyyMM" && dateStr.Length == 6 && int.TryParse(dateStr, out var yyyymm))
		{
			var year = yyyymm / 100;
			var month = yyyymm % 100;
			if (month >= 1 && month <= 12 && year >= 2000 && year <= 2100)
			{
				date = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
				return true;
			}
		}

		if (format == "yyyyMMdd" && dateStr.Length == 8 && int.TryParse(dateStr, out var yyyymmdd))
		{
			var year = yyyymmdd / 10000;
			var month = (yyyymmdd / 100) % 100;
			var day = yyyymmdd % 100;
			if (month >= 1 && month <= 12 && day >= 1 && day <= 31 && year >= 2000 && year <= 2100)
			{
				date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
				return true;
			}
		}

		// Fallback to standard parsing
		return DateTime.TryParseExact(dateStr, format, null,
			System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
			out date);
	}
}
