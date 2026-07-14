namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Message with financial advice.
/// </summary>
public class FinancialAdviseMessage : Message, ITransactionIdMessage
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FinancialAdviseMessage"/>.
	/// </summary>
	public FinancialAdviseMessage()
		: base(ExtendedMessageTypes.FinancialAdvise)
	{
	}

	/// <inheritdoc />
	public long TransactionId { get; set; }

	/// <summary>
	/// Replaces Financial Advisor's settings.
	/// </summary>
	public bool IsReplace { get; set; }

	/// <summary>
	/// Type.
	/// </summary>
	public string AdviseType { get; set; }

	/// <summary>
	/// Data in the xml format.
	/// </summary>
	public string Data { get; set; }

	/// <summary>
	/// Create a copy of <see cref="FinancialAdviseMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		return new FinancialAdviseMessage
		{
			TransactionId = TransactionId,
			IsReplace = IsReplace,
			AdviseType = AdviseType,
			Data = Data,
		};
	}
}