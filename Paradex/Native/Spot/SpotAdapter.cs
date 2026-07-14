namespace StockSharp.Paradex.Native.Spot;

using StockSharp.Paradex.Native.Common;

sealed class SpotAdapter : ParadexSectionAdapter
{
	public SpotAdapter(ParadexMessageAdapter owner)
		: base(owner, BoardCodes.ParadexSpot)
	{
	}

	public override ParadexSections Section => ParadexSections.Spot;
	protected override string SectionName => "Spot";
	protected override bool IsSpotSection => true;
	protected override SecurityTypes SectionSecurityType => SecurityTypes.CryptoCurrency;
}
