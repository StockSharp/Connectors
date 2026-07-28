namespace StockSharp.OpenFigi;

public partial class OpenFigiMessageAdapter
{
	private OpenFigiRestClient _restClient;

	/// <summary>
	/// Initialize <see cref="OpenFigiMessageAdapter"/>.
	/// </summary>
	public OpenFigiMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.OpenFigi];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.IsAssociated(BoardCodes.OpenFigi);

	private OpenFigiRestClient RestClient =>
		_restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_restClient?.Dispose();
		_restClient = null;
		base.DisposeManaged();
	}
}
