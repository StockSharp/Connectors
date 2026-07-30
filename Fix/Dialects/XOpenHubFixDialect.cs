namespace StockSharp.Fix.Dialects;

using System.IO;

using Ecng.IO.Compression;

/// <summary>
/// XOpenHub FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.xopenhub)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.XOpenHubKey,
	GroupName = LocalizedStrings.StockKey)]
public class XOpenHubFixDialect : BaseFixDialect
{
	/// <summary>
	/// Initializes a new instance of the <see cref="XOpenHubFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public XOpenHubFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8, FixVersions.Fix43)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss.fff");
		TimeParser = new FastTimeSpanParser("hh:mm:ss.fff");
		DateParser = new FastDateTimeParser("yyyyMMdd");

		//HasPosition = false;

		ExchangeBoard = "XOPHB";
	}

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_XOPENHUB";
#endif

	/// <inheritdoc />
	protected override bool LoginAsPortfolioName => true;

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		MessageTypes.MarketData.ToInfo(),
		MessageTypes.SecurityLookup.ToInfo(),

		//MessageTypes.Portfolio.ToInfo(),
		MessageTypes.PortfolioLookup.ToInfo(),
		MessageTypes.OrderRegister.ToInfo(),
		//MessageTypes.OrderReplace.ToInfo(),
		//MessageTypes.OrderCancel.ToInfo(),
		//MessageTypes.OrderGroupCancel.ToInfo(),
		//MessageTypes.OrderStatus.ToInfo(),

		//MessageTypes.ChangePassword.ToInfo(),

		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

	/// <inheritdoc />
	public override async IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
	{
		yield return DataType.MarketDepth;
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		foreach (var (_, body) in Properties.Resources.XOpenHub_FX.Unzip())
		{
			using var reader = new StreamReader(body);

			foreach (var line in (await reader.ReadToEndAsync(cancellationToken)).Split())
			{
				var parts = line.SplitByComma();

				SecurityTypes? type = null;

				switch (parts[1])
				{
					case "FX":
						type = SecurityTypes.Currency;
						break;

					case "Index":
						type = SecurityTypes.Index;
						break;

					case "Cash index":
						type = SecurityTypes.Index;
						break;

					case "Commodity":
						type = SecurityTypes.Commodity;
						break;

					case "Crypto":
						type = SecurityTypes.CryptoCurrency;
						break;
				}

				await RaiseNewOutMessageAsync(new SecurityMessage
				{
					SecurityId = new SecurityId
					{
						SecurityCode = parts[0],
						BoardCode = ExchangeBoard,
					},
					SecurityType = type,
					OriginalTransactionId = lookupMsg.TransactionId,
				}, cancellationToken);
			}
		}

		foreach (var (_, body) in Properties.Resources.XOpenHub_CFD.Unzip())
		{
			using var reader = new StreamReader(body);

			var parts = reader.ReadToEnd().SplitByComma();

			var (currency, err) = parts[7].FromMicexCurrencyName();

			await RaiseNewOutMessageAsync(new SecurityMessage
			{
				SecurityId = new SecurityId
				{
					SecurityCode = parts[0],
					BoardCode = parts[2],
					Isin = parts[1],
				},
				Decimals = parts[6].To<int?>(),
				Currency = currency,
				SecurityType = SecurityTypes.Cfd,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);

			if (err is not null)
				await RaiseNewOutMessageAsync(err.ToErrorMessage(), cancellationToken);
		}

		await RaiseNewOutMessageAsync(lookupMsg.CreateResult(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case MessageTypes.OrderRegister:
			{
				var regMsg = (OrderRegisterMessage)message;

				switch (regMsg.OrderType)
				{
					case null:
					//case OrderTypes.Limit:
					case OrderTypes.Market:
					//case OrderTypes.Conditional:
						break;
					default:
						throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
				}

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				await writer.WriteHandlInstAsync(regMsg, HandlInst.AutomatedExecutionOrderPrivate, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixType(), cancellationToken);

				await writer.WriteSideAsync(regMsg.Side, cancellationToken);

				await WriteSecurityIdAsync(writer, regMsg, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.MarketData:
			{
				var mdMsg = (MarketDataMessage)message;

				if (mdMsg.DataType2 == DataType.MarketDepth)
				{
				}
				else
				{
					return null;
				}

				await writer.WriteSubscriptionAsync(mdMsg, cancellationToken);

				await writer.WriteAsync(FixTags.MarketDepth, cancellationToken);
				await writer.WriteAsync(0, cancellationToken);

				await writer.WriteAsync(FixTags.MDUpdateType, cancellationToken);
				await writer.WriteAsync((int)MDUpdateType.FullRefresh, cancellationToken);

				await writer.WriteAsync(FixTags.NoRelatedSym, cancellationToken);
				await writer.WriteAsync(1, cancellationToken);

				await WriteSecurityIdAsync(writer, mdMsg, cancellationToken);

				await writer.WriteAsync(FixTags.NoMDEntryTypes, cancellationToken);
				await writer.WriteAsync(2, cancellationToken);

				await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
				await writer.WriteAsync(MDEntryType.Bid, cancellationToken);

				await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
				await writer.WriteAsync(MDEntryType.Offer, cancellationToken);

				return FixMessages.MarketDataRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityMessage secMsg, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(secMsg.SecurityId.SecurityCode.Remove("/"), cancellationToken);
	}

	/// <inheritdoc />
	protected override bool IsLogoutError(string text)
	{
		return !text.IsEmpty() && !text.ToLowerInvariant().EqualsIgnoreCase("Logged out");
	}
}