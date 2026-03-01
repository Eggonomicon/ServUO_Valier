////////////////////////////////////////////
// Don't expect to discover a copyright   //
// for an illegal emulator use.           //
////////////////////////////////////////////
using System;
using System.Collections;
using System.Collections.Generic;
using Server;
using Server.Items;
using Server.Gumps;

namespace Server.Mobiles
{
	public class MyVendor: BaseVendor
	{
		///////////////////////////////////
		// 0 for english 
		// 1 for french  
		private static int  m_Language   = 1 ;
		///////////////////////////////////
		// "true"  if you have made the code changes in BasCreature.cs
		// "false" if you have changed nothing.
		private static bool m_MyVendorAI = false;
		///////////////////////////////////

		public int vendorLanguage
		{
			get{ return m_Language; }
			set{ m_Language = value; }
		}

		private List<SBInfo> m_SBInfos = new List<SBInfo>();
		private SBMyVendor m_SBMyVendor = new SBMyVendor();
//		private bool m_AlreadyLoaded;
		private bool m_firstBuyCommand;
		private InternalTimer it; 

		protected override List<SBInfo> SBInfos{ get { return m_SBInfos; } }

		private static bool m_DEBUG=false;
		[CommandProperty( AccessLevel.GameMaster )]
		public static bool DEBUG
		{
			get{ return m_DEBUG; }
			set{ m_DEBUG = value; }
		}

		double m_Rate;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Rate
		{
			get{ return m_Rate; }
			set{ m_Rate = value; }
		}

		[Constructable]
		public MyVendor() : base( "" )
		{
//			m_AlreadyLoaded=false;
			m_Rate=1.0;
			m_firstBuyCommand=true;
			if (m_Language == 1)
				Title="(vendeur)";
			else
				Title="(vendor)";
			if (m_MyVendorAI)
				ChangeAIType(AIType.AI_MyVendor);
			if (it == null)
				it=new InternalTimer(this); 
			it.Start(); 
		}

		public override void Restock()
		{
			if ( m_RandomBuy )
				UpdateSellAndBuy();
			base.Restock();
		}
		
		private bool m_RandomBuy;
		[CommandProperty( AccessLevel.GameMaster )]
		public bool RandomBuy
		{
			get{ return m_RandomBuy; }
			set{ m_RandomBuy = value; }
		}

		public DateTime RandomBuyLastRestock;

		public override void InitSBInfo()
		{
//			if (!m_AlreadyLoaded)
//			{
				m_SBInfos.Add( m_SBMyVendor );
//				m_AlreadyLoaded=true;
//			}
		}

		public void UpdateSellAndBuy()
		{
			if ( Backpack == null )
				return;
			List<Server.Item> items = Backpack.Items;
			m_SBMyVendor.CleanUp();
			int    RandomBuyCounter=0;
			Item   RandomBuyItem=null;
			double RandomBuyRate=0.0;
			Type   RandomBuyType=null;
			for ( int i = 0; i < items.Count; ++i )
			{
				double rate=m_Rate;
				Item item=null;

				if (items[i] is Item)
					item = (Item)items[i];

				if (item != null)
				{
					if (item is MyVendorVirtualBag)
					{
						if ( ((MyVendorVirtualBag)item).Bag != null )
						if ( !(((MyVendorVirtualBag)item).Bag.Deleted) )
						{
							rate=rate*((MyVendorVirtualBag)item).getRate();
							item=((MyVendorVirtualBag)item).Bag;
							if ( !(item is MyVendorBag) )
							{
								m_SBMyVendor.AddItem(item,rate,item.GetType());
								item=null;
							}
						}
					}
				}

				if (item != null)
				{				
					if (item is MyVendorBag)
					{
						List<Server.Item> subitems = ((MyVendorBag)item).Items;
						for ( int j = 0; j < subitems.Count; ++j )
						{
							Item subitem=null;
							if ( subitems[j] is Item)
								subitem = (Item)subitems[j];
							if (subitem != null)
								if (!(subitem is Gold))
								{
									if (!RandomBuy)
									{
										m_SBMyVendor.AddItem(subitem,rate,subitems[j].GetType());
									}
									else
									{
										RandomBuyCounter++;
										if ( (RandomBuyCounter == 1) || (Utility.Random(RandomBuyCounter) == 1))
										{
											RandomBuyItem=subitem;
											RandomBuyRate=rate;
											RandomBuyType=subitems[j].GetType();
										}
									}
								}
						}
					}
					else
					{
						if (!(item is Gold))
						{
							if (!RandomBuy)
							{
								m_SBMyVendor.AddItem(item,m_Rate,items[i].GetType());
							}
							else
							{
								RandomBuyCounter++;
								if ( (RandomBuyCounter == 1) || (Utility.Random(RandomBuyCounter) == 1))
								{
									RandomBuyItem=item;
									RandomBuyRate=m_Rate;
									RandomBuyType=items[i].GetType();
								}
							}
						}
					}
				}
			}
			if (RandomBuy)
			{
				RandomBuyLastRestock=DateTime.Now;
				if (RandomBuyCounter>0)
				{
					m_SBMyVendor.AddItem(RandomBuyItem,RandomBuyRate,RandomBuyType);
				}
			}
		}

		public override VendorShoeType ShoeType
		{
			get{ return Utility.RandomBool() ? VendorShoeType.Shoes : VendorShoeType.Sandals; }
		}

		public override void InitOutfit()
		{
			base.InitOutfit();
		}

		public MyVendor( Serial serial ) : base( serial )
		{
		}

		public override void Serialize( GenericWriter writer )
		{
			base.Serialize( writer );

			writer.Write( (int) 2 ); // version
			writer.Write( m_RandomBuy );
			writer.Write( m_Rate );
		}

		public override void Deserialize( GenericReader reader )
		{
//			m_AlreadyLoaded=false;
			base.Deserialize( reader );

			int version = reader.ReadInt();
			switch(version)
			{
				case 2:
					m_RandomBuy = reader.ReadBool();
					goto case 1;
				case 1:
					if (version == 1) 
						m_RandomBuy = reader.ReadBool(); //OUPS
					m_Rate = reader.ReadDouble();
					goto case 0;
				case 0:
				break;
			}
			m_firstBuyCommand=true;
			UpdateSellAndBuy();
			LoadSBInfo();
			if (m_MyVendorAI)
				ChangeAIType(AIType.AI_MyVendor);
			if (it == null)
				it=new InternalTimer(this); 
			it.Start(); 
		}

		public override void VendorSell( Mobile from )
		{
			if (m_firstBuyCommand)
			{
				UpdateSellAndBuy();
				LoadSBInfo();
				m_firstBuyCommand=false;
			}
			else if (RandomBuy)
			{
				if ( DateTime.Now > RandomBuyLastRestock + TimeSpan.FromMinutes(10.0) )
				{
					UpdateSellAndBuy();
					LoadSBInfo();
				}
			}
			base.VendorSell(from);
		}

		public override void VendorBuy( Mobile from )
		{
			if (m_firstBuyCommand)
			{
				UpdateSellAndBuy();
				LoadSBInfo();
				m_firstBuyCommand=false;
			}
			else if (RandomBuy)
			{
				if ( DateTime.Now > RandomBuyLastRestock + TimeSpan.FromMinutes(10.0) )
				{
					UpdateSellAndBuy();
					LoadSBInfo();
				}
			}
			base.VendorBuy(from);
		}

		public override void OnDoubleClick( Mobile from )
		{
			if (from.AccessLevel >= AccessLevel.GameMaster)
			{
				from.SendGump(new PlayerVendorCustomizeGump( this, from ));
				if (m_Language == 0)
					Emote("Rearrange his stock");
				else
					Emote("Change les marchandises de son échoppe");
				UpdateSellAndBuy();
				LoadSBInfo();
			}
			if (RandomBuy)
			{
				if ( DateTime.Now > RandomBuyLastRestock + TimeSpan.FromHours(6.0) )
				{
					RandomBuyLastRestock=DateTime.Now;
					UpdateSellAndBuy();
					LoadSBInfo();
				} else {
					if (m_Language == 0)
					{
						Say("I sell this... If you buy it now, I can propose another thing in few moment...");
						Say("Yoou can also wait tomorrow, maybe I will have something more interesting for you...");
					} else {
						Say("J'ai ceci... Si vous me l'acheter, je pourrai vous proposer autre chose dans peu de temps...");
						Say("Vous pouvez aussi attendre demain, peut-etre aurai-je autre chose de plus interessant...");
					}
				}
			}
			base.OnDoubleClick(from);
		}

		private class InternalTimer : Timer 
		{
			private MyVendor m;
			
			public InternalTimer(MyVendor arg_m) : base( TimeSpan.FromHours(2.0),TimeSpan.FromHours( 2.0+(double)(Utility.Random(2)) ) ) 
			{
				Priority = TimerPriority.OneMinute;
				m=arg_m;
			}
			protected override void OnTick() 
			{
				if (m == null)
					{	Stop(); return; }
				if (m.Deleted)
					{	Stop(); return; }
				if (!m.Alive)
					{	Stop(); return; }
				m.UpdateSellAndBuy();
			} 
		}
	}
}