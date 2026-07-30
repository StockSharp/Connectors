namespace StockSharp.Fix.MT;

/// <summary>
/// MT FIX protocol dialect.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MTKey,
	GroupName = LocalizedStrings.ForexKey)]
[MediaIcon(Media.MediaNames.mt5)]
public class MTFixDialect : DefaultFixDialect
{
	/// <summary>
	/// Initializes a new instance of the <see cref="MTFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public MTFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
	}

	private static readonly HashSet<MessageTypes> _approvedTypes =
	[
		MessageTypes.MarketData,
		MessageTypes.SecurityLookup,
		MessageTypes.BoardLookup,

		MessageTypes.PortfolioLookup,
		MessageTypes.OrderRegister,
		MessageTypes.OrderReplace,
		MessageTypes.OrderCancel,
		MessageTypes.OrderGroupCancel,
		MessageTypes.OrderStatus,

		//MessageTypes.ChangePassword,

		FixMessageTypes.SeqReset,
		FixMessageTypes.ResendRequest,

		MessageTypes.DataTypeLookup,
	];

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages
		=> base.PossibleSupportedMessages.Where(t => _approvedTypes.Contains(t.Type));

	/// <inheritdoc />
	public override bool ExtraSetup => true;
}
