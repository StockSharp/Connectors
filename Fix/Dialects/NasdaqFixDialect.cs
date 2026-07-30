namespace StockSharp.Fix.Dialects;

/// <summary>
/// NASDAQ FIX protocol dialect.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="NasdaqFixDialect"/>.
/// </remarks>
/// <param name="transactionIdGenerator">Transaction id generator.</param>
[MediaIcon(Media.MediaNames.nasdaq)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.NASDAQKey,
	GroupName = LocalizedStrings.StockKey)]
public class NasdaqFixDialect(IdGenerator transactionIdGenerator) : BaseFixDialect(transactionIdGenerator, Encoding.UTF8)
{
#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_NASDAQ";
#endif

	/// <inheritdoc />
	protected override ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		return base.OnWriteAsync(writer, message, cancellationToken);
	}
}