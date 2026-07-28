namespace StockSharp.SimFin.Native;

static class SimFinExtensions
{
	private static readonly HashSet<string> _metadataColumns =
		new(StringComparer.OrdinalIgnoreCase)
		{
			"SimFinId",
			"Currency",
			"Fiscal Year",
			"Fiscal Period",
			"Report Date",
			"Publish Date",
			"Restated",
			"Source",
			"TTM",
			"Value Check",
		};

	public static SimFinCompany[] ToCompanies(this JToken token)
	{
		if (token is JObject compact &&
			compact["columns"] is JArray columns &&
			compact["data"] is JArray data)
			return data.OfType<JArray>()
				.Select(row => Row(columns, row))
				.Select(ToCompany)
				.Where(static company => company.Id > 0 &&
					!company.Ticker.IsEmpty())
				.ToArray();
		if (token is not JArray array)
			throw new InvalidDataException(
				"SimFin company list returned an invalid response.");
		return array.OfType<JObject>()
			.Select(ToCompany)
			.Where(static company => company.Id > 0 &&
				!company.Ticker.IsEmpty())
			.ToArray();
	}

	public static SimFinPrice[] ToPrices(this JToken token)
	{
		if (token is not JArray companies)
			throw new InvalidDataException(
				"SimFin prices returned an invalid response.");
		var result = new List<SimFinPrice>();
		foreach (var company in companies.OfType<JObject>())
		{
			var columns = company["columns"] as JArray;
			var data = company["data"] as JArray;
			if (columns is null || data is null)
				continue;
			foreach (var row in Rows(data, columns.Count))
			{
				var value = Row(columns, row);
				var date = value.Date("Date");
				if (date is null)
					continue;
				result.Add(new()
				{
					CompanyId = value.Long("SimFinId", "id") ??
						company.Value<long?>("id") ?? 0,
					Ticker = company.Value<string>("ticker"),
					Currency = company.Value<string>("currency"),
					Date = date.Value,
					Open = value.Decimal("Open"),
					High = value.Decimal("High"),
					Low = value.Decimal("Low"),
					Close = value.Decimal("Close"),
					AdjustedClose = value.Decimal("Adj. Close",
						"Adjusted Close", "AdjustedClose"),
					Volume = value.Decimal("Volume"),
				});
			}
		}
		return [.. result.OrderBy(static price => price.Date)];
	}

	public static SimFinFundamental[] ToFundamentals(
		this JToken token)
	{
		if (token is not JArray companies)
			throw new InvalidDataException(
				"SimFin statements returned an invalid response.");
		var result = new List<SimFinFundamental>();
		foreach (var company in companies.OfType<JObject>())
		{
			if (company["statements"] is not JArray statements)
				continue;
			foreach (var statement in statements.OfType<JObject>())
			{
				if (statement["columns"] is not JArray columns ||
					statement["data"] is not JArray data)
					continue;
				var statementType =
					statement.Value<string>("statement");
				foreach (var row in Rows(data, columns.Count))
				{
					var value = Row(columns, row);
					foreach (var property in value.Properties()
						.Where(property =>
							!_metadataColumns.Contains(
								property.Name) &&
							property.Value.Type is not
								JTokenType.Null and not
								JTokenType.Undefined))
					{
						result.Add(new()
						{
							CompanyId = company.Value<long?>("id") ??
								value.Long("SimFinId") ?? 0,
							Ticker = company.Value<string>("ticker"),
							Currency =
								company.Value<string>("currency"),
							Statement = statementType,
							Metric = property.Name,
							RawValue = property.Value.ToString(
								Formatting.None),
							Value = property.Value.ToDecimal(),
							FiscalYear = value.Int("Fiscal Year"),
							FiscalPeriod =
								value.String("Fiscal Period"),
							ReportDate = value.Date("Report Date"),
							PublishDate = value.Date("Publish Date"),
							Source = value.String("Source"),
							Restated = value.Bool("Restated"),
						});
					}
				}
			}
		}
		return [.. result.OrderBy(static item =>
			item.ReportDate ?? item.PublishDate)];
	}

	public static SecurityId ToSecurityId(this SimFinCompany company)
		=> new()
		{
			SecurityCode = company.Ticker,
			BoardCode = BoardCodes.SimFin,
			Native = company.Id,
			Isin = company.Isin,
		};

	public static SecurityMessage ToSecurityMessage(
		this SimFinCompany company, long transactionId)
		=> new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = company.ToSecurityId(),
			Name = company.Name,
			ShortName = company.Ticker,
			Class = company.IndustryName
				.IsEmpty(company.SectorName)
				.IsEmpty(company.SectorCode),
			SecurityType = SecurityTypes.Stock,
		};

	private static SimFinCompany ToCompany(JObject value)
		=> new()
		{
			Id = value.Long("id", "SimFinId") ?? 0,
			Name = value.String("name", "companyName"),
			Ticker = value.String("ticker"),
			Isin = value.String("isin"),
			SectorCode = value.String("sectorCode"),
			SectorName = value.String("sectorName"),
			IndustryName = value.String("industryName"),
			Market = value.String("market"),
			Description = value.String("companyDescription"),
		};

	private static IEnumerable<JArray> Rows(JArray value,
		int columnCount)
	{
		foreach (var item in value)
		{
			if (item is not JArray array)
				continue;
			if (array.Count == columnCount &&
				array.All(static child => child is not JArray))
				yield return array;
			else
				foreach (var row in Rows(array, columnCount))
					yield return row;
		}
	}

	private static JObject Row(JArray columns, JArray values)
	{
		var result = new JObject();
		for (var i = 0; i < columns.Count && i < values.Count; i++)
		{
			var name = columns[i]?.Value<string>();
			if (!name.IsEmpty())
				result[name] = values[i];
		}
		return result;
	}

	private static JToken Get(this JObject value,
		params string[] names)
	{
		foreach (var name in names)
		{
			var property = value.Properties().FirstOrDefault(
				item => item.Name.EqualsIgnoreCase(name));
			if (property is not null)
				return property.Value;
		}
		return null;
	}

	private static string String(this JObject value,
		params string[] names)
		=> value.Get(names)?.Value<string>();

	private static long? Long(this JObject value,
		params string[] names)
		=> value.Get(names)?.Value<long?>();

	private static int? Int(this JObject value,
		params string[] names)
		=> value.Get(names)?.Value<int?>();

	private static bool? Bool(this JObject value,
		params string[] names)
		=> value.Get(names)?.Value<bool?>();

	private static decimal? Decimal(this JObject value,
		params string[] names)
		=> value.Get(names).ToDecimal();

	private static decimal? ToDecimal(this JToken value)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;
		if (value.Type is JTokenType.Integer or JTokenType.Float)
			return value.Value<decimal>();
		return decimal.TryParse(value.Value<string>(),
			NumberStyles.Any, CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;
	}

	private static DateTime? Date(this JObject value,
		params string[] names)
	{
		var token = value.Get(names);
		if (token is null)
			return null;
		if (token.Type == JTokenType.Date)
			return token.Value<DateTime>().ToUniversalTime();
		return DateTime.TryParse(token.Value<string>(),
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
			out var result)
				? result
				: null;
	}
}
