namespace StockSharp.Fireblocks;

/// <summary>Fireblocks transfer parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FireblocksKey)]
public sealed class FireblocksOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Destination peer type.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DestinationTypeKey,
		Description = LocalizedStrings.FireblocksDestinationPeerTypeDescKey,
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 0)]
	public FireblocksPeerTypes DestinationType
	{
		get => (FireblocksPeerTypes?)Parameters.TryGetValue(
			nameof(DestinationType)) ?? FireblocksPeerTypes.OneTimeAddress;
		set => Parameters[nameof(DestinationType)] = value;
	}

	/// <summary>
	/// Destination object identifier. Not used for a one-time address.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DestinationIdKey,
		Description = LocalizedStrings.FireblocksDestinationObjectIdentifierDescKey,
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 1)]
	public string DestinationId
	{
		get => (string)Parameters.TryGetValue(nameof(DestinationId));
		set => Parameters[nameof(DestinationId)] = value?.Trim();
	}

	/// <summary>Network fee level.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FeeLevelKey,
		Description = LocalizedStrings.FireblocksNetworkFeeLevelDescKey,
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 2)]
	public FireblocksFeeLevels FeeLevel
	{
		get => (FireblocksFeeLevels?)Parameters.TryGetValue(nameof(FeeLevel)) ??
			FireblocksFeeLevels.Medium;
		set => Parameters[nameof(FeeLevel)] = value;
	}

	/// <summary>Whether the network fee is deducted from the amount.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.GrossAmountKey,
		Description = LocalizedStrings.DeductTheNetworkFeeFromTheRequestedAmountDescKey,
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 3)]
	public bool IsGrossAmount
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsGrossAmount)) ?? false;
		set => Parameters[nameof(IsGrossAmount)] = value;
	}

	/// <summary>Workspace note not published on-chain.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CommentKey,
		Description = LocalizedStrings.CommentKey,
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 4)]
	public string Note
	{
		get => (string)Parameters.TryGetValue(nameof(Note));
		set => Parameters[nameof(Note)] = value?.Trim();
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new FireblocksOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
