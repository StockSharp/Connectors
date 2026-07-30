namespace StockSharp.Fix.Dialects.Moex.Spectra;

/// <summary>
/// SPECTRA FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.moex)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SpectraKey,
	GroupName = LocalizedStrings.RussiaKey)]
public class SpectraFixDialect : BaseFixDialect
{
	enum SpectraTags
	{
		// ReSharper disable InconsistentNaming
		MarketSegmentID = 1300,
		// ReSharper restore InconsistentNaming
		Flags = 20008,
	}

	private readonly FastDateTimeParser _expiryDateParser = new("yyyyMMdd");
	private const char _passiveTif = 'z';

	/// <summary>
	/// Initializes a new instance of the <see cref="SpectraFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public SpectraFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8)
	{
		TimeZone = TimeHelper.Moscow;
		//OrderLookup = true;

		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss.fffffffff");
	}

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_SPECTRA";
#endif

	//public override string StorageName => "Plaza";

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		//MessageTypes.MarketData.ToInfo(),
		//MessageTypes.SecurityLookup.ToInfo(),

		//MessageTypes.Portfolio.ToInfo(),
		//MessageTypes.PortfolioLookup.ToInfo(),
		MessageTypes.OrderRegister.ToInfo(),
		MessageTypes.OrderReplace.ToInfo(),
		MessageTypes.OrderCancel.ToInfo(),
		MessageTypes.OrderGroupCancel.ToInfo(),
		MessageTypes.OrderStatus.ToInfo(),

		//MessageTypes.ChangePassword.ToInfo(),

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

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixType(), cancellationToken);

				await WriteSecurityIdAsync(writer, regMsg, cancellationToken);

				await WriteAccountAndPartiesAsync(writer, regMsg, cancellationToken);

				var tif = regMsg.PostOnly == true ? _passiveTif : regMsg.GetFixTimeInForce();

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(tif, cancellationToken);

				await writer.WriteSideAsync(regMsg.Side, cancellationToken);

				if (regMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(FixTags.DisplayQty, cancellationToken);
					await writer.WriteAsync(regMsg.VisibleVolume.Value, cancellationToken);
				}

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				if (regMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(regMsg.Price, cancellationToken);
				}

				if (!regMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(FixTags.SecondaryClOrdID, cancellationToken);
					await writer.WriteAsync(regMsg.Comment, cancellationToken);
				}

				if (tif == FixTimeInForce.GoodTillDate)
				{
					await writer.WriteExpiryDateAsync(regMsg, _expiryDateParser, TimeZone, cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;

				if (!cancelMsg.PortfolioName.IsEmpty())
					await WriteAccountAndPartiesAsync(writer, cancelMsg, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				if (cancelMsg.OrderId != null)
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(cancelMsg.OrderId.Value, cancellationToken);
				}

				if (cancelMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
					await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, cancelMsg, cancellationToken);

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				if (cancelMsg.Volume != null)
				{
					await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
					await writer.WriteAsync(cancelMsg.Volume.Value, cancellationToken);
				}

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;

				if (!replaceMsg.PortfolioName.IsEmpty())
					await WriteAccountAndPartiesAsync(writer, replaceMsg, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				if (replaceMsg.OldOrderId != null)
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(replaceMsg.OldOrderId.Value, cancellationToken);
				}

				if (replaceMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
					await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);
				}

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, replaceMsg, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return FixMessages.OrderCancelReplaceRequest;
			}

			case MessageTypes.OrderGroupCancel:
			{
				var cancelMsg = (OrderGroupCancelMessage)message;

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				if (!cancelMsg.PortfolioName.IsEmpty())
					await WriteAccountAndPartiesAsync(writer, cancelMsg, cancellationToken);
				else
				{
					await writer.WriteAsync(FixTags.Account, cancellationToken);
					await writer.WriteAsync("%%%", cancellationToken);
				}

				char requestType;

				if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
				{
					requestType = MassCancelRequestType.CancelOrdersForSecurity;

					await WriteSecurityIdAsync(writer, cancelMsg, cancellationToken);
				}
				else //if (!cancelMsg.SecurityId.BoardCode.IsEmpty())
				{
					requestType = MassCancelRequestType.CancelOrdersForMarket;

					await writer.WriteAsync((FixTags)SpectraTags.MarketSegmentID, cancellationToken);
					await writer.WriteAsync(cancelMsg.SecurityType == SecurityTypes.Option ? 'O' : 'F', cancellationToken);
				}

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				await writer.WriteAsync(FixTags.MassCancelRequestType, cancellationToken);
				await writer.WriteAsync(requestType, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return FixMessages.OrderMassCancelRequest;
			}

			case MessageTypes.OrderStatus:
			{
				var statusMsg = (OrderStatusMessage)message;

				if (statusMsg.TransactionId != 0)
				{
					await writer.WriteAsync(FixTags.OrdStatusReqID, cancellationToken);
					await writer.WriteAsync(statusMsg.TransactionId, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, statusMsg, cancellationToken);

				if (statusMsg.OrderId != null)
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(statusMsg.OrderId.Value, cancellationToken);
				}

				if (statusMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
					await writer.WriteAsync(statusMsg.OriginalTransactionId, cancellationToken);
				}

				if (statusMsg.Side != null)
				{
					await writer.WriteSideAsync(statusMsg.Side.Value, cancellationToken);
				}

				return FixMessages.OrderStatusRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private static async ValueTask WriteAccountAndPartiesAsync(IFixWriter writer, OrderMessage orderMsg, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Account, cancellationToken);
		await writer.WriteAsync(orderMsg.PortfolioName.Substring(orderMsg.PortfolioName.Length - 3), cancellationToken);

		var hasClientCode = !orderMsg.ClientCode.IsEmpty();
		var hasBrokerCode = !orderMsg.BrokerCode.IsEmpty();

		var partyCount = 0 + (hasClientCode ? 1 : 0) + (hasBrokerCode ? 1 : 0);

		if (partyCount == 0)
			return;

		await writer.WriteAsync(FixTags.NoPartyIDs, cancellationToken);
		await writer.WriteAsync(partyCount, cancellationToken);

		if (hasClientCode)
		{
			await writer.WriteAsync(FixTags.PartyID, cancellationToken);
			await writer.WriteAsync(orderMsg.ClientCode, cancellationToken);

			await writer.WriteAsync(FixTags.PartyIDSource, cancellationToken);
			await writer.WriteAsync(PartyIDSource.GenerallyAcceptedMarketParticipantIdentifier, cancellationToken);

			await writer.WriteAsync(FixTags.PartyRole, cancellationToken);
			await writer.WriteAsync((int)PartyRole.ClientId, cancellationToken);
		}

		if (hasBrokerCode)
		{
			await writer.WriteAsync(FixTags.PartyID, cancellationToken);
			await writer.WriteAsync(orderMsg.BrokerCode, cancellationToken);

			await writer.WriteAsync(FixTags.PartyIDSource, cancellationToken);
			await writer.WriteAsync(PartyIDSource.GenerallyAcceptedMarketParticipantIdentifier, cancellationToken);

			await writer.WriteAsync(FixTags.PartyRole, cancellationToken);
			await writer.WriteAsync((int)PartyRole.EnteringFirm, cancellationToken);
		}
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityMessage secMsg, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(secMsg.SecurityId.SecurityCode, cancellationToken);

		if (!secMsg.CfiCode.IsEmpty())
		{
			await writer.WriteAsync(FixTags.CFICode, cancellationToken);
			await writer.WriteAsync(secMsg.CfiCode, cancellationToken);
		}

		if (secMsg.SecurityType != null)
		{
			await writer.WriteAsync(FixTags.SecurityType, cancellationToken);
			await writer.WriteAsync(secMsg.SecurityType.Value.ToFix(), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override bool IsLogoutError(string text)
	{
		return !text.IsEmpty() && !text.EqualsIgnoreCase("the confirming logout.");
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		switch (msgType)
		{
			case FixMessages.ExecutionReport:
			{
				var report = new ExecutionReport();

				long? flags = null;

				var isOk = await ReadExecutionReportAsync(reader, report, TimeStampParser, async (tag, r1, r2, cancellationToken) =>
				{
					switch (tag)
					{
						case (FixTags)SpectraTags.Flags:
							flags = await reader.ReadLongAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				var result = ProcessExecutionReportAsync(report, (r, execMsg, cancellationToken) =>
				{
					if (flags is long f)
					{
						execMsg.IsSystem = f.IsPlazaSystem();
						execMsg.TimeInForce = f.GetPlazaTimeInForce();
						execMsg.OrderStatus = f;

						if (f.HasBits(0x1000000000000000))
							execMsg.PostOnly = true;
					}

					return default;
				}, cancellationToken);

				await foreach (var msg in result)
					yield return msg;

				break;
			}
		}

		await foreach (var msg in base.OnReadAsync(reader, msgType, cancellationToken))
			yield return msg;
	}
}
