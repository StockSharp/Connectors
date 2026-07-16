namespace StockSharp.IQFeed;

using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;

using Ecng.IO.Compression;

/// <summary>
/// IQFeed securities CSV file parser.
/// </summary>
public class IQFeedSecurityParser
{
	/// <summary>
	/// Default URL for downloading securities file.
	/// </summary>
	public const string DefaultSecuritiesUrl = "https://www.dtniq.com/product/mktsymbols_v2.zip";

	/// <summary>
	/// File name inside the zip archive.
	/// </summary>
	public const string FileName = "mktsymbols_v2.txt";

	private static async IAsyncEnumerable<SecurityMessage> ParseAsync(
		Stream zipStream,
		HashSet<SecurityTypes> securityTypes = null,
		long? maxCount = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		if (zipStream is null)
			throw new ArgumentNullException(nameof(zipStream));

		var filterTypes = securityTypes?.Count > 0;
		var left = maxCount ?? long.MaxValue;

		foreach (var (name, body) in zipStream.Unzip(true, e => e.EqualsIgnoreCase(FileName)))
		{
			if (body == null)
				throw new InvalidOperationException($"Unable to read file '{FileName}' in the archive");

			using var reader = new StreamReader(body);

			// Skip header line
			await reader.ReadLineAsync(cancellationToken);

			string line;
			while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var secMsg = ParseLine(line);
				if (secMsg == null)
					continue;

				if (filterTypes && (secMsg.SecurityType == null || !securityTypes.Contains(secMsg.SecurityType.Value)))
					continue;

				yield return secMsg;

				if (--left <= 0)
					yield break;
			}
		}
	}

	/// <summary>
	/// Parse securities from local file.
	/// </summary>
	/// <param name="filePath">Path to local zip file.</param>
	/// <param name="securityTypes">Filter by security types. If null or empty, all types are returned.</param>
	/// <param name="maxCount">Maximum number of securities to return. If null, all securities are returned.</param>
	/// <returns>Async enumerable of parsed securities.</returns>
	public static IAsyncEnumerable<SecurityMessage> ParseFromFileAsync(
		string filePath,
		HashSet<SecurityTypes> securityTypes = null,
		long? maxCount = null)
	{
		if (filePath.IsEmpty())
			throw new ArgumentNullException(nameof(filePath));

		return Impl();

		async IAsyncEnumerable<SecurityMessage> Impl([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await using var stream = File.OpenRead(filePath);

			await foreach (var sec in ParseAsync(stream, securityTypes, maxCount, cancellationToken))
				yield return sec;
		}
	}

	/// <summary>
	/// Download and parse securities from URL.
	/// </summary>
	/// <param name="url">URL to download zip file from. If null, <see cref="DefaultSecuritiesUrl"/> is used.</param>
	/// <param name="securityTypes">Filter by security types. If null or empty, all types are returned.</param>
	/// <param name="maxCount">Maximum number of securities to return. If null, all securities are returned.</param>
	/// <returns>Async enumerable of parsed securities.</returns>
	public static IAsyncEnumerable<SecurityMessage> ParseFromUrlAsync(
		string url = null,
		HashSet<SecurityTypes> securityTypes = null,
		long? maxCount = null)
	{
		return Impl();

		async IAsyncEnumerable<SecurityMessage> Impl([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			url ??= DefaultSecuritiesUrl;

			using var http = new HttpClient();
			await using var stream = await http.GetStreamAsync(url, cancellationToken);

			await foreach (var sec in ParseAsync(stream, securityTypes, maxCount, cancellationToken))
				yield return sec;
		}
	}

	/// <summary>
	/// Parse single line from the securities file.
	/// </summary>
	/// <param name="line">Tab-separated line.</param>
	/// <returns>Parsed <see cref="SecurityMessage"/> or null if line is invalid.</returns>
	public static SecurityMessage ParseLine(string line)
	{
		if (line.IsEmpty())
			return null;

		var parts = line.SplitByTab(false);

		if (parts.Length == 9)
		{
			// Fix incorrect tabulation, e.g.:
			// CS.17.CB	CREDIT SUISSE NEW YORK 1.375% 05/26/17		NYSE	NYSE	BONDS
			const int startExclude = 2;
			const int countExclude = 1;
			var copy = new string[parts.Length - countExclude];
			Array.Copy(parts, copy, startExclude);
			Array.Copy(parts, startExclude + countExclude, copy, startExclude, copy.Length - startExclude);
			parts = copy;
		}

		if (parts.Length < 5)
			return null;

		var secCode = parts[0];
		var secName = parts[1];
		var boardCode = parts[2];
		var secTypeStr = parts[4];

		var secType = secTypeStr.ToSecurityType();

		return new SecurityMessage
		{
			SecurityId = new SecurityId
			{
				SecurityCode = secCode,
				BoardCode = boardCode,
			},
			Name = secName,
			SecurityType = secType,
		};
	}

	/// <summary>
	/// Convert security type string to <see cref="SecurityTypes"/>.
	/// </summary>
	/// <param name="value">Security type string from IQFeed.</param>
	/// <returns>Security type or null if unknown.</returns>
	public static SecurityTypes? ToSecurityType(string value) => IQFeedHelper.ToSecurityType(value);
}
