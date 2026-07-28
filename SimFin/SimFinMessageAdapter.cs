namespace StockSharp.SimFin;

public partial class SimFinMessageAdapter
{
	private readonly SemaphoreSlim _companiesSync = new(1, 1);
	private SimFinRestClient _restClient;
	private SimFinCompany[] _companies;

	/// <summary>
	/// Initialize <see cref="SimFinMessageAdapter"/>.
	/// </summary>
	public SimFinMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(SimFinDataTypes.Fundamentals);
		this.AddSupportedCandleTimeFrames(
			[TimeSpan.FromDays(1)]);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.SimFin];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.IsAssociated(BoardCodes.SimFin);

	private SimFinRestClient RestClient =>
		_restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private async ValueTask<SimFinCompany[]> GetCompaniesAsync(
		CancellationToken cancellationToken)
	{
		if (_companies is not null)
			return _companies;
		await _companiesSync.WaitAsync(cancellationToken);
		try
		{
			if (_companies is null)
				_companies = await RestClient.GetCompaniesAsync(
					cancellationToken);
			return _companies;
		}
		finally
		{
			_companiesSync.Release();
		}
	}

	private async ValueTask<SimFinCompany> ResolveCompanyAsync(
		SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var native = securityId.Native?.ToString();
		var companies = await GetCompaniesAsync(cancellationToken);
		var company = companies.FirstOrDefault(item =>
			!native.IsEmpty() &&
				item.Id.ToString(CultureInfo.InvariantCulture) ==
					native ||
			item.Ticker.EqualsIgnoreCase(
				securityId.SecurityCode));
		return company ?? throw new InvalidOperationException(
			$"Unknown SimFin company " +
				$"'{securityId.SecurityCode.IsEmpty(native)}'.");
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
