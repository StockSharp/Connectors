namespace StockSharp.Paxos;

/// <summary>Paxos brokerage and custody parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PaxosKey)]
public sealed class PaxosOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Native operation kind.</summary>
	[DataMember]
	[Display(
		Name = "Operation",
		GroupName = "Paxos",
		Order = 0)]
	public PaxosOperations Operation
	{
		get => (PaxosOperations?)Parameters.TryGetValue(nameof(Operation)) ??
			PaxosOperations.Trade;
		set => Parameters[nameof(Operation)] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey,
		GroupName = "Paxos",
		Order = 1)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>Quote amount required by a market buy.</summary>
	[DataMember]
	[Display(
		Name = "Quote amount",
		GroupName = "Paxos",
		Order = 2)]
	public decimal? QuoteAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(QuoteAmount));
		set => Parameters[nameof(QuoteAmount)] = value;
	}

	/// <summary>Submit a post-only limit order.</summary>
	[DataMember]
	[Display(
		Name = "Post only",
		GroupName = "Paxos",
		Order = 3)]
	public bool IsPostOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsPostOnly)) ?? false;
		set => Parameters[nameof(IsPostOnly)] = value;
	}

	/// <summary>Identity requesting an operation.</summary>
	[DataMember]
	[Display(
		Name = "Identity ID",
		GroupName = "Paxos",
		Order = 4)]
	public string IdentityId
	{
		get => (string)Parameters.TryGetValue(nameof(IdentityId));
		set => Parameters[nameof(IdentityId)] = value?.Trim();
	}

	/// <summary>Account associated with <see cref="IdentityId"/>.</summary>
	[DataMember]
	[Display(
		Name = "Identity account ID",
		GroupName = "Paxos",
		Order = 5)]
	public string IdentityAccountId
	{
		get => (string)Parameters.TryGetValue(nameof(IdentityAccountId));
		set => Parameters[nameof(IdentityAccountId)] = value?.Trim();
	}

	/// <summary>Profile receiving trade settlement or transferred assets.</summary>
	[DataMember]
	[Display(
		Name = "Destination profile",
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 0)]
	public string DestinationProfileId
	{
		get => (string)Parameters.TryGetValue(nameof(DestinationProfileId));
		set => Parameters[nameof(DestinationProfileId)] = value?.Trim();
	}

	/// <summary>External crypto destination address.</summary>
	[DataMember]
	[Display(
		Name = "Destination address",
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 1)]
	public string DestinationAddress
	{
		get => (string)Parameters.TryGetValue(nameof(DestinationAddress));
		set => Parameters[nameof(DestinationAddress)] = value?.Trim();
	}

	/// <summary>Crypto withdrawal network.</summary>
	[DataMember]
	[Display(
		Name = "Crypto network",
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 2)]
	public PaxosCryptoNetworks CryptoNetwork
	{
		get => (PaxosCryptoNetworks?)Parameters.TryGetValue(
			nameof(CryptoNetwork)) ?? PaxosCryptoNetworks.Unknown;
		set => Parameters[nameof(CryptoNetwork)] = value;
	}

	/// <summary>Destination asset for a stablecoin conversion.</summary>
	[DataMember]
	[Display(
		Name = "Destination asset",
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 3)]
	public string DestinationAsset
	{
		get => (string)Parameters.TryGetValue(nameof(DestinationAsset));
		set => Parameters[nameof(DestinationAsset)] = value?.Trim();
	}

	/// <summary>Optional blockchain memo.</summary>
	[DataMember]
	[Display(
		Name = "Memo",
		GroupName = LocalizedStrings.WithdrawKey,
		Order = 4)]
	public string Memo
	{
		get => (string)Parameters.TryGetValue(nameof(Memo));
		set => Parameters[nameof(Memo)] = value?.Trim();
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new PaxosOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
