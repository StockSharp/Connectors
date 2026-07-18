namespace StockSharp.NasdaqDataLink;

static class Extensions
{
	private const string _boardCode = "NASDAQDL";

	public static bool TryParseDataLinkCode(this string value, out string databaseCode,
		out string datasetCode)
	{
		databaseCode = null;
		datasetCode = null;
		if (value.IsEmpty())
			return false;

		var separator = value.IndexOf('/');
		if (separator <= 0 || separator >= value.Length - 1 ||
			value.IndexOf('/', separator + 1) >= 0)
		{
			return false;
		}

		databaseCode = value[..separator].Trim();
		datasetCode = value[(separator + 1)..].Trim();
		return !databaseCode.IsEmpty() && !datasetCode.IsEmpty();
	}

	public static SecurityMessage ToSecurityMessage(this NasdaqDataLinkDataset dataset,
		long originalTransactionId, SecurityTypes securityType, CurrencyTypes? currency)
	{
		var code = dataset.Code;
		if (!code.TryParseDataLinkCode(out _, out _))
			throw new InvalidOperationException("Nasdaq Data Link dataset metadata has no valid code.");

		return new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new SecurityId
			{
				SecurityCode = code,
				BoardCode = _boardCode,
				Native = code,
			},
			Name = dataset.Name.IsEmpty(code),
			ShortName = dataset.Name.IsEmpty(code),
			SecurityType = securityType,
			Currency = currency,
			Class = new[]
			{
				dataset.DatabaseCode,
				dataset.Frequency == NasdaqDataLinkFrequencies.Unknown
					? null
					: dataset.Frequency.ToString(),
				dataset.IsPremium ? "Premium" : null,
			}.Where(value => !value.IsEmpty()).Join(" / "),
		};
	}

	public static bool Matches(this NasdaqDataLinkDataset dataset, string value)
	{
		if (value.IsEmpty())
			return true;
		return dataset.Code.EqualsIgnoreCase(value) ||
			dataset.DatasetCode.EqualsIgnoreCase(value) ||
			dataset.Name.ContainsIgnoreCase(value) ||
			dataset.Description.ContainsIgnoreCase(value);
	}

	public static SecurityId NormalizeDataLink(this SecurityId securityId, string code)
	{
		if (securityId.SecurityCode.IsEmpty())
			securityId.SecurityCode = code;
		if (securityId.BoardCode.IsEmpty())
			securityId.BoardCode = _boardCode;
		securityId.Native ??= code;
		return securityId;
	}
}

sealed class NasdaqDataLinkColumnMap
{
	private static readonly string[] _valueNames =
		["value", "price", "last", "rate", "yield", "indexvalue", "settle", "settlement"];
	private static readonly string[] _openNames = ["open", "openprice", "adjopen", "adjustedopen"];
	private static readonly string[] _highNames = ["high", "highprice", "adjhigh", "adjustedhigh"];
	private static readonly string[] _lowNames = ["low", "lowprice", "adjlow", "adjustedlow"];
	private static readonly string[] _closeNames =
		["close", "closeprice", "adjclose", "adjustedclose", "settle", "settlement"];
	private static readonly string[] _volumeNames =
		["volume", "totalvolume", "vol", "adjvolume", "adjustedvolume"];
	private static readonly string[] _openInterestNames = ["openinterest", "openint", "oi"];

	public NasdaqDataLinkColumnMap(string[] columnNames, string valueColumn,
		string openColumn, string highColumn, string lowColumn, string closeColumn,
		string volumeColumn, string openInterestColumn)
	{
		ColumnNames = columnNames ?? throw new ArgumentNullException(nameof(columnNames));
		if (columnNames.Length < 2)
			throw new InvalidOperationException(
				"Nasdaq Data Link response must contain a date and at least one value column.");

		Value = Find(valueColumn, _valueNames, true);
		Open = Find(openColumn, _openNames, false);
		High = Find(highColumn, _highNames, false);
		Low = Find(lowColumn, _lowNames, false);
		Close = Find(closeColumn, _closeNames, false);
		Volume = Find(volumeColumn, _volumeNames, false);
		OpenInterest = Find(openInterestColumn, _openInterestNames, false);
	}

	public string[] ColumnNames { get; }
	public int Value { get; }
	public int Open { get; }
	public int High { get; }
	public int Low { get; }
	public int Close { get; }
	public int Volume { get; }
	public int OpenInterest { get; }

	public bool HasOhlc => Open >= 0 && High >= 0 && Low >= 0 && Close >= 0;

	public void Validate(NasdaqDataLinkRow row)
	{
		if (row == null)
			throw new InvalidOperationException("Nasdaq Data Link returned a null observation row.");
		var expected = ColumnNames.Length - 1;
		if (row.Values?.Length != expected)
			throw new InvalidOperationException(
				$"Nasdaq Data Link row has {row.Values?.Length ?? 0} values, but {expected} were declared.");
	}

	public decimal? Get(NasdaqDataLinkRow row, int index)
		=> index < 0 ? null : row.Values[index].ToDecimal();

	private int Find(string configuredName, string[] automaticNames, bool useFallback)
	{
		if (!configuredName.IsEmpty())
		{
			var configured = Normalize(configuredName);
			for (var column = 1; column < ColumnNames.Length; column++)
			{
				if (Normalize(ColumnNames[column]) == configured)
					return column - 1;
			}
			throw new InvalidOperationException(
				$"Nasdaq Data Link response has no configured column '{configuredName}'.");
		}

		foreach (var name in automaticNames)
		{
			for (var column = 1; column < ColumnNames.Length; column++)
			{
				if (Normalize(ColumnNames[column]) == name)
					return column - 1;
			}
		}

		return useFallback ? 0 : -1;
	}

	private static string Normalize(string value)
		=> value.IsEmpty()
			? string.Empty
			: new string(value.Where(char.IsLetterOrDigit)
				.Select(char.ToLowerInvariant).ToArray());
}
