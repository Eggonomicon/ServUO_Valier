////////////////////////////////////////////
// Don't expect to discover a copyright   //
// for an illegal emulator use.           //
////////////////////////////////////////////
using System;
using System.Collections;
using System.Reflection;
using Server;
using Server.Gumps;
using Server.Network;
using Server.Mobiles;
using Server.Targeting;

namespace Server.Items
{
	public class MyVendorVirtualBag : Item
	{
		private Item m_Bag;
		[CommandProperty( AccessLevel.GameMaster )]
		public Item Bag
		{ get{ return m_Bag; } set{ m_Bag = value; } }

		private Item m_RegionalStone;
		[CommandProperty( AccessLevel.GameMaster )]
		public  Item RegionalStone
		{ get{ return m_RegionalStone; } set{ m_RegionalStone = value; } }

		public double getRate()
		{
			double rate = MyVendorStone.AAA_CommonGlobalEconomyRate;
			if (m_Bag == null)
				return rate;
			if ( m_Bag.Deleted )
				return rate;
			if ( !(m_Bag is MyVendorBag) )
				return rate;
				
			if (m_RegionalStone == null)
				return rate;
			if ( m_RegionalStone.Deleted )
				return rate;
			if ( !(m_RegionalStone is MyVendorStone) )
				return rate;
			
			rate=((MyVendorStone)m_RegionalStone).getResourceValue(((MyVendorBag)m_Bag).ResourceType);
			return rate;
		}
		
		[Constructable]
		public MyVendorVirtualBag() : base(3705)
		{
			Name="A virtual vendor bag";
			Visible=false;
			Hue=1159;
		}

		public MyVendorVirtualBag( Serial serial ) : base( serial )
		{
		}


		public override void OnDoubleClick( Mobile from )
		{
			Name="(Not Fully Initialized)";
			if (m_RegionalStone != null)
					Name="(Not Fully Initialized) ["+m_RegionalStone.Name+"] []";
			if (m_Bag != null)
					Name="(Not Fully Initialized) [] ["+m_Bag.Name+"]";
			if (m_Bag != null)
				if (m_RegionalStone != null)
					Name="["+RegionalStone.Name+"] ["+m_Bag.Name+"]";
			if (from.AccessLevel <= AccessLevel.Player)
			{
				Visible=false;
				return;
			}
			from.SendMessage("Select a vendor bag (MyVendorBag) as a reference");
			from.Target=new InternalTarget(this);
		}

		public override void Serialize( GenericWriter writer )
		{
			base.Serialize( writer );
			writer.Write( (int) 0 ); // version
			writer.Write( m_Bag );
			writer.Write( m_RegionalStone );
		}

		public override void Deserialize( GenericReader reader )
		{
			base.Deserialize( reader );
			int version = reader.ReadInt();
			m_Bag = reader.ReadItem();
			m_RegionalStone = reader.ReadItem();
		}

		///////////// TARGET //////////////
		private class InternalTarget: Target
		{
			private MyVendorVirtualBag  m_Bag;

			public InternalTarget(MyVendorVirtualBag bag) : base( -1, false, TargetFlags.None )			{
				m_Bag=bag;
			}

			protected override void OnTarget( Mobile from, object targeted )
			{
				//if ( targeted is MyVendorBag )
				//{
					m_Bag.Bag=(Item)targeted;
					if (m_Bag.RegionalStone != null)
						m_Bag.Name="["+m_Bag.RegionalStone.Name+"] "+m_Bag.Bag.Name;
					else
						m_Bag.Name="[] "+m_Bag.Bag.Name;
					from.SendMessage("The virtual bag is now linked.");
				//}
			}
		}
		//////////////////////////////////////////////

	}
}