namespace StockSharp.Fix.Dialects.GainFutures;

using System.Net;

/// <summary>
/// Gain Futures FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.openecry)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OpenECryKey,
	GroupName = LocalizedStrings.StockKey)]
public class GainFuturesFixDialect : BaseFixDialect
{
	private static class GainFuturesFixMessages
	{
		public const string OrderMassStatusAck = "BR";
	}

	private static class GainFuturesFixTags
	{
		public const FixTags UUID = (FixTags)12003;
		public const FixTags FastHashCode = (FixTags)12004;
		public const FixTags MaxRecords = (FixTags)12051;
		public const FixTags SymbolLookupMode = (FixTags)12052;
		public const FixTags ContractGroup = (FixTags)12054;
		public const FixTags ContractType = (FixTags)12055;
		public const FixTags ByBaseContractsOnly = (FixTags)12056;
		public const FixTags OptionsRequired = (FixTags)12057;
		public const FixTags ClearingFirmID = (FixTags)12058;
		public const FixTags EleSymbol = (FixTags)12059;
		public const FixTags UnderlyingOECSymbol = (FixTags)12060;
		public const FixTags LegOECSymbol = (FixTags)12061;
		public const FixTags PitSymbol = (FixTags)12062;
		public const FixTags PriceFormat = (FixTags)12063;
		public const FixTags MarginCalcReqID = (FixTags)12064;
		public const FixTags MarginCalcReqResult = (FixTags)12065;
		public const FixTags MaxQty = (FixTags)12066;
		public const FixTags InitialMargin = (FixTags)12067;
		public const FixTags MaintenanceMargin = (FixTags)12068;
		public const FixTags NetOptionValue = (FixTags)12069;
		public const FixTags RiskValue = (FixTags)12070;
		public const FixTags FrontExpirationMonth = (FixTags)12071;
		public const FixTags UpdatesSinceTimestamp = (FixTags)12072;
		public const FixTags LastSolicitedClOrdID = (FixTags)12073;
		public const FixTags MassStatusReqResult = (FixTags)12074;
		public const FixTags StrikeDisplayFactor = (FixTags)12075;
	}

	private static class GainFuturesFixAddresses
	{
		public static readonly EndPoint Api = $"api.gainfutures.com:9400".To<EndPoint>();
		public static readonly EndPoint Sim = $"sim.gainfutures.com:9300".To<EndPoint>();
		public static readonly EndPoint Prod = $"prod.gainfutures.com:9400".To<EndPoint>();
	}

	private string _fastHashCode;

	/// <summary>
	/// Initializes a new instance of the <see cref="GainFuturesFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public GainFuturesFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss");
		TimeParser = new FastTimeSpanParser("hh:mm:ss");
		//DateParser = new FastDateTimeParser("yyyyMMdd");

		//HasPosition = false;
	}

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		MessageTypes.SecurityLookup.ToInfo(),
		MessageTypes.MarketData.ToInfo(),

		MessageTypes.PortfolioLookup.ToInfo(),
		MessageTypes.OrderRegister.ToInfo(),
		MessageTypes.OrderReplace.ToInfo(),
		MessageTypes.OrderCancel.ToInfo(),
		MessageTypes.OrderStatus.ToInfo(),

		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

	/// <inheritdoc />
	public override IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
		=> new DataType[]
		{
			DataType.MarketDepth,
			DataType.Level1,
			DataType.Ticks,
		}.ToAsyncEnumerable();

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => dataType != DataType.Securities;

	/// <inheritdoc />
	protected override void OnReset()
	{
		_fastHashCode = null;
		base.OnReset();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (!lookupMsg.IsSubscribe)
			return;

		foreach (var acc in Accounts.SplitByComma())
		{
			await RaiseNewOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = acc,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}

		await RaiseNewOutMessageAsync(lookupMsg.CreateResult(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case MessageTypes.Connect:
			{
				string uuid;

				if (Address.Equals(GainFuturesFixAddresses.Api))
					uuid = "9e61a8bc-0a31-4542-ad85-33ebab0e4e86";
				else if (Address.Equals(GainFuturesFixAddresses.Prod))
					uuid = "7bc4f65b-98bb-4c88-ab5e-7cd19abc3d4e";
				else //if (Address.Equals(GainFuturesAddresses.Sim))
					uuid = "9e61a8bc-0a31-4542-ad85-33ebab0e4e86";

				return await WriteLogonRequestAsync(writer, (ConnectMessage)message, cancellationToken, async (w, cancellationToken) =>
				{
					await w.WriteAsync(GainFuturesFixTags.UUID, cancellationToken);
					await w.WriteAsync(uuid, cancellationToken);
				});
			}
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

				await WriteAccountAsync(writer, regMsg, cancellationToken);

				await WriteAllocAsync(writer, regMsg, cancellationToken);

				await writer.WriteHandlInstAsync(regMsg, HandlInst.AutomatedExecutionOrderPrivate, cancellationToken);

				await WriteExecInstAsync(writer, regMsg, cancellationToken);

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

				var condition = regMsg.Condition as FixOrderCondition;

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixTimeInForce(), cancellationToken);

				if (regMsg.TillDate != null)
				{
					await writer.WriteAsync(FixTags.ExpireTime, cancellationToken);
					await writer.WriteAsync(regMsg.TillDate.Value, TimeStampParser, cancellationToken);
				}

				if (!regMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(FixTags.Text, cancellationToken);
					await writer.WriteAsync(regMsg.Comment, cancellationToken);
				}

				if (condition?.Offset != null)
				{
					await writer.WriteAsync(FixTags.PegOffsetValue, cancellationToken);
					await writer.WriteAsync(condition.Offset.Value, cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await WriteAccountAsync(writer, cancelMsg, cancellationToken);

				await WriteAllocAsync(writer, cancelMsg, cancellationToken);

				await WriteSecurityIdAsync(writer, cancelMsg, cancellationToken);

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				if (cancelMsg.Volume != null)
				{
					await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
					await writer.WriteAsync(cancelMsg.Volume.Value, cancellationToken);
				}

				if (!cancelMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(FixTags.Text, cancellationToken);
					await writer.WriteAsync(cancelMsg.Comment, cancellationToken);
				}

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				await WriteAccountAsync(writer, replaceMsg, cancellationToken);

				await WriteAllocAsync(writer, replaceMsg, cancellationToken);

				await WriteExecInstAsync(writer, replaceMsg, cancellationToken);

				if (replaceMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(FixTags.MaxFloor, cancellationToken);
					await writer.WriteAsync(replaceMsg.VisibleVolume.Value, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, replaceMsg, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteTransactTimeAsync(TimeStampParser, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixType(), cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				var condition = replaceMsg.Condition as FixOrderCondition;

				if (condition?.StopLoss is decimal sl)
				{
					await writer.WriteAsync(FixTags.StopPx, cancellationToken);
					await writer.WriteAsync(sl, cancellationToken);
				}

				if (condition?.Offset != null)
				{
					await writer.WriteAsync(FixTags.PegOffsetValue, cancellationToken);
					await writer.WriteAsync(condition.Offset.Value, cancellationToken);
				}

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixTimeInForce(), cancellationToken);

				if (replaceMsg.TillDate != null)
				{
					await writer.WriteAsync(FixTags.ExpireTime, cancellationToken);
					await writer.WriteAsync(replaceMsg.TillDate.Value, TimeStampParser, cancellationToken);
				}

				if (!replaceMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(FixTags.Text, cancellationToken);
					await writer.WriteAsync(replaceMsg.Comment, cancellationToken);
				}

				return FixMessages.OrderCancelReplaceRequest;
			}

			case MessageTypes.OrderStatus:
			{
				var statusMsg = (OrderStatusMessage)message;

				var isSingleOrder = statusMsg.OriginalTransactionId != default;

				if (isSingleOrder)
				{
					await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
					await writer.WriteAsync(statusMsg.OriginalTransactionId, cancellationToken);
				}

				if (statusMsg.TransactionId != default)
				{
					await writer.WriteAsync(isSingleOrder ? FixTags.OrdStatusReqID : FixTags.MassStatusReqID, cancellationToken);
					await writer.WriteAsync(statusMsg.TransactionId, cancellationToken);
				}

				await WriteAccountAsync(writer, statusMsg, cancellationToken);

				await WriteAllocAsync(writer, statusMsg, cancellationToken);

				if (statusMsg.SecurityId != default)
					await WriteSecurityIdAsync(writer, statusMsg, cancellationToken);

				if (statusMsg.Side != null)
				{
					await writer.WriteSideAsync(statusMsg.Side.Value, cancellationToken);
				}

				return isSingleOrder ? FixMessages.OrderStatusRequest : FixMessages.OrderMassStatusRequest;
			}

			case MessageTypes.PortfolioLookup:
			{
				var pfMsg = (PortfolioLookupMessage)message;

				await writer.WriteAsync(FixTags.PosReqID, cancellationToken);
				await writer.WriteAsync(pfMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.PosReqType, cancellationToken);
				await writer.WriteAsync((int)PosReqType.Positions, cancellationToken);

				await writer.WriteAsync(FixTags.SubscriptionRequestType, cancellationToken);
				await writer.WriteAsync(SubscriptionRequestType.Snapshot, cancellationToken);

				await WriteAccountAsync(writer, pfMsg, cancellationToken);

				await writer.WriteAsync(FixTags.AccountType, cancellationToken);
				await writer.WriteAsync((int)AccountType.AccountIsCarriedOnCustomerSideBooks, cancellationToken);

				return FixMessages.RequestForPositions;
			}

			case MessageTypes.SecurityLookup:
			{
				var lookupMsg = (SecurityLookupMessage)message;

				await writer.WriteAsync(FixTags.SecurityReqID, cancellationToken);
				await writer.WriteAsync(lookupMsg.TransactionId, cancellationToken);

				var secId = lookupMsg.SecurityId;

				await writer.WriteAsync(FixTags.SecurityListRequestType, cancellationToken);
				await writer.WriteAsync((int)(secId == default ? SecurityListRequestType.AllSecurities : SecurityListRequestType.Symbol), cancellationToken);

				if (secId != default)
				{
					await writer.WriteAsync(FixTags.Symbol, cancellationToken);
					await writer.WriteAsync(secId.SecurityCode, cancellationToken);
				}

				if (lookupMsg.Count != null)
				{
					await writer.WriteAsync(GainFuturesFixTags.MaxRecords, cancellationToken);
					await writer.WriteAsync(lookupMsg.Count.Value, cancellationToken);
				}

				await writer.WriteAsync(FixTags.SubscriptionRequestType, cancellationToken);
				await writer.WriteAsync(lookupMsg.GetSubscriptionType(), cancellationToken);

				return FixMessages.SecurityListRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityMessage secMsg, CancellationToken cancellationToken)
	{
		var symbol = secMsg.GetUnderlyingCode();
		var cfiCode = secMsg.CfiCode;
		var maturity = secMsg.ExpiryDate;
		var strike = secMsg.Strike;

		if (symbol.IsEmpty())
		{
			symbol = secMsg.SecurityId.SecurityCode;

			var parts = symbol.SplitBySep("_");
			if (parts.Length > 2)
			{
				symbol = parts[0];
				maturity = YearMonthParser.Parse(parts[1]).UtcKind();
				cfiCode = parts[2];

				if (parts.Length > 3)
					strike = parts[3].To<decimal>();
			}
		}

		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(symbol, cancellationToken);

		await writer.WriteAsync(FixTags.CFICode, cancellationToken);
		await writer.WriteAsync(cfiCode, cancellationToken);

		if (maturity != null)
		{
			await writer.WriteAsync(FixTags.MaturityMonthYear, cancellationToken);
			await writer.WriteAsync(maturity.Value, YearMonthParser, cancellationToken);
		}

		if (strike != null)
		{
			await writer.WriteAsync(FixTags.StrikePrice, cancellationToken);
			await writer.WriteAsync(strike.Value, cancellationToken);
		}
	}

	private static ValueTask WriteAllocAsync(IFixWriter writer, OrderMessage orderMsg, CancellationToken cancellationToken)
	{
		return default;
	}

	private static ValueTask WriteExecInstAsync(IFixWriter writer, OrderRegisterMessage orderMsg, CancellationToken cancellationToken)
	{
		return default;
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		switch (msgType)
		{
			case FixMessages.Logon:
			{
				var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
				{
					switch (tag)
					{
						case GainFuturesFixTags.FastHashCode:
							_fastHashCode = await reader.ReadStringAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				yield return new ConnectMessage();
				break;
			}
			case GainFuturesFixMessages.OrderMassStatusAck:
			{
				string massStatusReqID = null;
				int? massStatusReqResult = null;
				string text = null;

				var isOk = await reader.ReadMessageAsync(async (tag, cancellationToken) =>
				{
					switch (tag)
					{
						case FixTags.MassStatusReqID:
							massStatusReqID = await reader.ReadStringAsync(cancellationToken);
							return true;
						case GainFuturesFixTags.MassStatusReqResult:
							massStatusReqResult = await reader.ReadIntAsync(cancellationToken);
							return true;
						case FixTags.Text:
							text = await reader.ReadStringAsync(cancellationToken);
							return true;
						default:
							return false;
					}
				}, cancellationToken);

				if (!isOk)
					yield break;

				var transId = massStatusReqID.To<long>();

				if (massStatusReqResult == 1) // ERROR
				{
					yield return new SubscriptionResponseMessage
					{
						OriginalTransactionId = transId,
						Error = new InvalidOperationException(text),
					};
				}
				else
				{
					yield return new SubscriptionFinishedMessage
					{
						OriginalTransactionId = transId,
					};
				}

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
	protected override void InitSecId(SecurityMessage message, string symbol, string securityExchange, string idSource, string idValue)
	{
		base.InitSecId(message, symbol, securityExchange, idSource, idValue);

		if (!message.ShortName.IsEmpty())
		{
			message.TryFillUnderlyingId(symbol);

			var secCode = message.SecurityId.SecurityCode;

			if (message.ExpiryDate != null)
				secCode += $"_{message.ExpiryDate:yyyyMM}";

			if (!message.CfiCode.IsEmpty())
				secCode += $"_{message.CfiCode}";

			if (message.Strike != null)
				secCode += $"_{message.Strike.Value}";

			message.SetSecurityCode(secCode);
		}
	}

	/// <inheritdoc />
	protected async override ValueTask<bool> ProcessSecurityDefinitionAsync(FixTags tag, IFixReader reader, SecurityMessage message, CancellationToken cancellationToken)
	{
		switch (tag)
		{
			case GainFuturesFixTags.EleSymbol:
				message.ShortName = await reader.ReadStringAsync(cancellationToken);
				return true;
			case FixTags.ExpireTime:
				message.ExpiryDate = await reader.ReadUtcAsync(TimeStampParser, cancellationToken);
				return true;
			case GainFuturesFixTags.ContractGroup:
				message.Class = await reader.ReadStringAsync(cancellationToken);
				return true;
			case GainFuturesFixTags.PriceFormat:
			{
				var pf = await reader.ReadIntAsync(cancellationToken);

				if (pf > 0)
					message.PriceStep = pf.GetPriceStep();
				else if (pf < 0)
				{
					pf = pf.Abs();

					if ((pf % 100) == 0)
						message.PriceStep = 0.01m / (pf / 100);
					else
						message.PriceStep = 0.01m / pf;
				}

				return true;
			}
			default:
				return await base.ProcessSecurityDefinitionAsync(tag, reader, message, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask<bool> ProcessExecutionReportExtraTagAsync(FixTags tag, IFixReader reader, ExecutionReport report, CancellationToken cancellationToken)
	{
		switch (tag)
		{
			case FixTags.MaturityMonthYear:
				report.Symbol += $"_{await reader.ReadUtcAsync(YearMonthParser, cancellationToken):yyyyMM}";
				return true;
			case FixTags.CFICode:
				report.Symbol += $"_{await reader.ReadStringAsync(cancellationToken)}";
				return true;
			case FixTags.StrikePrice:
				report.Symbol += $"_{await reader.ReadDecimalAsync(cancellationToken)}";
				return true;
			default:
				return await base.ProcessExecutionReportExtraTagAsync(tag, reader, report, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<ExecutionMessage> ProcessExecutionReportAsync(ExecutionReport report, ExecutionMessage message, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var secId = message.SecurityId;

		if (secId.BoardCode.IsEmpty())
		{
			secId.BoardCode = "CME";
			message.SecurityId = secId;
		}

		await foreach (var r in base.ProcessExecutionReportAsync(report, message, cancellationToken))
			yield return r;
	}
}