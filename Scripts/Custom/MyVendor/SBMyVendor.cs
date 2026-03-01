////////////////////////////////////////////
// Don't expect to discover a copyright   //
// for an illegal emulator use.           //
////////////////////////////////////////////
using System;
using System.Collections;
using System.Collections.Generic;
using Server.Items;

namespace Server.Mobiles
{
	public class SBMyVendor: SBInfo
	{
		private List<GenericBuyInfo> m_BuyInfo = new InternalBuyInfo();
		private IShopSellInfo m_SellInfo = new InternalSellInfo();

		public SBMyVendor()
		{
		}

		public override IShopSellInfo SellInfo { get { return m_SellInfo; } }
		public override List<GenericBuyInfo> BuyInfo { get { return m_BuyInfo; } }

		public void CleanUp()
		{
			m_BuyInfo.Clear();
		}

		public void AddItem(Item aItem,double aRate,Type aType)
		{
			((InternalBuyInfo)m_BuyInfo).AddItem(aItem,aRate,aType);
			((InternalSellInfo)m_SellInfo).AddItem(aItem,aRate,aType);
		}

		public class InternalBuyInfo : List<GenericBuyInfo>
		{
		  public void AddItem(Item aItem,double aRate,Type aType)
		  {
		  if (aItem is BaseBeverage)
			{
			if ( aItem.Name == null )
				Add( new BeverageBuyInfo( aType, ((BaseBeverage)aItem).Content, (int)(aItem.Weight*aRate), aItem.Amount ,aItem.ItemID, aItem.Hue ) );
			else
				Add( new BeverageBuyInfo( aItem.Name, aType, ((BaseBeverage)aItem).Content,(int)(aItem.Weight*aRate), aItem.Amount ,aItem.ItemID, aItem.Hue ) );
			return;
			}		
		  // Name Type Prix Nombre ID Hue
		  if ( aItem.Name == null )
			Add( new GenericBuyInfo( aType, (int)(aItem.Weight*aRate), aItem.Amount ,aItem.ItemID, aItem.Hue ) );
		  else
			Add( new GenericBuyInfo( aItem.Name, aType, (int)(aItem.Weight*aRate), aItem.Amount ,aItem.ItemID, aItem.Hue ) );
		  }

		  public InternalBuyInfo()
		  {
		  }
		}

		public class InternalSellInfo : GenericSellInfo
		{
			public void AddItem(Item aItem,double aRate,Type aType)
			{
				// Type Prix Nombre ID Hue
				Add( aType , (int)(aItem.Weight*aRate/3) );				
			}

			public InternalSellInfo()
			{
			}
		}
	}
}