namespace StockSharp.SecEdgar;

public partial class SecEdgarMessageAdapter
{
	private readonly SemaphoreSlim _companiesSync = new(1, 1);
	private SecEdgarRestClient _restClient;
	private SecEdgarCompany[] _companies;

	/// <summary>Initialize the adapter.</summary>
	public SecEdgarMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.News);
		this.AddSupportedMarketDataType(
			SecEdgarDataTypes.CompanyFacts);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
	[
		BoardCodes.SecEdgar,
		BoardCodes.Nasdaq,
		BoardCodes.Nyse,
		BoardCodes.Amex,
		BoardCodes.Arca,
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			AssociatedBoards.Any(board =>
				board.EqualsIgnoreCase(securityId.BoardCode));

	private SecEdgarRestClient RestClient =>
		_restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private async ValueTask<SecEdgarCompany[]> GetCompaniesAsync(
		CancellationToken cancellationToken)
	{
		if (_companies is not null)
			return _companies;
		await _companiesSync.WaitAsync(cancellationToken);
		try
		{
			if (_companies is null)
				_companies = (await RestClient.GetTickersAsync(
					cancellationToken)).ToCompanies();
			return _companies;
		}
		finally
		{
			_companiesSync.Release();
		}
	}

	private async ValueTask<SecEdgarCompany> ResolveCompanyAsync(
		SecurityId securityId, CancellationToken cancellationToken)
	{
		var value = (securityId.Native as string)
			.IsEmpty(securityId.SecurityCode)?.Trim();
		var companies = await GetCompaniesAsync(cancellationToken);
		var cik = value.NormalizeCik();
		var company = companies.FirstOrDefault(item =>
			!cik.IsEmpty() && item.Cik.EqualsIgnoreCase(cik) ||
			item.Ticker.EqualsIgnoreCase(value));
		if (company is not null)
			return company;
		if (cik.IsEmpty())
			throw new InvalidOperationException(
				$"Unknown SEC EDGAR company '{value}'.");
		var submission = await RestClient.GetSubmissionAsync(cik,
			cancellationToken);
		return submission.ToCompany(cik);
	}

	private void ClearState()
		=> _companies = null;

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_restClient?.Dispose();
		_restClient = null;
		_companiesSync.Dispose();
		base.DisposeManaged();
	}
}
