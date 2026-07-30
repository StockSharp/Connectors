namespace StockSharp.Fix.Dialects.Moex.Asts;

using System.Text.RegularExpressions;

/// <summary>
/// ASTS FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.moex)]
public abstract class AstsFixDialect : BaseFixDialect
{
	//private int _resendMaxNum;
	private readonly FastDateTimeParser _execTimeParser = new("yyyyMMdd-HH:mm:ss");

	private const char _passiveTif = 'z';

	/// <summary>
	/// Initializes a new instance of the <see cref="AstsFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	protected AstsFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, StringHelper.WindowsCyrillic)
	{
		TimeZone = TimeHelper.Moscow;
		//OrderLookup = true;

		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss.ffffff");
	}

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

		MessageTypes.ChangePassword.ToInfo(),

		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_ASTS";
#endif

	/// <inheritdoc />
	protected override async ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			//case MessageTypes.Connect:
			//{
			//	_resendMaxNum = 0;
			//	return base.OnWrite(writer, message);
			//}

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

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				await WriteAccountAndPartiesAsync(writer, regMsg, cancellationToken);

				if (regMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(FixTags.MaxFloor, cancellationToken);
					await writer.WriteAsync(regMsg.VisibleVolume.Value, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, regMsg, cancellationToken);

				await writer.WriteSideAsync(regMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(regMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixType(), cancellationToken);

				if (regMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(regMsg.Price, cancellationToken);
				}

				var tif = regMsg.PostOnly == true ? _passiveTif : regMsg.GetFixTimeInForce();

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(tif, cancellationToken);

				if (regMsg.IsMarketMaker == true)
				{
					await writer.WriteMarketMakerAsync(cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				if (cancelMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
					await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);
				}

				if (cancelMsg.OrderId != null)
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(cancelMsg.OrderId.Value, cancellationToken);
				}

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);
				
				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				if (replaceMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
					await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);
				}

				if (replaceMsg.OldOrderId != null)
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(replaceMsg.OldOrderId.Value, cancellationToken);
				}

				await WriteAccountAndPartiesAsync(writer, replaceMsg, cancellationToken);

				await WriteSecurityIdAsync(writer, replaceMsg, cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixType(), cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				return FixMessages.OrderCancelReplaceRequest;
			}

			case MessageTypes.OrderGroupCancel:
			{
				var cancelMsg = (OrderGroupCancelMessage)message;

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				var requestType = MassCancelRequestType.CancelAllOrders;

				if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
				{
					requestType = MassCancelRequestType.CancelOrdersForSecurity;

					await WriteSecurityIdAsync(writer, cancelMsg, cancellationToken);
				}

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				if (!cancelMsg.PortfolioName.IsEmpty())
					await WriteAccountAndPartiesAsync(writer, cancelMsg, cancellationToken);

				await writer.WriteAsync(FixTags.MassCancelRequestType, cancellationToken);
				await writer.WriteAsync(requestType, cancellationToken);

				return FixMessages.OrderMassCancelRequest;
			}

			case MessageTypes.OrderStatus:
			{
				var statusMsg = (OrderStatusMessage)message;

				if (statusMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
					await writer.WriteAsync(statusMsg.OriginalTransactionId, cancellationToken);
				}

				if (statusMsg.OrderId != null)
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(statusMsg.OrderId.Value, cancellationToken);
				}

				return FixMessages.OrderStatusRequest;
			}

			case MessageTypes.ChangePassword:
			{
				var pwdMsg = (ChangePasswordMessage)message;

				var msgType = await WriteLogonRequestAsync(writer, new(), cancellationToken);

				await writer.WriteAsync(FixTags.NewPassword, cancellationToken);
				await writer.WriteAsync(pwdMsg.NewPassword.UnSecure(), cancellationToken);

				return msgType;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private async ValueTask WriteAccountAndPartiesAsync(IFixWriter writer, OrderMessage orderMsg, CancellationToken cancellationToken)
	{
		await WriteAccountAsync(writer, orderMsg, cancellationToken);

		if (orderMsg.ClientCode.IsEmpty())
			return;

		await writer.WriteAsync(FixTags.NoPartyIDs, cancellationToken);
		await writer.WriteAsync(1, cancellationToken);

		await writer.WriteAsync(FixTags.PartyID, cancellationToken);
		await writer.WriteAsync(orderMsg.ClientCode, cancellationToken);

		await writer.WriteAsync(FixTags.PartyIDSource, cancellationToken);
		await writer.WriteAsync(PartyIDSource.Proprietary, cancellationToken);

		await writer.WriteAsync(FixTags.PartyRole, cancellationToken);
		await writer.WriteAsync((int)PartyRole.ClientId, cancellationToken);
	}

	/// <summary>
	/// Write security id.
	/// </summary>
	/// <param name="writer">FIX data writer.</param>
	/// <param name="secMsg">Security ID.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	protected abstract ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityMessage secMsg, CancellationToken cancellationToken);

	/// <inheritdoc />
	protected override bool IsLogoutError(string text)
	{
		if (text != null)
		{
			var match = Regex.Match(text, @"The incoming message has a sequence number \(\d+\) less than expected \((?<maxCounter>\d+)\) and the PossDupFlag is not set.");
			if (match.Success)
			{
				//_resendMaxNum = match.Groups["maxCounter"].Value.To<int>();
				//SendLogon(_resendMaxNum);
				return false;
			}
		}

		return !text.IsEmpty() && !text.EqualsIgnoreCase("the confirming logout.");
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		switch (msgType)
		{
			case FixMessages.Logon:
			{
				var result = base.OnReadAsync(reader, msgType, cancellationToken);

				//if (retVal)
				//{
				//	//if (_resendMaxNum > 0)
				//	//{
				//	//	_writer.WriteResendRequest(2, _resendMaxNum);
				//	//	_resendMaxNum = 0;
				//	//}
				//}

				await foreach (var msg in result)
					yield return msg;

				break;
			}

			case FixMessages.ExecutionReport:
			{
				var report = new ExecutionReport();

				var isOk = await ReadExecutionReportAsync(reader, report, _execTimeParser, (tag, r1, r2, cancellationToken) => new(false), cancellationToken);

				if (!isOk)
					yield break;

				var result = ProcessExecutionReportAsync(report, (r, execMsg, cancellationToken) =>
				{
					report.ExecId = report.ExecId.Split('|')[0];
					return default;
				}, cancellationToken);

				await foreach (var msg in result)
					yield return msg;

				break;
			}

			default:
			{
				await foreach (var msg in base.OnReadAsync(reader, msgType, cancellationToken))
					yield return msg;

				break;
			}
		}
	}

	/// <inheritdoc />
	protected override bool ApplyTimeInForce(ExecutionReport report, ExecutionMessage msg, bool throwInvalid)
	{
		if (report.TimeInForce == _passiveTif)
		{
			msg.PostOnly = true;
			return true;
		}
		else
			return base.ApplyTimeInForce(report, msg, throwInvalid);
	}
}

/// <summary>
/// ASTS (currencies section) FIX protocol dialect.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AstsCurrencyFixDialect"/>.
/// </remarks>
/// <param name="transactionIdGenerator">Transaction id generator.</param>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AstsCurrenciesKey,
	GroupName = LocalizedStrings.RussiaKey)]
public class AstsCurrencyFixDialect(IdGenerator transactionIdGenerator) : AstsFixDialect(transactionIdGenerator)
{
	/// <inheritdoc />
	protected override async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityMessage secMsg, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(secMsg.SecurityId.SecurityCode, cancellationToken);

		// это не обязательные значения, поэтому нет смысла их передавать
		//writer.Write(FixTags.Product);
		//writer.Write((int)Product.Currency);

		//writer.Write(FixTags.CFICode);
		//writer.Write("MRCXXX");

		// TODO
		//if (secMsg.SecurityType != null)
		//{
		//	// 'FXSPOT' (Валютный спот);
		//	// 'FXSWAP' (Валютный своп);
		//	// 'FXFWD'(Валютный форвард);
		//	// 'FXBKT'(Бивалютная корзина).
		//	writer.Write(FixTags.SecurityType);
		//	writer.Write(secMsg.SecurityType.Value.ToFix());
		//}

		await writer.WriteAsync(FixTags.NoTradingSessions, cancellationToken);
		await writer.WriteAsync(1, cancellationToken);

		await writer.WriteAsync(FixTags.TradingSessionID, cancellationToken);
		await writer.WriteAsync(secMsg.SecurityId.BoardCode, cancellationToken);
	}
}

/// <summary>
/// ASTS (equity section) FIX protocol dialect.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AstsEquityFixDialect"/>.
/// </remarks>
/// <param name="transactionIdGenerator">Transaction id generator.</param>
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AstsEquitiesKey)]
public class AstsEquityFixDialect(IdGenerator transactionIdGenerator) : AstsFixDialect(transactionIdGenerator)
{
	/// <inheritdoc />
	protected override async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityMessage secMsg, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(secMsg.SecurityId.SecurityCode, cancellationToken);

		// Для заявок в режиме РЕПО с ЦК необходимо указание этого поля как 167=REPO, в противном случае заявка будет отклонена.
		if (secMsg is OrderRegisterMessage regMsg && regMsg.Condition is IRepoOrderCondition repo && repo.IsRepo)
		{
			await writer.WriteAsync(FixTags.SecurityType, cancellationToken);
			await writer.WriteAsync("REPO", cancellationToken);
		}

		await writer.WriteAsync(FixTags.NoTradingSessions, cancellationToken);
		await writer.WriteAsync(1, cancellationToken);

		await writer.WriteAsync(FixTags.TradingSessionID, cancellationToken);
		await writer.WriteAsync(secMsg.SecurityId.BoardCode, cancellationToken);
	}
}
