namespace StockSharp.Fix.Dialects;

/// <summary>
/// CME FIX protocol dialect.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CmeFixDialect"/>.
/// </remarks>
/// <param name="transactionIdGenerator">Transaction id generator.</param>
[MediaIcon(Media.MediaNames.cme)]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ChicagoMercantileExchangeKey)]
public class CmeFixDialect(IdGenerator transactionIdGenerator) : BaseFixDialect(transactionIdGenerator, Encoding.UTF8)
{
#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_CME";
#endif

	/// <inheritdoc />
	protected override ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		return base.OnWriteAsync(writer, message, cancellationToken);
	}
}