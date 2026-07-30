namespace StockSharp.Fix.Dialects;

/// <summary>
/// QUIK PreTrade FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.quik)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QuikPreTradeKey,
	GroupName = LocalizedStrings.RussiaKey)]
public class QuikPreTradeFixDialect : BaseFixDialect
{
	/// <summary>
	/// Initializes a new instance of the <see cref="QuikPreTradeFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public QuikPreTradeFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8, FixVersions.Fix42)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss.fff");
		TimeParser = new FastTimeSpanParser("hh:mm:ss.fff");
		DateParser = new FastDateTimeParser("yyyyMMdd");
	}

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_QUIKSERVER";
#endif

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		MessageTypes.OrderRegister.ToInfo(),
		MessageTypes.OrderReplace.ToInfo(),

		MessageTypes.ChangePassword.ToInfo(),

		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

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

				var securityId = regMsg.SecurityId;

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				if (regMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(regMsg.Price, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				if (!regMsg.ClientCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.ClientID, cancellationToken);
					await writer.WriteAsync(regMsg.ClientCode, cancellationToken);
				}

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(CreateClOrdId(regMsg), cancellationToken);

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;
				var securityId = replaceMsg.SecurityId;

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				if (!replaceMsg.ClientCode.IsEmpty())
				{
					await writer.WriteAsync(FixTags.ClientID, cancellationToken);
					await writer.WriteAsync(replaceMsg.ClientCode, cancellationToken);
				}

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(CreateClOrdId(replaceMsg), cancellationToken);

				return FixMessages.OrderCancelReplaceRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private static string CreateClOrdId(OrderMessage orderMsg)
	{
		/*
		Формат при работе с ICAP: <TRDACC>#<BROKERREF>#<TRANSID>,
		где:
		TRDACC – торговый счет в QUIK (опционально);
		BROKERREF – код клиента и, опционально, комментарий в QUIK;
		TRANSID – произвольное значение, являющееся уникальным в рамках сессии.
		Пример 1: «EBSTA01#RW331#1».
		Пример 2: «#RW331#2»
		 */
		return $"{orderMsg.PortfolioName}#{orderMsg.ClientCode}#{orderMsg.TransactionId}";
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityId securityId, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(securityId.SecurityCode, cancellationToken);

		if (!securityId.SecurityCode.IsEmpty())
		{
			await writer.WriteAsync(FixTags.SecurityID, cancellationToken);
			await writer.WriteAsync(securityId.SecurityCode, cancellationToken);

			await writer.WriteAsync(FixTags.IDSource, cancellationToken);
			await writer.WriteAsync(SecurityIDSource.ExchangeSymbol, cancellationToken);
		}
		else if (!securityId.Isin.IsEmpty())
		{
			await writer.WriteAsync(FixTags.SecurityID, cancellationToken);
			await writer.WriteAsync(securityId.Isin, cancellationToken);

			await writer.WriteAsync(FixTags.IDSource, cancellationToken);
			await writer.WriteAsync(SecurityIDSource.IsinNumber, cancellationToken);
		}

		await writer.WriteAsync(FixTags.ExDestination, cancellationToken);
		await writer.WriteAsync(securityId.BoardCode, cancellationToken);
	}
}