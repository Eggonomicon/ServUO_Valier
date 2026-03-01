////////////////////////////////////////////
// Don't expect to discover a copyright   //
// for an illegal emulator use.           //
////////////////////////////////////////////
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Gumps;
using Server.Network;
using Server.Mobiles;
using Server.Items;
using Server.Targeting;
using Server.ContextMenus;

namespace Server.Items
{
	public class MyVendorBag : Bag
	{
		private MyVendorResourceType m_ResourceType;
		[CommandProperty( AccessLevel.GameMaster )]
		public MyVendorResourceType ResourceType
		{ get{ return m_ResourceType; } set{ m_ResourceType = value; } }
	
		[Constructable]
		public MyVendorBag() : base()
		{
			Name="Un sac de vendeur";
			Visible=false;
			DropSound=1000;
			Hue=1159;
		}

		public MyVendorBag( Serial serial ) : base( serial )
		{
		}

		public override void GetContextMenuEntries( Mobile from, List<ContextMenuEntry> list )
		{
			base.GetContextMenuEntries( from, list );

			if ( from.AccessLevel > AccessLevel.Player )
			{
				list.Add( new DupeVendorBagContextEntry( from, this ) );
				list.Add( new CopyVendorBagContextEntry( from, this ) );
			}
		}

		public override void OnDoubleClick( Mobile from )
		{
			if (from.AccessLevel <= AccessLevel.Player)
			{
				Visible=false;
				return;
			}
			base.OnDoubleClick( from );
		}

		public override void Serialize( GenericWriter writer )
		{
			base.Serialize( writer );

			writer.Write( (int) 1 ); // version
			writer.Write( (int) ResourceType );

		}

		public override void Deserialize( GenericReader reader )
		{
			base.Deserialize( reader );

			int version = reader.ReadInt();
			switch(version)
			{
			case 1:
					ResourceType=(MyVendorResourceType)reader.ReadInt();
					goto case 0;
			case 0:
					break;
			default:
					break;
			}

		}


		//////////////////////////// DUPE ITEM IN //////////////////////////////

		public static void DupeItemIn(Item copy,BaseContainer pack)
		{
			if (pack == null) 
				return;
			Type t = copy.GetType();
			ConstructorInfo[] info = t.GetConstructors();
			foreach ( ConstructorInfo c in info )
			{
				ParameterInfo[] paramInfo = c.GetParameters();
				if ( paramInfo.Length == 0 )
				{
					object[] objParams = new object[0];
					try 
					{
						for (int i=0;i<1;i++)
						{
							object o = c.Invoke( objParams );
							if ( o != null && o is Item )
							{
								Item newItem = (Item)o;
								CopyProperties( newItem, copy );
								newItem.Parent = null;
								pack.DropItem( newItem );
							}
						}
					}
					catch
					{
						return;
					}
				}
			}
		}
		private static void CopyProperties ( Item dest, Item src ) 
		{ 
			PropertyInfo[] props = src.GetType().GetProperties(); 
			for ( int i = 0; i < props.Length; i++ ) 
			{ 
				try
				{
					if ( props[i].CanRead && props[i].CanWrite )
					{
						props[i].SetValue( dest, props[i].GetValue( src, null ), null ); 
					}
				}
				catch {}
			}
		}

		//////////////////////// CONFIRM GUMP ////////////////////////////
		public class MyVendorBagGump: Gump
		{
			private Bag m_Bag;

			public MyVendorBagGump(Bag myBag): base( 0, 0 )
			{
				m_Bag=myBag;
				this.Closable=true;
				this.Disposable=true;
				this.Dragable=true;
				this.Resizable=false;
				this.AddPage(0);
				this.AddBackground(79, 86, 256, 63, 9200);
				this.AddPage(1);
				this.AddLabel(97, 95, 0, @"Dupe this bag and all its containt ?");
				this.AddButton(124, 119, 241, 242, (int)Buttons.CANCEL, GumpButtonType.Reply, 0);
				this.AddButton(239, 119, 247, 248, (int)Buttons.OK, GumpButtonType.Reply, 0);
			}	
		
			public enum Buttons
			{
				CANCEL,
				OK
			}
	
			public override void OnResponse( NetState sender, RelayInfo info )
			{
				Mobile from = sender.Mobile;
				switch (info.ButtonID)
				{
					case (int)Buttons.OK:
						MyVendorBag result=new MyVendorBag();
						if ( from.Backpack == null)
							m_Bag.DropItem(result);
						else
							from.Backpack.DropItem(result);					
						result.Name=m_Bag.Name;
						for ( int j = 0; j < m_Bag.Items.Count; ++j )
						{
							MyVendorBag.DupeItemIn((Item)(m_Bag.Items[j]),(BaseContainer)result);
						}
						break;
					case (int)Buttons.CANCEL:
					default:
						break;
				}
			}

		}

		///////////// CONTEXT MENU (Repeat/Copy) ////////////////////
		public class  DupeVendorBagContextEntry : ContextMenuEntry
		{
			private Mobile       m_From;
			private MyVendorBag  m_Bag;

			public DupeVendorBagContextEntry(Mobile from,MyVendorBag bag):base(5020,100)
			{
				m_Bag=bag;
				m_From=from;
			}

			public override void OnClick()
			{
				m_From.SendGump(new MyVendorBagGump(m_Bag));
			}
		}

		public class  CopyVendorBagContextEntry : ContextMenuEntry
		{
			private Mobile       m_From;
			private MyVendorBag  m_Bag;

			public CopyVendorBagContextEntry(Mobile from,MyVendorBag bag):base(5044,100)
			{
				m_Bag=bag;
				m_From=from;
			}

			public override void OnClick()
			{
				m_From.SendMessage("Select the BaseVendor to copy or Hit Escape to cancel.");
				m_From.Target = new InternalTarget(m_Bag);
			}
		}

		///////////// TARGET A VENDOR //////////////
		private class InternalTarget: Target
		{
			private MyVendorBag  m_Bag;

			public InternalTarget(MyVendorBag bag) : base( -1, false, TargetFlags.None )
			{
				m_Bag=bag;
			}

			protected override void OnTarget( Mobile from, object targeted )
			{
				if ( targeted is BaseVendor )
				{
					BaseVendor vendor=(BaseVendor)targeted;
					IBuyItemInfo[] buys=vendor.GetBuyInfo();
					foreach ( IBuyItemInfo buy in buys )
					{
						object o = buy.GetEntity();
						if (o is Item)
						{
						  Item newitem = (Item)o;
						  if (newitem != null)
						  {
						 	m_Bag.DropItem(newitem);
							newitem.Weight=buy.Price;
							newitem.Amount=buy.MaxAmount;
						  }
						} else if (o is Mobile) {
							((Mobile)o).MoveToWorld(from.Location,from.Map);
						 	from.SendMessage("Shit... I don't manage Mobiles ({0}) ..."
									+((Mobile)o).Name);
						}
					}
				}
			}
		}
		//////////////////////////////////////////////

	}
}