namespace StockSharp.Paradex.Native.Derivatives;

using StockSharp.Paradex.Native.Common;

sealed class DerivativesAdapter : ParadexSectionAdapter
{
	public DerivativesAdapter(ParadexMessageAdapter owner)
		: base(owner, BoardCodes.ParadexDerivatives)
	{
	}

	public override ParadexSections Section => ParadexSections.Derivatives;
	protected override string SectionName => "Derivatives";
	protected override bool IsSpotSection => false;
	protected override SecurityTypes SectionSecurityType => SecurityTypes.Future;
}
