namespace StockSharp.Fix.Dialects;

/// <summary>
/// ICE FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.ice)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.IceKey,
	GroupName = LocalizedStrings.StockKey)]
public class IceFixDialect : BaseFixDialect
{
	/// <summary>
	/// Initializes a new instance of the <see cref="IceFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public IceFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8)
	{
	}

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_ICE";
#endif

	/// <inheritdoc />
	protected override ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		throw new UnauthorizedAccessException();
	}
}