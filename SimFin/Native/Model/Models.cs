namespace StockSharp.SimFin.Native;

sealed class SimFinCompany
{
	public long Id { get; init; }
	public string Name { get; init; }
	public string Ticker { get; init; }
	public string Isin { get; init; }
	public string SectorCode { get; init; }
	public string SectorName { get; init; }
	public string IndustryName { get; init; }
	public string Market { get; init; }
	public string Description { get; init; }
}

sealed class SimFinPrice
{
	public long CompanyId { get; init; }
	public string Ticker { get; init; }
	public string Currency { get; init; }
	public DateTime Date { get; init; }
	public decimal? Open { get; init; }
	public decimal? High { get; init; }
	public decimal? Low { get; init; }
	public decimal? Close { get; init; }
	public decimal? AdjustedClose { get; init; }
	public decimal? Volume { get; init; }
}

sealed class SimFinFundamental
{
	public long CompanyId { get; init; }
	public string Ticker { get; init; }
	public string Currency { get; init; }
	public string Statement { get; init; }
	public string Metric { get; init; }
	public string RawValue { get; init; }
	public decimal? Value { get; init; }
	public int? FiscalYear { get; init; }
	public string FiscalPeriod { get; init; }
	public DateTime? ReportDate { get; init; }
	public DateTime? PublishDate { get; init; }
	public string Source { get; init; }
	public bool? Restated { get; init; }
}
