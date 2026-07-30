namespace StockSharp.Fix.Quik.Lua;

using System.ComponentModel;

/// <summary>
/// QUIK Lua FIX protocol dialect.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QuikLuaKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MediaIcon(Media.MediaNames.quik)]
public class LuaFixDialect : DefaultFixDialect
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LuaFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public LuaFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		TimeZone = TimeHelper.Moscow;
	}

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	[Browsable(false)]
	public static IEnumerable<TimeSpan> AllTimeFrames => LuaFixExtensions.AllTimeFrames;

	/// <inheritdoc />
	public override string Name => "QuikLua";

	/// <inheritdoc />
	public override IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
		=> new DataType[]
		{
			DataType.MarketDepth,
			DataType.Level1,
			DataType.Ticks,
		}.ToAsyncEnumerable();

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

	/// <inheritdoc />
	public override Type OrderConditionType => this.IsTransactional() ? typeof(QuikOrderCondition) : null;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
	{
		if (!this.IsTransactional())
		{
			if (dataType == DataType.Ticks || dataType == DataType.Level1)
				return true;
		}

		return base.IsAllDownloadingSupported(dataType);
	}

	/// <inheritdoc />
	protected override ValueTask WriteOrderConditionAsync(IFixWriter writer, OrderCondition condition, CancellationToken cancellationToken)
	{
		return writer.WriteOrderConditionAsync((QuikOrderCondition)condition, TimeStampParser, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask<bool> ReadOrderConditionAsync(IFixReader reader, FixTags tag, Func<OrderCondition> getCondition, CancellationToken cancellationToken)
	{
		return reader.ReadOrderConditionAsync(tag, TimeStampParser, () => (QuikOrderCondition)getCondition(), cancellationToken);
	}
}
