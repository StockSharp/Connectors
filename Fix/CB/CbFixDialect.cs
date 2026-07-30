namespace StockSharp.Fix.CB;

/// <summary>
/// Crypto Broker FIX protocol dialect.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CBKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MediaIcon(Media.MediaNames.imex)]
public class CbFixDialect(IdGenerator transactionIdGenerator) : DefaultFixDialect(transactionIdGenerator)
{
	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages
		=> base.PossibleSupportedMessages.Where(t => t.Type != MessageTypes.News);

	/// <inheritdoc />
	public override IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
		=> base.GetSupportedMarketDataTypesAsync(securityId, from, to).Where(t => t != DataType.News);

	/// <inheritdoc />
	public override IEnumerable<Level1Fields> CandlesBuildFrom => [Level1Fields.LastTradePrice, Level1Fields.BestBidPrice, Level1Fields.BestAskPrice, Level1Fields.SpreadMiddle];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => true;

	/// <inheritdoc />
	public override bool IsSecurityRequired(DataType dataType) => false;
}