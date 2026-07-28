namespace StockSharp.SecEdgar.Native;

sealed class SecEdgarCompany
{
	public string Cik { get; init; }
	public string Name { get; init; }
	public string Ticker { get; init; }
	public string Exchange { get; init; }

	public SecurityId ToSecurityId()
		=> new()
		{
			SecurityCode = Ticker.IsEmpty(Cik),
			BoardCode = Exchange.ToSecBoard(),
			Native = Cik,
		};
}

sealed class SecEdgarFiling
{
	public string Cik { get; init; }
	public string CompanyName { get; init; }
	public string AccessionNumber { get; init; }
	public DateTime FilingDate { get; init; }
	public DateTime? ReportDate { get; init; }
	public DateTime? AcceptanceDateTime { get; init; }
	public string Form { get; init; }
	public string FileNumber { get; init; }
	public string Items { get; init; }
	public long? Size { get; init; }
	public bool IsXbrl { get; init; }
	public bool IsInlineXbrl { get; init; }
	public string PrimaryDocument { get; init; }
	public string Description { get; init; }
}

sealed class SecEdgarFact
{
	public string Taxonomy { get; init; }
	public string Concept { get; init; }
	public string Label { get; init; }
	public string Description { get; init; }
	public string Unit { get; init; }
	public string Value { get; init; }
	public decimal? NumericValue { get; init; }
	public DateTime? StartDate { get; init; }
	public DateTime? EndDate { get; init; }
	public DateTime FiledDate { get; init; }
	public string AccessionNumber { get; init; }
	public int? FiscalYear { get; init; }
	public string FiscalPeriod { get; init; }
	public string Form { get; init; }
	public string Frame { get; init; }
}
