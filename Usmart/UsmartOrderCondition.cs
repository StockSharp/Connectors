namespace StockSharp.Usmart;

/// <summary>Additional parameters for uSMART stock orders.</summary>
[DataContract]
[Serializable]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartKey)]
public sealed class UsmartOrderCondition : OrderCondition
{
	private UsmartOrderInstructions _instruction;
	private UsmartTradingSessions _session;
	private bool _isFractional;
	private bool _forceOrder;

	/// <summary>Native order instruction.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OrderTypeKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	public UsmartOrderInstructions Instruction
	{
		get => _instruction;
		set
		{
			_instruction = value;
			Parameters[nameof(Instruction)] = value;
		}
	}

	/// <summary>U.S. trading session.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartSessionKey,
		Description = LocalizedStrings.UsmartSessionDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	public UsmartTradingSessions Session
	{
		get => _session;
		set
		{
			_session = value;
			Parameters[nameof(Session)] = value;
		}
	}

	/// <summary>Route through the fractional-share endpoint.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartFractionalKey,
		Description = LocalizedStrings.UsmartFractionalDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	public bool IsFractional
	{
		get => _isFractional;
		set
		{
			_isFractional = value;
			Parameters[nameof(IsFractional)] = value;
		}
	}

	/// <summary>Allow submission outside the normal price range.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartForceOrderKey,
		Description = LocalizedStrings.UsmartForceOrderDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	public bool ForceOrder
	{
		get => _forceOrder;
		set
		{
			_forceOrder = value;
			Parameters[nameof(ForceOrder)] = value;
		}
	}
}
