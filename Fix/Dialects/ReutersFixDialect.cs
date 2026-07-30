namespace StockSharp.Fix.Dialects;

/// <summary>
/// Reuters FIX protocol dialect.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ReutersFixDialect"/>.
/// </remarks>
/// <param name="transactionIdGenerator">Transaction id generator.</param>
[MediaIcon(Media.MediaNames.reuters)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ReutersKey,
	GroupName = LocalizedStrings.StockKey)]
public class ReutersFixDialect(IdGenerator transactionIdGenerator) : BaseFixDialect(transactionIdGenerator, Encoding.UTF8)
{
#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_REUTERS";
#endif

	/// <inheritdoc />
	protected override ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		throw new UnauthorizedAccessException();
	}
}