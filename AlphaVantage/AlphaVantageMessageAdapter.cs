namespace StockSharp.AlphaVantage;

using System;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using StockSharp.Localization;
using StockSharp.Messages;

partial class AlphaVantageMessageAdapter
{
	/// <summary>
	/// Initializes a new instance of the <see cref="AlphaVantageMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public AlphaVantageMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();

		this.AddSupportedCandleTimeFrames(AllTimeFrames);

		_alphaClient = new() { Parent = this };

		IterationInterval = TimeSpan.FromSeconds(1);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.AlphaVantage];

	/// <inheritdoc />
	protected override ValueTask ConnectAsync(ConnectMessage msg, CancellationToken token)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);

		return base.ConnectAsync(msg, token);
	}
}