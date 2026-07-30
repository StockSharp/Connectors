namespace StockSharp.Fix.Dialects;

/// <summary>
/// CBOE FIX protocol dialect.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CboeFixDialect"/>.
/// </remarks>
/// <param name="transactionIdGenerator">Transaction id generator.</param>
[MediaIcon(Media.MediaNames.cboe)]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CboeKey)]
public class CboeFixDialect(IdGenerator transactionIdGenerator) : BaseFixDialect(transactionIdGenerator, Encoding.UTF8)
{
#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_CBOE";
#endif

	/// <inheritdoc />
	protected override ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		return base.OnWriteAsync(writer, message, cancellationToken);
	}
}