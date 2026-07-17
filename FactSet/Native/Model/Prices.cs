namespace StockSharp.FactSet.Native.Model;

sealed class FactSetPricesResponse
{
	[JsonProperty("data")]
	public FactSetPrice[] Data { get; set; }
}

sealed class FactSetPrice
{
	[JsonProperty("fsymId")]
	public string FsymId { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("adjDate")]
	public string AdjustmentDate { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("price")]
	public decimal? Close { get; set; }

	[JsonProperty("priceOpen")]
	public decimal? Open { get; set; }

	[JsonProperty("priceHigh")]
	public decimal? High { get; set; }

	[JsonProperty("priceLow")]
	public decimal? Low { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	public bool HasOhlc => Open != null && High != null && Low != null && Close != null;

	public DateTime? GetTime() => ParseDate(Date);

	internal static DateTime? ParseDate(string value)
	{
		if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
			return null;
		return DateTime.SpecifyKind(date, DateTimeKind.Utc);
	}
}

sealed class FactSetFixedIncomePricesResponse
{
	[JsonProperty("data")]
	public FactSetFixedIncomePrice[] Data { get; set; }
}

sealed class FactSetFixedIncomePrice
{
	[JsonProperty("fsymId")]
	public string FsymId { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("securityType")]
	public string SecurityType { get; set; }

	[JsonProperty("issuerEntityId")]
	public string IssuerEntityId { get; set; }

	[JsonProperty("issuerType")]
	public string IssuerType { get; set; }

	[JsonProperty("priceBid")]
	public decimal? Bid { get; set; }

	[JsonProperty("priceMid")]
	public decimal? Mid { get; set; }

	[JsonProperty("priceAsk")]
	public decimal? Ask { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	public DateTime? GetTime() => FactSetPrice.ParseDate(Date);
}

sealed class FactSetPricesQuery
{
	public string Id { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }
	public string Currency { get; init; }
	public string Adjustment { get; init; }

	public string ToQueryString()
	{
		var query = $"ids={Encode(Id)}&frequency=D&calendar=LOCAL" +
			$"&adjust={Encode(Adjustment)}&batch=N";
		if (From != null)
			query += $"&startDate={From:yyyy-MM-dd}";
		if (To != null)
			query += $"&endDate={To:yyyy-MM-dd}";
		if (!Currency.IsEmpty())
			query += $"&currency={Encode(Currency)}";
		return query;
	}

	private static string Encode(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);
}

sealed class FactSetFixedIncomePricesQuery
{
	public string Id { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }

	public string ToQueryString()
	{
		var query = $"ids={Uri.EscapeDataString(Id ?? string.Empty)}&frequency=D";
		if (From != null)
			query += $"&startDate={From:yyyy-MM-dd}";
		if (To != null)
			query += $"&endDate={To:yyyy-MM-dd}";
		return query;
	}
}
