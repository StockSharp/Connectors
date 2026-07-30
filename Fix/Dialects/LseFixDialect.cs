namespace StockSharp.Fix.Dialects;

/// <summary>
/// LSE FIX protocol dialect.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="LseFixDialect"/>.
/// </remarks>
/// <param name="transactionIdGenerator">Transaction id generator.</param>
[MediaIcon(Media.MediaNames.lse)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LondonStockExchangeKey,
	GroupName = LocalizedStrings.StockKey)]
public class LseFixDialect(IdGenerator transactionIdGenerator) : BaseFixDialect(transactionIdGenerator, Encoding.UTF8)
{
#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_LSE";
#endif

	/// <inheritdoc />
	protected override ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		return base.OnWriteAsync(writer, message, cancellationToken);
	}
}