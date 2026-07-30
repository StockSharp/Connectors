namespace StockSharp.Fix.Dialects;

using System.IO;

using Ecng.IO.Compression;

/// <summary>
/// oneZero FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.onezero)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OneZeroKey,
	GroupName = LocalizedStrings.StockKey)]
public class OneZeroFixDialect : BaseFixDialect
{
	/// <summary>
	/// Initializes a new instance of the <see cref="OneZeroFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public OneZeroFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss");
		TimeParser = new FastTimeSpanParser("hh:mm:ss.fff");
		DateParser = new FastDateTimeParser("yyyyMMdd");

		//HasPosition = false;

		ExchangeBoard = "ONZR";
	}

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_ONEZERO";
#endif

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		MessageTypes.MarketData.ToInfo(),
		MessageTypes.SecurityLookup.ToInfo(),

		//MessageTypes.Portfolio.ToInfo(),
		MessageTypes.PortfolioLookup.ToInfo(),
		MessageTypes.OrderRegister.ToInfo(),
		//MessageTypes.OrderReplace.ToInfo(),
		MessageTypes.OrderCancel.ToInfo(),
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
		foreach (var (_, body) in Properties.Resources.OneZeroSymbols.Unzip())
		{
			using var reader = new StreamReader(body);

			foreach (var line in (await reader.ReadToEndAsync(cancellationToken)).SplitByRN().Skip(1))
			{
				var parts = line.SplitByComma();

				await RaiseNewOutMessageAsync(new SecurityMessage
				{
					SecurityId = new SecurityId
					{
						SecurityCode = parts[0],
						BoardCode = ExchangeBoard,
					},
					Decimals = parts[1].To<int>(),
					OriginalTransactionId = lookupMsg.TransactionId,
				}, cancellationToken);
			}
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
					case OrderTypes.Limit:
					case OrderTypes.Market:
					case OrderTypes.Conditional:
						break;
					default:
						throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
				}

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				if (!regMsg.ClientCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.SenderSubID, cancellationToken);
					await writer.WriteAsync(regMsg.ClientCode, cancellationToken);
				}

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				await writer.WriteHandlInstAsync(regMsg, HandlInst.AutomatedExecutionOrderPrivate, cancellationToken);

				await WriteSecurityIdAsync(writer, regMsg, cancellationToken);

				await writer.WriteSideAsync(regMsg.Side, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixType(), cancellationToken);

				if (regMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(regMsg.Price, cancellationToken);
				}

				if (!regMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(FixTags.Text, cancellationToken);
					await writer.WriteAsync(regMsg.Comment, cancellationToken);
				}

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixTimeInForce(), cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.MarketData:
			{
				var mdMsg = (MarketDataMessage)message;

				if (mdMsg.DataType2 == DataType.Level1 ||
					mdMsg.DataType2 == DataType.MarketDepth)
				{
				}
				else
				{
					return null;
				}

				await writer.WriteSubscriptionAsync(mdMsg, cancellationToken);

				await writer.WriteAsync(FixTags.MarketDepth, cancellationToken);
				await writer.WriteAsync(mdMsg.MaxDepth ?? 0, cancellationToken);

				await writer.WriteAsync(FixTags.MDUpdateType, cancellationToken);
				await writer.WriteAsync((int)MDUpdateType.FullRefresh, cancellationToken);

				await writer.WriteAsync(FixTags.NoMDEntryTypes, cancellationToken);
				await writer.WriteAsync(2, cancellationToken);

				await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
				await writer.WriteAsync(MDEntryType.Bid, cancellationToken);

				await writer.WriteAsync(FixTags.MDEntryType, cancellationToken);
				await writer.WriteAsync(MDEntryType.Offer, cancellationToken);

				await writer.WriteAsync(FixTags.NoRelatedSym, cancellationToken);
				await writer.WriteAsync(1, cancellationToken);

				await WriteSecurityIdAsync(writer, mdMsg, cancellationToken);

				return FixMessages.MarketDataRequest;
			}

			case MessageTypes.PortfolioLookup:
			{
				var pfLookup = (PortfolioLookupMessage)message;

				await RaiseNewOutMessageAsync(new PortfolioMessage
				{
					PortfolioName = GetSyntheticPortfolioName(),
					OriginalTransactionId = pfLookup.TransactionId
				}, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.PosReqID, cancellationToken);
				await writer.WriteAsync(pfLookup.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.PosReqType, cancellationToken);
				await writer.WriteAsync((int)PosReqType.Positions, cancellationToken);

				await writer.WriteAsync(FixTags.NoPartyIDs, cancellationToken);
				await writer.WriteAsync(1, cancellationToken);

				await writer.WriteAsync(FixTags.PartyID, cancellationToken);
				await writer.WriteAsync(pfLookup.PortfolioName.IsEmpty() ? "*" : pfLookup.PortfolioName, cancellationToken);

				return FixMessages.RequestForPositions;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityMessage secMsg, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(secMsg.SecurityId.SecurityCode, cancellationToken);
	}

	/// <inheritdoc />
	protected override OrderStates? GetOrderState(ExecutionReport report)
	{
		if (report.OrdStatus == OrdStatus.PendingNew)
		{
			return OrderStates.Active;
		}

		return base.GetOrderState(report);
	}
}