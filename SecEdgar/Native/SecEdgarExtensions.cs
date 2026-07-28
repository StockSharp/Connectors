namespace StockSharp.SecEdgar.Native;

static class SecEdgarExtensions
{
	public static string NormalizeCik(this string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		if (value.StartsWith("CIK",
			StringComparison.OrdinalIgnoreCase))
			value = value[3..];
		if (value.IsEmpty() || !value.All(char.IsDigit) ||
			value.Length > 10)
			return null;
		value = value.TrimStart('0');
		if (value.IsEmpty())
			value = "0";
		return $"CIK{value.PadLeft(10, '0')}";
	}

	public static string CikNumber(this string cik)
		=> cik.NormalizeCik()?[3..].TrimStart('0')
			.IsEmpty("0");

	public static string ToSecBoard(this string exchange)
		=> exchange?.Trim().ToUpperInvariant() switch
		{
			"NASDAQ" => BoardCodes.Nasdaq,
			"NYSE" => BoardCodes.Nyse,
			"NYSE AMERICAN" => BoardCodes.Amex,
			"NYSE ARCA" => BoardCodes.Arca,
			_ => BoardCodes.SecEdgar,
		};

	public static SecEdgarCompany[] ToCompanies(this JObject value)
	{
		if (value?["fields"] is not JArray fields ||
			value["data"] is not JArray data)
			return [];
		var names = fields
			.Select((token, index) => (
				Name: token.Value<string>(), Index: index))
			.Where(static pair => !pair.Name.IsEmpty())
			.ToDictionary(static pair => pair.Name,
				static pair => pair.Index,
				StringComparer.OrdinalIgnoreCase);
		var result = new List<SecEdgarCompany>(data.Count);
		foreach (var row in data.OfType<JArray>())
		{
			string Get(string name)
			{
				if (!names.TryGetValue(name, out var index) ||
					index >= row.Count)
					return null;
				return row[index]?.Value<string>();
			}
			var cik = Get("cik").NormalizeCik();
			var ticker = Get("ticker")?.Trim();
			if (cik.IsEmpty() || ticker.IsEmpty())
				continue;
			result.Add(new()
			{
				Cik = cik,
				Name = Get("name")?.Trim(),
				Ticker = ticker,
				Exchange = Get("exchange")?.Trim(),
			});
		}
		return [.. result];
	}

	public static SecEdgarCompany ToCompany(this JObject value,
		string fallbackCik)
	{
		var cik = value?.Value<string>("cik").NormalizeCik()
			.IsEmpty(fallbackCik.NormalizeCik());
		var tickers = value?["tickers"] as JArray;
		var exchanges = value?["exchanges"] as JArray;
		return new()
		{
			Cik = cik,
			Name = value?.Value<string>("name"),
			Ticker = tickers?.FirstOrDefault()?.Value<string>()
				.IsEmpty(cik),
			Exchange = exchanges?.FirstOrDefault()?.Value<string>(),
		};
	}

	public static SecEdgarFiling[] ToFilings(this JObject value,
		string cik, string companyName)
	{
		if (value is null ||
			value["accessionNumber"] is not JArray accessions)
			return [];
		var result = new List<SecEdgarFiling>(accessions.Count);
		for (var index = 0; index < accessions.Count; index++)
		{
			var accession = accessions.ValueAt<string>(index);
			var filingDate = value.ArrayDate("filingDate", index);
			if (accession.IsEmpty() || filingDate is null)
				continue;
			result.Add(new()
			{
				Cik = cik.NormalizeCik(),
				CompanyName = companyName,
				AccessionNumber = accession,
				FilingDate = filingDate.Value,
				ReportDate = value.ArrayDate("reportDate", index),
				AcceptanceDateTime = value.ArrayDateTime(
					"acceptanceDateTime", index),
				Form = value.ArrayValue<string>("form", index),
				FileNumber = value.ArrayValue<string>(
					"fileNumber", index),
				Items = value.ArrayValue<string>("items", index),
				Size = value.ArrayValue<long?>("size", index),
				IsXbrl = value.ArrayBoolean("isXBRL", index),
				IsInlineXbrl = value.ArrayBoolean(
					"isInlineXBRL", index),
				PrimaryDocument = value.ArrayValue<string>(
					"primaryDocument", index),
				Description = value.ArrayValue<string>(
					"primaryDocDescription", index),
			});
		}
		return [.. result];
	}

	public static SecEdgarFact[] ToFacts(this JObject value)
	{
		if (value?["facts"] is not JObject taxonomies)
			return [];
		var result = new List<SecEdgarFact>();
		foreach (var taxonomy in taxonomies.Properties())
		{
			if (taxonomy.Value is not JObject concepts)
				continue;
			foreach (var concept in concepts.Properties())
			{
				if (concept.Value is not JObject definition ||
					definition["units"] is not JObject units)
					continue;
				var label = definition.Value<string>("label");
				var description = definition.Value<string>(
					"description");
				foreach (var unit in units.Properties())
				{
					if (unit.Value is not JArray observations)
						continue;
					foreach (var observation in
						observations.OfType<JObject>())
					{
						var filed = observation.Date("filed");
						var factValue = observation["val"];
						if (filed is null || factValue is null ||
							factValue.Type is JTokenType.Null or
								JTokenType.Undefined)
							continue;
						var text = factValue.Type == JTokenType.String
							? factValue.Value<string>()
							: factValue.ToString(Formatting.None);
						result.Add(new()
						{
							Taxonomy = taxonomy.Name,
							Concept = concept.Name,
							Label = label,
							Description = description,
							Unit = unit.Name,
							Value = text,
							NumericValue = text.ToSecDecimal(),
							StartDate = observation.Date("start"),
							EndDate = observation.Date("end"),
							FiledDate = filed.Value,
							AccessionNumber =
								observation.Value<string>("accn"),
							FiscalYear =
								observation.Value<int?>("fy"),
							FiscalPeriod =
								observation.Value<string>("fp"),
							Form = observation.Value<string>("form"),
							Frame = observation.Value<string>("frame"),
						});
					}
				}
			}
		}
		return [.. result];
	}

	public static SecurityMessage ToSecurityMessage(
		this SecEdgarCompany company, long transactionId)
		=> new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = company.ToSecurityId(),
			Name = company.Name.IsEmpty(company.Ticker),
			ShortName = company.Ticker,
			Class = company.Exchange,
			SecurityType = SecurityTypes.Stock,
			Currency = CurrencyTypes.USD,
		};

	public static NewsMessage ToNewsMessage(this SecEdgarFiling filing,
		SecEdgarCompany company, long transactionId,
		Uri websiteEndpoint)
	{
		var time = filing.AcceptanceDateTime ??
			filing.FilingDate;
		var details = new[]
		{
			$"Form: {filing.Form}",
			filing.ReportDate is null
				? null
				: $"Report date: {filing.ReportDate:yyyy-MM-dd}",
			filing.Items.IsEmpty()
				? null
				: $"Items: {filing.Items}",
			filing.FileNumber.IsEmpty()
				? null
				: $"File number: {filing.FileNumber}",
			filing.IsInlineXbrl
				? "Inline XBRL"
				: filing.IsXbrl
					? "XBRL"
					: null,
			filing.Description,
		}.Where(static item => !item.IsEmpty()).Join(Environment.NewLine);
		return new()
		{
			OriginalTransactionId = transactionId,
			ServerTime = DateTime.SpecifyKind(time,
				DateTimeKind.Utc),
			Id = filing.AccessionNumber,
			BoardCode = company.ToSecurityId().BoardCode,
			SecurityId = company.ToSecurityId(),
			Source = "SEC EDGAR",
			Headline = $"{company.Name.IsEmpty(company.Ticker)} — " +
				$"{filing.Form} filing",
			Story = details,
			Url = filing.ToArchiveUri(websiteEndpoint)?.AbsoluteUri,
			Priority = filing.Form.EqualsIgnoreCase("8-K") ||
				filing.Form.EqualsIgnoreCase("6-K")
					? NewsPriorities.High
					: NewsPriorities.Regular,
			Language = "en",
		};
	}

	public static Uri ToArchiveUri(this SecEdgarFiling filing,
		Uri websiteEndpoint)
	{
		if (websiteEndpoint is null ||
			filing.Cik.IsEmpty() ||
			filing.AccessionNumber.IsEmpty() ||
			filing.PrimaryDocument.IsEmpty())
			return null;
		var accession = filing.AccessionNumber.Replace("-",
			string.Empty, StringComparison.Ordinal);
		var document = filing.PrimaryDocument
			.Replace('\\', '/')
			.Split('/', StringSplitOptions.RemoveEmptyEntries)
			.Select(Uri.EscapeDataString)
			.Join("/");
		var path = $"Archives/edgar/data/" +
			$"{filing.Cik.CikNumber()}/{accession}/" +
			document;
		return new(EnsureTrailingSlash(websiteEndpoint), path);
	}

	public static DateTime? Date(this JObject value, string name)
		=> ParseDate(value?.Value<string>(name));

	private static DateTime? ArrayDate(this JObject value,
		string name, int index)
		=> ParseDate(value.ArrayValue<string>(name, index));

	private static DateTime? ArrayDateTime(this JObject value,
		string name, int index)
	{
		var text = value.ArrayValue<string>(name, index);
		if (text.IsEmpty())
			return null;
		return DateTimeOffset.TryParse(text,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
			out var result)
				? result.UtcDateTime
				: null;
	}

	private static DateTime? ParseDate(string value)
	{
		if (value.IsEmpty())
			return null;
		return DateTime.TryParseExact(value, "yyyy-MM-dd",
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
			out var result)
				? DateTime.SpecifyKind(result, DateTimeKind.Utc)
				: null;
	}

	private static bool ArrayBoolean(this JObject value,
		string name, int index)
	{
		var token = (value?[name] as JArray).TokenAt(index);
		if (token is null)
			return false;
		if (token.Type == JTokenType.Boolean)
			return token.Value<bool>();
		var text = token.Value<string>();
		return text.EqualsIgnoreCase("1") ||
			text.EqualsIgnoreCase("true");
	}

	private static T ArrayValue<T>(this JObject value,
		string name, int index)
		=> (value?[name] as JArray).ValueAt<T>(index);

	private static T ValueAt<T>(this JArray value, int index)
	{
		var token = value.TokenAt(index);
		return token is null ? default : token.Value<T>();
	}

	private static JToken TokenAt(this JArray value, int index)
	{
		if (value is null || index < 0 || index >= value.Count)
			return null;
		var token = value[index];
		return token is null ||
			token.Type is JTokenType.Null or JTokenType.Undefined
				? null
				: token;
	}

	private static decimal? ToSecDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any,
			CultureInfo.InvariantCulture, out var result)
				? result
				: null;

	private static Uri EnsureTrailingSlash(Uri value)
		=> value.AbsoluteUri.EndsWith("/",
			StringComparison.Ordinal)
				? value
				: new(value.AbsoluteUri + "/");
}
