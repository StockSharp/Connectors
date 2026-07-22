namespace StockSharp.Talos;

/// <summary>Talos FIX 4.4 protocol profile.</summary>
[MediaIcon(Media.MediaNames.talos)]
[Display(
	Name = "Talos")]
public sealed class TalosFixDialect : DefaultFixDialect
{
	/// <summary>Initializes the Talos FIX profile.</summary>
	public TalosFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ExchangeBoard = BoardCodes.Talos;
	}

	/// <inheritdoc />
	public override string FeatureName => "Talos";

	/// <inheritdoc />
	public override MessageAdapterCategories Categories =>
		MessageAdapterCategories.Crypto |
		MessageAdapterCategories.RealTime |
		MessageAdapterCategories.Paid |
		MessageAdapterCategories.Transactions;

	/// <inheritdoc />
	protected override string GetBoardCode(string destination, string exchange,
		string tradingSession)
		=> BoardCodes.Talos;
}
