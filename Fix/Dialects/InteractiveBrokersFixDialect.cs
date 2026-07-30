namespace StockSharp.Fix.Dialects;

/// <summary>
/// Interactive Brokers FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.interactivebrokers)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.InteractiveBrokersKey,
	GroupName = LocalizedStrings.StockKey)]
public class InteractiveBrokersFixDialect : BaseFixDialect
{
	private static class IbkrTags
	{
		// ReSharper disable InconsistentNaming
		public const FixTags LocateBroker = (FixTags)5700;
		public const FixTags NoStrategyParameters = (FixTags)5957;
		public const FixTags StrategyParameterName = (FixTags)5958;
		public const FixTags StrategyParameterValue = (FixTags)5960;
		public const FixTags Exchange = (FixTags)6004;
		public const FixTags ContractID = (FixTags)6008;
		public const FixTags ConQPath = (FixTags)6009;
		public const FixTags OrderReferenceAccount = (FixTags)6010;
		public const FixTags ComboLegInformation = (FixTags)6013;
		public const FixTags IBKRLocalSymbol = (FixTags)6035;
		public const FixTags TradingClass = (FixTags)6058;
		public const FixTags IssueDate = (FixTags)6075;
		public const FixTags BdSymbol = (FixTags)6076;
		public const FixTags BdFlag = (FixTags)6077;
		public const FixTags ShortSaleRule = (FixTags)6086;
		public const FixTags WhatIf = (FixTags)6091;
		public const FixTags ParentClientID = (FixTags)6107;
		public const FixTags TriggerMethod = (FixTags)6115;
		public const FixTags OptionAcct = (FixTags)6122;
		public const FixTags ConditionConID = (FixTags)6123;
		public const FixTags ConditionExchange = (FixTags)6124;
		public const FixTags ConditionTriggerPrice = (FixTags)6125;
		public const FixTags ConditionOperand = (FixTags)6126;
		public const FixTags ConditionTriggerMethod = (FixTags)6127;
		public const FixTags ConditionIgnoreRegularTradingHours = (FixTags)6128;
		public const FixTags ConditionListSize = (FixTags)6136;
		public const FixTags ConditionLogicOperantBinder = (FixTags)6137;
		public const FixTags DailyNewID = (FixTags)6143;
		public const FixTags StockRangeLower = (FixTags)6152;
		public const FixTags StockRangeUpper = (FixTags)6153;
		public const FixTags Delta = (FixTags)6154;
		public const FixTags ConditionUnderlying = (FixTags)6165;
		public const FixTags ConditionStrike = (FixTags)6166;
		public const FixTags ConditionRight = (FixTags)6167;
		public const FixTags ConditionExpiry = (FixTags)6168;
		public const FixTags ConditionSecurityType = (FixTags)6169;
		public const FixTags ConditionLocalSymbol = (FixTags)6170;
		public const FixTags DiscretionaryType = (FixTags)6173;
		public const FixTags ForceOnlyRTH = (FixTags)6205;
		public const FixTags LegLocateReqd = (FixTags)6215;
		public const FixTags LegLocateBroker = (FixTags)6216;
		public const FixTags CondPrimaryExch = (FixTags)6217;
		public const FixTags CondCurrency = (FixTags)6218;
		public const FixTags ConditionType = (FixTags)6222;
		public const FixTags ConditionTime = (FixTags)6223;
		public const FixTags ConditionMargin = (FixTags)6245;
		public const FixTags ConditionExecutionPattern = (FixTags)6246;
		public const FixTags SmartComboGuarantee = (FixTags)6248;
		public const FixTags NoBarriers = (FixTags)6257;
		public const FixTags BarrierPrice = (FixTags)6258;
		public const FixTags BarrierStopPrice = (FixTags)6259;
		public const FixTags BarrierTrailingAmt = (FixTags)6260;
		public const FixTags BarrierPriceDelimiter = (FixTags)6261;
		public const FixTags BarrierLimitPrice = (FixTags)6262;
		public const FixTags ConditionVolume = (FixTags)6263;
		public const FixTags TrailingAmtUnit = (FixTags)6268;
		public const FixTags BarrierTrailingAmtUnit = (FixTags)6269;
		public const FixTags CheapToReroute = (FixTags)6271;
		public const FixTags PipExchanges = (FixTags)6273;
		public const FixTags ContinuousUpdate = (FixTags)6275;
		public const FixTags UnderlyingRefPrice = (FixTags)6279;
		public const FixTags XcrossC1OrdID = (FixTags)6281;
		public const FixTags XCrossClearingFirm = (FixTags)6282;
		public const FixTags XCrossClearingAccount = (FixTags)6283;
		public const FixTags XCrossOpenClose = (FixTags)6284;
		public const FixTags XCrossOptionAcct = (FixTags)6285;
		public const FixTags FacilitationPercentage = (FixTags)6286;
		public const FixTags NotHeld = (FixTags)6287;
		public const FixTags HedgingType = (FixTags)6290;
		public const FixTags TrailLimitOffset = (FixTags)6370;
		public const FixTags StagedOrder = (FixTags)6406;
		public const FixTags DeactivateOnClose = (FixTags)6436;
		public const FixTags DividendSchedule = (FixTags)6458;
		public const FixTags InterestSchedule = (FixTags)6459;
		public const FixTags IsDeltaHedge = (FixTags)6460;
		public const FixTags VolatCapPercentage = (FixTags)6467;
		public const FixTags VolatCapTicks = (FixTags)6468;
		public const FixTags Deactivate = (FixTags)6521;
		public const FixTags ConsiderExecCost = (FixTags)6559;
		public const FixTags ExchangeExecID = (FixTags)6568;
		public const FixTags CondSubmitCancel = (FixTags)6579;
		public const FixTags StockRefPrice = (FixTags)6580;
		public const FixTags GTCExpireTime = (FixTags)6596;
		public const FixTags ProfessionalCustomer = (FixTags)6636;
		public const FixTags HedgeType = (FixTags)6665;
		public const FixTags HedgeRatio = (FixTags)6666;
		public const FixTags LegClearingFirm = (FixTags)6680;
		public const FixTags UseNetPrice = (FixTags)6882;
		public const FixTags ImbalanceOnly = (FixTags)6737;
		public const FixTags Mifid2DecisionMakerShortCode = (FixTags)8237;
		public const FixTags Mifid2DecisionAlgo = (FixTags)8243;
		public const FixTags Mifid2ExecutionTrader = (FixTags)8254;
		public const FixTags Mifid2ExecutionAlgo = (FixTags)8255;
		public const FixTags AllowPastEndTime = (FixTags)8700;
		public const FixTags DisplaySize = (FixTags)8701;
		public const FixTags EndTime = (FixTags)8702;
		public const FixTags ForceCompletion = (FixTags)8703;
		public const FixTags PctVol = (FixTags)8706;
		public const FixTags RiskAversion = (FixTags)8707;
		public const FixTags StartTime = (FixTags)8708;
		public const FixTags ImpVolatility = (FixTags)9816;
		// ReSharper restore InconsistentNaming
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="InteractiveBrokersFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public InteractiveBrokersFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8, FixVersions.Fix42)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss");
		TimeParser = new FastTimeSpanParser("hh:mm:ss");
		DateParser = new FastDateTimeParser("yyyyMMdd");
	}

	/// <inheritdoc />
	public override Type OrderConditionType => typeof(InteractiveBrokersFixOrderCondition);

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		MessageTypes.OrderRegister.ToInfo(),
		MessageTypes.OrderReplace.ToInfo(),
		MessageTypes.OrderCancel.ToInfo(),
		MessageTypes.OrderStatus.ToInfo(),

		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

	/// <inheritdoc />
	public override IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
		=> AsyncEnumerable.Empty<DataType>();

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_IBKR";
#endif

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

				if (regMsg.VisibleVolume != null)
				{
					await writer.WriteAsync(FixTags.MaxFloor, cancellationToken);
					await writer.WriteAsync(regMsg.VisibleVolume.Value, cancellationToken);
				}

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(regMsg.TransactionId, cancellationToken);

				await WriteCurrencyAsync(writer, regMsg, cancellationToken);

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				await writer.WriteHandlInstAsync(regMsg, HandlInst.AutomatedExecutionOrderPublic, cancellationToken);

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

				await WriteOrderConditionAsync(writer, (InteractiveBrokersFixOrderCondition)regMsg.Condition, cancellationToken);

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(regMsg.GetFixTimeInForce(), cancellationToken);

				if (regMsg.SecurityType != null)
				{
					await writer.WriteAsync(FixTags.SecurityType, cancellationToken);
					await writer.WriteAsync(regMsg.SecurityType.Value.ToFix(), cancellationToken);

					if (regMsg.SecurityType == SecurityTypes.Option)
					{
						if (regMsg.OptionType != null)
						{
							await writer.WriteAsync(FixTags.PutOrCall, cancellationToken);
							await writer.WriteAsync(regMsg.OptionType.Value.ToFix(), cancellationToken);
						}

						if (regMsg.Strike != null)
						{
							await writer.WriteAsync(FixTags.StrikePrice, cancellationToken);
							await writer.WriteAsync(regMsg.Strike.Value, cancellationToken);
						}
					}

					if ((regMsg.SecurityType == SecurityTypes.Future || regMsg.SecurityType == SecurityTypes.Option) && regMsg.ExpiryDate != null)
					{
						await writer.WriteAsync(FixTags.MaturityMonthYear, cancellationToken);
						await writer.WriteAsync(regMsg.ExpiryDate.Value, YearMonthParser, cancellationToken);

						await writer.WriteAsync(FixTags.MaturityDay, cancellationToken);
						await writer.WriteAsync(regMsg.ExpiryDate.Value.Day, cancellationToken);
					}
				}

				await writer.WriteAsync(FixTags.SecurityExchange, cancellationToken);
				await writer.WriteAsync(regMsg.SecurityId.BoardCode, cancellationToken);

				if (regMsg.Multiplier != null)
				{
					await writer.WriteAsync(FixTags.ContractMultiplier, cancellationToken);
					await writer.WriteAsync(regMsg.Multiplier.Value, cancellationToken);
				}

				if (regMsg.TillDate != null)
				{
					if (regMsg.TillDate.Value.TimeOfDay != default)
					{
						await writer.WriteAsync(FixTags.ExpireTime, cancellationToken);
						await writer.WriteAsync(regMsg.TillDate.Value.TimeOfDay, TimeParser, cancellationToken);
					}

					await writer.WriteExpiryDateAsync(regMsg, DateParser, TimeZone, cancellationToken);
				}

				return FixMessages.NewOrderSingle;
			}

			case MessageTypes.OrderCancel:
			{
				var cancelMsg = (OrderCancelMessage)message;
				var securityId = cancelMsg.SecurityId;

				await WriteAccountAsync(writer, cancelMsg, cancellationToken);

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, cancelMsg.OriginalTransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(cancelMsg.TransactionId, cancellationToken);

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				if (cancelMsg.Side != null)
				{
					await writer.WriteSideAsync(cancelMsg.Side.Value, cancellationToken);
				}

				if (cancelMsg.Volume != null)
				{
					await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
					await writer.WriteAsync(cancelMsg.Volume.Value, cancellationToken);
				}

				return FixMessages.OrderCancelRequest;
			}

			case MessageTypes.OrderReplace:
			{
				var replaceMsg = (OrderReplaceMessage)message;
				var securityId = replaceMsg.SecurityId;

				await WriteAccountAsync(writer, replaceMsg, cancellationToken);

				await writer.WriteHandlInstAsync(replaceMsg, HandlInst.AutomatedExecutionOrderPublic, cancellationToken);

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);
				await writer.WriteAsync(replaceMsg.TransactionId, cancellationToken);

				await writer.WriteAsync(FixTags.OrigClOrdID, cancellationToken);
				await WriteClOrdIdAsync(writer, replaceMsg.OriginalTransactionId, cancellationToken);

				if (replaceMsg.OldOrderId != null)
				{
					await writer.WriteAsync(FixTags.OrderID, cancellationToken);
					await writer.WriteAsync(replaceMsg.OldOrderId.Value, cancellationToken);
				}

				await WriteSecurityIdAsync(writer, securityId, cancellationToken);

				await writer.WriteSideAsync(replaceMsg.Side, cancellationToken);

				await writer.WriteAsync(FixTags.OrderQty, cancellationToken);
				await writer.WriteAsync(replaceMsg.Volume, cancellationToken);

				await writer.WriteAsync(FixTags.OrdType, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixType(), cancellationToken);

				if (replaceMsg.OrderType != OrderTypes.Market)
				{
					await writer.WriteAsync(FixTags.Price, cancellationToken);
					await writer.WriteAsync(replaceMsg.Price, cancellationToken);
				}

				await writer.WriteAsync(FixTags.TimeInForce, cancellationToken);
				await writer.WriteAsync(replaceMsg.GetFixTimeInForce(), cancellationToken);

				if (!replaceMsg.Comment.IsEmpty())
				{
					await writer.WriteAsync(FixTags.Text, cancellationToken);
					await writer.WriteAsync(replaceMsg.Comment, cancellationToken);
				}

				if (replaceMsg.TillDate != null)
				{
					if (replaceMsg.TillDate.Value.TimeOfDay != default)
					{
						await writer.WriteAsync(FixTags.ExpireTime, cancellationToken);
						await writer.WriteAsync(replaceMsg.TillDate.Value.TimeOfDay, TimeParser, cancellationToken);
					}

					await writer.WriteExpiryDateAsync(replaceMsg, DateParser, TimeZone, cancellationToken);
				}

				await WriteOrderConditionAsync(writer, (InteractiveBrokersFixOrderCondition)replaceMsg.Condition, cancellationToken);

				return FixMessages.OrderCancelReplaceRequest;
			}

			case MessageTypes.OrderStatus:
			{
				var statusMsg = (OrderStatusMessage)message;

				await writer.WriteAsync(FixTags.ClOrdID, cancellationToken);

				if (statusMsg.OriginalTransactionId != 0)
				{
					await writer.WriteAsync(statusMsg.OriginalTransactionId, cancellationToken);
				}
				else
				{
					// A special value of � * � may be specified to refer to all the open orders
					// of the customer.
					await writer.WriteAsync(" * ", cancellationToken);
				}

				return FixMessages.OrderStatusRequest;
			}

			default:
				return await base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	private static async ValueTask WriteCurrencyAsync(IFixWriter writer, SecurityMessage message, CancellationToken cancellationToken)
	{
		if (message.Currency == null)
			return;

		await writer.WriteAsync(FixTags.Currency, cancellationToken);
		await writer.WriteAsync(message.Currency.Value.ToMicexCurrencyName(), cancellationToken);
	}

	private static async ValueTask WriteSecurityIdAsync(IFixWriter writer, SecurityId securityId, CancellationToken cancellationToken)
	{
		await writer.WriteAsync(FixTags.Symbol, cancellationToken);
		await writer.WriteAsync(securityId.SecurityCode, cancellationToken);

		if (!securityId.Cusip.IsEmpty())
		{
			await writer.WriteAsync(FixTags.SecurityID, cancellationToken);
			await writer.WriteAsync(securityId.Cusip, cancellationToken);

			await writer.WriteAsync(FixTags.IDSource, cancellationToken);
			await writer.WriteAsync(SecurityIDSource.Cusip, cancellationToken);
		}
		else if (!securityId.Isin.IsEmpty())
		{
			await writer.WriteAsync(FixTags.SecurityID, cancellationToken);
			await writer.WriteAsync(securityId.Isin, cancellationToken);

			await writer.WriteAsync(FixTags.IDSource, cancellationToken);
			await writer.WriteAsync(SecurityIDSource.IsinNumber, cancellationToken);
		}
	}

	private async ValueTask WriteOrderConditionAsync(IFixWriter writer, InteractiveBrokersFixOrderCondition condition, CancellationToken cancellationToken)
	{
		if (writer == null)
			throw new ArgumentNullException(nameof(writer));

		if (condition == null)
			return;

		if (condition.StopLoss is decimal sl)
		{
			await writer.WriteAsync(FixTags.StopPx, cancellationToken);
			await writer.WriteAsync(sl, cancellationToken);
		}

		if (!condition.LocateBroker.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.LocateBroker, cancellationToken);
			await writer.WriteAsync(condition.LocateBroker, cancellationToken);
		}

		if (condition.NoStrategyParameters != null)
		{
			await writer.WriteAsync(IbkrTags.NoStrategyParameters, cancellationToken);
			await writer.WriteAsync(condition.NoStrategyParameters.Value, cancellationToken);
		}

		if (!condition.StrategyParameterName.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.StrategyParameterName, cancellationToken);
			await writer.WriteAsync(condition.StrategyParameterName, cancellationToken);
		}

		if (!condition.StrategyParameterValue.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.StrategyParameterValue, cancellationToken);
			await writer.WriteAsync(condition.StrategyParameterValue, cancellationToken);
		}

		if (!condition.ContractID.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ContractID, cancellationToken);
			await writer.WriteAsync(condition.ContractID, cancellationToken);
		}

		if (!condition.OrderReferenceAccount.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.OrderReferenceAccount, cancellationToken);
			await writer.WriteAsync(condition.OrderReferenceAccount, cancellationToken);
		}

		if (!condition.IBKRLocalSymbol.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.IBKRLocalSymbol, cancellationToken);
			await writer.WriteAsync(condition.IBKRLocalSymbol, cancellationToken);
		}

		if (!condition.TradingClass.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.TradingClass, cancellationToken);
			await writer.WriteAsync(condition.TradingClass, cancellationToken);
		}

		if (condition.ShortSaleRule != null)
		{
			await writer.WriteAsync(IbkrTags.ShortSaleRule, cancellationToken);
			await writer.WriteAsync(condition.ShortSaleRule.Value, cancellationToken);
		}

		if (condition.WhatIf != null)
		{
			await writer.WriteAsync(IbkrTags.WhatIf, cancellationToken);
			await writer.WriteAsync(condition.WhatIf.Value, cancellationToken);
		}

		if (!condition.TriggerMethod.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.TriggerMethod, cancellationToken);
			await writer.WriteAsync(condition.TriggerMethod, cancellationToken);
		}

		if (!condition.OptionAcct.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.OptionAcct, cancellationToken);
			await writer.WriteAsync(condition.OptionAcct, cancellationToken);
		}

		if (!condition.ConditionConID.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ConditionConID, cancellationToken);
			await writer.WriteAsync(condition.ConditionConID, cancellationToken);
		}

		if (!condition.ConditionExchange.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ConditionExchange, cancellationToken);
			await writer.WriteAsync(condition.ConditionExchange, cancellationToken);
		}

		if (condition.ConditionTriggerPrice != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionTriggerPrice, cancellationToken);
			await writer.WriteAsync(condition.ConditionTriggerPrice.Value, cancellationToken);
		}

		if (!condition.ConditionOperand.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ConditionOperand, cancellationToken);
			await writer.WriteAsync(condition.ConditionOperand, cancellationToken);
		}

		if (condition.ConditionTriggerMethod != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionTriggerMethod, cancellationToken);
			await writer.WriteAsync(condition.ConditionTriggerMethod.Value, cancellationToken);
		}

		if (condition.ConditionIgnoreRegularTradingHours != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionIgnoreRegularTradingHours, cancellationToken);
			await writer.WriteAsync(condition.ConditionIgnoreRegularTradingHours.Value, cancellationToken);
		}

		if (condition.ConditionListSize != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionListSize, cancellationToken);
			await writer.WriteAsync(condition.ConditionListSize.Value, cancellationToken);
		}

		if (condition.ConditionLogicOperantBinder != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionLogicOperantBinder, cancellationToken);
			await writer.WriteAsync(condition.ConditionLogicOperantBinder.Value, cancellationToken);
		}

		if (condition.StockRangeLower != null)
		{
			await writer.WriteAsync(IbkrTags.StockRangeLower, cancellationToken);
			await writer.WriteAsync(condition.StockRangeLower.Value, cancellationToken);
		}

		if (condition.StockRangeUpper != null)
		{
			await writer.WriteAsync(IbkrTags.StockRangeUpper, cancellationToken);
			await writer.WriteAsync(condition.StockRangeUpper.Value, cancellationToken);
		}

		if (condition.Delta != null)
		{
			await writer.WriteAsync(IbkrTags.Delta, cancellationToken);
			await writer.WriteAsync(condition.Delta.Value, cancellationToken);
		}

		if (!condition.ConditionUnderlying.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ConditionUnderlying, cancellationToken);
			await writer.WriteAsync(condition.ConditionUnderlying, cancellationToken);
		}

		if (condition.ConditionStrike != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionStrike, cancellationToken);
			await writer.WriteAsync(condition.ConditionStrike.Value, cancellationToken);
		}

		if (condition.ConditionRight != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionRight, cancellationToken);
			await writer.WriteAsync(condition.ConditionRight.Value, cancellationToken);
		}

		if (condition.ConditionExpiry != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionExpiry, cancellationToken);
			await writer.WriteAsync(condition.ConditionExpiry.Value, YearMonthParser, cancellationToken);
		}

		if (!condition.ConditionSecurityType.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ConditionSecurityType, cancellationToken);
			await writer.WriteAsync(condition.ConditionSecurityType, cancellationToken);
		}

		if (!condition.ConditionLocalSymbol.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ConditionLocalSymbol, cancellationToken);
			await writer.WriteAsync(condition.ConditionLocalSymbol, cancellationToken);
		}

		if (condition.DiscretionaryType != null)
		{
			await writer.WriteAsync(IbkrTags.DiscretionaryType, cancellationToken);
			await writer.WriteAsync(condition.DiscretionaryType.Value, cancellationToken);
		}

		if (condition.ForceOnlyRTH != null)
		{
			await writer.WriteAsync(IbkrTags.ForceOnlyRTH, cancellationToken);
			await writer.WriteAsync(condition.ForceOnlyRTH.Value, cancellationToken);
		}

		if (condition.LegLocateReqd != null)
		{
			await writer.WriteAsync(IbkrTags.LegLocateReqd, cancellationToken);
			await writer.WriteAsync(condition.LegLocateReqd.Value, cancellationToken);
		}

		if (!condition.LegLocateBroker.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.LegLocateBroker, cancellationToken);
			await writer.WriteAsync(condition.LegLocateBroker, cancellationToken);
		}

		if (!condition.CondPrimaryExch.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.CondPrimaryExch, cancellationToken);
			await writer.WriteAsync(condition.CondPrimaryExch, cancellationToken);
		}

		if (!condition.CondCurrency.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.CondCurrency, cancellationToken);
			await writer.WriteAsync(condition.CondCurrency, cancellationToken);
		}

		if (condition.ConditionType != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionType, cancellationToken);
			await writer.WriteAsync(condition.ConditionType.Value, cancellationToken);
		}

		if (condition.ConditionTime != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionTime, cancellationToken);
			await writer.WriteAsync(condition.ConditionTime.Value.ToUniversalTime(), TimeStampParser, cancellationToken);
		}

		if (condition.ConditionMargin != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionMargin, cancellationToken);
			await writer.WriteAsync(condition.ConditionMargin.Value, cancellationToken);
		}

		if (!condition.ConditionExecutionPattern.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ConditionExecutionPattern, cancellationToken);
			await writer.WriteAsync(condition.ConditionExecutionPattern, cancellationToken);
		}

		if (condition.SmartComboGuarantee != null)
		{
			await writer.WriteAsync(IbkrTags.SmartComboGuarantee, cancellationToken);
			await writer.WriteAsync(condition.SmartComboGuarantee.Value, cancellationToken);
		}

		if (condition.NoBarriers != null)
		{
			await writer.WriteAsync(IbkrTags.NoBarriers, cancellationToken);
			await writer.WriteAsync(condition.NoBarriers.Value, cancellationToken);
		}

		if (condition.BarrierPrice != null)
		{
			await writer.WriteAsync(IbkrTags.BarrierPrice, cancellationToken);
			await writer.WriteAsync(condition.BarrierPrice.Value, cancellationToken);
		}

		if (condition.BarrierStopPrice != null)
		{
			await writer.WriteAsync(IbkrTags.BarrierStopPrice, cancellationToken);
			await writer.WriteAsync(condition.BarrierStopPrice.Value, cancellationToken);
		}

		if (condition.BarrierTrailingAmt != null)
		{
			await writer.WriteAsync(IbkrTags.BarrierTrailingAmt, cancellationToken);
			await writer.WriteAsync(condition.BarrierTrailingAmt.Value, cancellationToken);
		}

		if (!condition.BarrierPriceDelimiter.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.BarrierPriceDelimiter, cancellationToken);
			await writer.WriteAsync(condition.BarrierPriceDelimiter, cancellationToken);
		}

		if (condition.BarrierLimitPrice != null)
		{
			await writer.WriteAsync(IbkrTags.BarrierLimitPrice, cancellationToken);
			await writer.WriteAsync(condition.BarrierLimitPrice.Value, cancellationToken);
		}

		if (condition.ConditionVolume != null)
		{
			await writer.WriteAsync(IbkrTags.ConditionVolume, cancellationToken);
			await writer.WriteAsync(condition.ConditionVolume.Value, cancellationToken);
		}

		if (!condition.TrailingAmtUnit.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.TrailingAmtUnit, cancellationToken);
			await writer.WriteAsync(condition.TrailingAmtUnit, cancellationToken);
		}

		if (!condition.BarrierTrailingAmtUnit.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.BarrierTrailingAmtUnit, cancellationToken);
			await writer.WriteAsync(condition.BarrierTrailingAmtUnit, cancellationToken);
		}

		if (condition.CheapToReroute != null)
		{
			await writer.WriteAsync(IbkrTags.CheapToReroute, cancellationToken);
			await writer.WriteAsync(condition.CheapToReroute.Value, cancellationToken);
		}

		if (!condition.ContinuousUpdate.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ContinuousUpdate, cancellationToken);
			await writer.WriteAsync(condition.ContinuousUpdate, cancellationToken);
		}

		if (!condition.UnderlyingRefPrice.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.UnderlyingRefPrice, cancellationToken);
			await writer.WriteAsync(condition.UnderlyingRefPrice, cancellationToken);
		}

		if (!condition.XcrossC1OrdID.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.XcrossC1OrdID, cancellationToken);
			await writer.WriteAsync(condition.XcrossC1OrdID, cancellationToken);
		}

		if (!condition.XCrossClearingFirm.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.XCrossClearingFirm, cancellationToken);
			await writer.WriteAsync(condition.XCrossClearingFirm, cancellationToken);
		}

		if (!condition.XCrossClearingAccount.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.XCrossClearingAccount, cancellationToken);
			await writer.WriteAsync(condition.XCrossClearingAccount, cancellationToken);
		}

		if (!condition.XCrossOpenClose.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.XCrossOpenClose, cancellationToken);
			await writer.WriteAsync(condition.XCrossOpenClose, cancellationToken);
		}

		if (!condition.XCrossOptionAcct.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.XCrossOptionAcct, cancellationToken);
			await writer.WriteAsync(condition.XCrossOptionAcct, cancellationToken);
		}

		if (condition.FacilitationPercentage != null)
		{
			await writer.WriteAsync(IbkrTags.FacilitationPercentage, cancellationToken);
			await writer.WriteAsync(condition.FacilitationPercentage.Value, cancellationToken);
		}

		if (condition.NotHeld != null)
		{
			await writer.WriteAsync(IbkrTags.NotHeld, cancellationToken);
			await writer.WriteAsync(condition.NotHeld.Value, cancellationToken);
		}

		if (!condition.HedgingType.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.HedgingType, cancellationToken);
			await writer.WriteAsync(condition.HedgingType, cancellationToken);
		}

		if (condition.TrailLimitOffset != null)
		{
			await writer.WriteAsync(IbkrTags.TrailLimitOffset, cancellationToken);
			await writer.WriteAsync(condition.TrailLimitOffset.Value, cancellationToken);
		}

		if (condition.StagedOrder != null)
		{
			await writer.WriteAsync(IbkrTags.StagedOrder, cancellationToken);
			await writer.WriteAsync(condition.StagedOrder.Value, cancellationToken);
		}

		if (condition.DeactivateOnClose != null)
		{
			await writer.WriteAsync(IbkrTags.DeactivateOnClose, cancellationToken);
			await writer.WriteAsync(condition.DeactivateOnClose.Value, cancellationToken);
		}

		if (!condition.DividendSchedule.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.DividendSchedule, cancellationToken);
			await writer.WriteAsync(condition.DividendSchedule, cancellationToken);
		}

		if (!condition.InterestSchedule.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.InterestSchedule, cancellationToken);
			await writer.WriteAsync(condition.InterestSchedule, cancellationToken);
		}

		if (!condition.IsDeltaHedge.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.IsDeltaHedge, cancellationToken);
			await writer.WriteAsync(condition.IsDeltaHedge, cancellationToken);
		}

		if (condition.VolatCapPercentage != null)
		{
			await writer.WriteAsync(IbkrTags.VolatCapPercentage, cancellationToken);
			await writer.WriteAsync(condition.VolatCapPercentage.Value, cancellationToken);
		}

		if (!condition.VolatCapTicks.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.VolatCapTicks, cancellationToken);
			await writer.WriteAsync(condition.VolatCapTicks, cancellationToken);
		}

		if (condition.ConsiderExecCost != null)
		{
			await writer.WriteAsync(IbkrTags.ConsiderExecCost, cancellationToken);
			await writer.WriteAsync(condition.ConsiderExecCost.Value, cancellationToken);
		}

		if (condition.CondSubmitCancel != null)
		{
			await writer.WriteAsync(IbkrTags.CondSubmitCancel, cancellationToken);
			await writer.WriteAsync(condition.CondSubmitCancel.Value, cancellationToken);
		}

		if (!condition.StockRefPrice.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.StockRefPrice, cancellationToken);
			await writer.WriteAsync(condition.StockRefPrice, cancellationToken);
		}

		if (condition.ProfessionalCustomer != null)
		{
			await writer.WriteAsync(IbkrTags.ProfessionalCustomer, cancellationToken);
			await writer.WriteAsync(condition.ProfessionalCustomer.Value, cancellationToken);
		}

		if (condition.HedgeType != null)
		{
			await writer.WriteAsync(IbkrTags.HedgeType, cancellationToken);
			await writer.WriteAsync(condition.HedgeType.Value, cancellationToken);
		}

		if (condition.HedgeRatio != null)
		{
			await writer.WriteAsync(IbkrTags.HedgeRatio, cancellationToken);
			await writer.WriteAsync(condition.HedgeRatio.Value, cancellationToken);
		}

		if (!condition.LegClearingFirm.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.LegClearingFirm, cancellationToken);
			await writer.WriteAsync(condition.LegClearingFirm, cancellationToken);
		}

		if (condition.UseNetPrice != null)
		{
			await writer.WriteAsync(IbkrTags.UseNetPrice, cancellationToken);
			await writer.WriteAsync(condition.UseNetPrice.Value, cancellationToken);
		}

		if (condition.ImbalanceOnly != null)
		{
			await writer.WriteAsync(IbkrTags.ImbalanceOnly, cancellationToken);
			await writer.WriteAsync(condition.ImbalanceOnly.Value, cancellationToken);
		}

		if (!condition.Mifid2DecisionMakerShortCode.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.Mifid2DecisionMakerShortCode, cancellationToken);
			await writer.WriteAsync(condition.Mifid2DecisionMakerShortCode, cancellationToken);
		}

		if (!condition.Mifid2DecisionAlgo.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.Mifid2DecisionAlgo, cancellationToken);
			await writer.WriteAsync(condition.Mifid2DecisionAlgo, cancellationToken);
		}

		if (!condition.Mifid2ExecutionTrader.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.Mifid2ExecutionTrader, cancellationToken);
			await writer.WriteAsync(condition.Mifid2ExecutionTrader, cancellationToken);
		}

		if (!condition.Mifid2ExecutionAlgo.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.Mifid2ExecutionAlgo, cancellationToken);
			await writer.WriteAsync(condition.Mifid2ExecutionAlgo, cancellationToken);
		}

		if (!condition.AllowPastEndTime.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.AllowPastEndTime, cancellationToken);
			await writer.WriteAsync(condition.AllowPastEndTime, cancellationToken);
		}

		if (condition.DisplaySize != null)
		{
			await writer.WriteAsync(IbkrTags.DisplaySize, cancellationToken);
			await writer.WriteAsync(condition.DisplaySize.Value, cancellationToken);
		}

		if (condition.EndTime != null)
		{
			await writer.WriteAsync(IbkrTags.EndTime, cancellationToken);
			await writer.WriteAsync(condition.EndTime.Value, TimeStampParser, cancellationToken);
		}

		if (!condition.ForceCompletion.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ForceCompletion, cancellationToken);
			await writer.WriteAsync(condition.ForceCompletion, cancellationToken);
		}

		if (condition.PctVol != null)
		{
			await writer.WriteAsync(IbkrTags.PctVol, cancellationToken);
			await writer.WriteAsync(condition.PctVol.Value, cancellationToken);
		}

		if (!condition.RiskAversion.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.RiskAversion, cancellationToken);
			await writer.WriteAsync(condition.RiskAversion, cancellationToken);
		}

		if (condition.StartTime != null)
		{
			await writer.WriteAsync(IbkrTags.StartTime, cancellationToken);
			await writer.WriteAsync(condition.StartTime.Value, TimeStampParser, cancellationToken);
		}

		if (!condition.ImpVolatility.IsEmpty())
		{
			await writer.WriteAsync(IbkrTags.ImpVolatility, cancellationToken);
			await writer.WriteAsync(condition.ImpVolatility, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override bool IsLogoutError(string text)
	{
		return !text.IsEmpty() && !text.EqualsIgnoreCase("replying to logout");
	}
}