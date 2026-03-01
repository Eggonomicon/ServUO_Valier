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
	public enum MyVendorResourceType
	{
		Invalid=0,
		Stone,
		PreciousStones,
		Wood,
		Metal,
		Cloths,
		SavageAnimals,
		HomeAnimals,
		Fish,
		Alcohol,
		DrinkableWater,
		Cereals,
		FruitsAndVegetables,
		Other_0,
		Other_1,
		Other_2,
		Other_3
	}


	public class MyVendorStone : Item
	{
	
		public double getResourceValue(MyVendorResourceType res)
		{
			double result=AAA_CommonGlobalEconomyRate*(AAA_CommonStoneEconomyRate*(AAA_LocalRate-1)+1);
			switch(res)
			{
		case MyVendorResourceType.Stone:				result=result*Stone; break;
		case MyVendorResourceType.PreciousStones:		result=result*PreciousStones; break;
		case MyVendorResourceType.Wood:					result=result*Wood; break;
		case MyVendorResourceType.Metal:				result=result*Metal; break;
		case MyVendorResourceType.Cloths:				result=result*Cloths; break;
		case MyVendorResourceType.SavageAnimals:		result=result*SavageAnimals; break;
		case MyVendorResourceType.HomeAnimals:			result=result*HomeAnimals; break;
		case MyVendorResourceType.Fish:					result=result*Fish; break;
		case MyVendorResourceType.Alcohol:				result=result*Alcohol; break;
		case MyVendorResourceType.DrinkableWater:		result=result*DrinkableWater; break;
		case MyVendorResourceType.Cereals:				result=result*Cereals; break;
		case MyVendorResourceType.FruitsAndVegetables:	result=result*FruitsAndVegetables; break;
		case MyVendorResourceType.Other_0:				result=result*Other_0; break;
		case MyVendorResourceType.Other_1:				result=result*Other_1; break;
		case MyVendorResourceType.Other_2:				result=result*Other_2; break;
		case MyVendorResourceType.Other_3:				result=result*Other_3; break;
		default: break;
			}
			return result;
		}
	
		private double m_AAA_LocalRate=1.0;
		[CommandProperty( AccessLevel.GameMaster )]
		public double AAA_LocalRate
		{ get{ return m_AAA_LocalRate; } set{ m_AAA_LocalRate = value; } }

		private static double m_AAA_CommonGlobalEconomyRate=1.0;
		[CommandProperty( AccessLevel.GameMaster )]
		public static double AAA_CommonGlobalEconomyRate
		{ get{ return m_AAA_CommonGlobalEconomyRate; } set{ m_AAA_CommonGlobalEconomyRate = value; } }

		private static double m_AAA_CommonStoneEconomyRate=1.0;
		[CommandProperty( AccessLevel.GameMaster )]
		public static double AAA_CommonStoneEconomyRate
		{ get{ return m_AAA_CommonStoneEconomyRate; } set{ m_AAA_CommonStoneEconomyRate = value; } }

		private double m_Stone;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Stone
		{ get{ return m_Stone; } set{ m_Stone = value; } }

		private double m_PreciousStones;
		[CommandProperty( AccessLevel.GameMaster )]
		public double PreciousStones
		{ get{ return m_PreciousStones; } set{ m_PreciousStones = value; } }

		private double m_Wood;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Wood
		{ get{ return m_Wood; } set{ m_Wood = value; } }

		private double m_Metal;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Metal
		{ get{ return m_Metal; } set{ m_Metal = value; } }

		private double m_Cloths;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Cloths
		{ get{ return m_Cloths; } set{ m_Cloths = value; } }

		private double m_SavageAnimals;
		[CommandProperty( AccessLevel.GameMaster )]
		public double SavageAnimals
		{ get{ return m_SavageAnimals; } set{ m_SavageAnimals = value; } }

		private double m_HomeAnimals;
		[CommandProperty( AccessLevel.GameMaster )]
		public double HomeAnimals
		{ get{ return m_HomeAnimals; } set{ m_HomeAnimals = value; } }

		private double m_Fish;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Fish
		{ get{ return m_Fish; } set{ m_Fish = value; } }

		private double m_Alcohol;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Alcohol
		{ get{ return m_Alcohol; } set{ m_Alcohol = value; } }

		private double m_DrinkableWater;
		[CommandProperty( AccessLevel.GameMaster )]
		public double DrinkableWater
		{ get{ return m_DrinkableWater; } set{ m_DrinkableWater = value; } }

		private double m_Cereals;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Cereals
		{ get{ return m_Cereals; } set{ m_Cereals = value; } }

		private double m_FruitsAndVegetables;
		[CommandProperty( AccessLevel.GameMaster )]
		public double FruitsAndVegetables
		{ get{ return m_FruitsAndVegetables; } set{ m_FruitsAndVegetables = value; } }

		private double m_Other_0;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Other_0
		{ get{ return m_Other_0; } set{ m_Other_0 = value; } }

		private double m_Other_1;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Other_1
		{ get{ return m_Other_1; } set{ m_Other_1 = value; } }

		private double m_Other_2;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Other_2
		{ get{ return m_Other_2; } set{ m_Other_2 = value; } }

		private double m_Other_3;
		[CommandProperty( AccessLevel.GameMaster )]
		public double Other_3
		{ get{ return m_Other_3; } set{ m_Other_3 = value; } }
		
		[Constructable]
		public MyVendorStone() : base(8317)
		{
			Name="Regional economic management stone (rename it!)";
			Visible=false;
			Hue=1159;
			Stone=1.0;
			PreciousStones=1.0;
			Wood=1.0;
			Metal=1.0;
			Cloths=1.0;
			SavageAnimals=1.0;
			HomeAnimals=1.0;
			Fish=1.0;
			Alcohol=1.0;
			DrinkableWater=1.0;
			Cereals=1.0;
			FruitsAndVegetables=1.0;
			Other_0=1.0;
			Other_1=1.0;
			Other_2=1.0;
			Other_3=1.0;
		}

		public MyVendorStone( Serial serial ) : base( serial )
		{
		}

		public override void OnDoubleClick( Mobile from )
		{
			if (from.AccessLevel <= AccessLevel.Player)
			{
				Visible=false;
				return;
			}
			from.SendMessage("Select a vendor virtual bag.");
			from.Target=new InternalTarget(this);
		}

		public override void Serialize( GenericWriter writer )
		{
			base.Serialize( writer );
			writer.Write( (int) 2 ); // version
			writer.Write(AAA_CommonStoneEconomyRate);
			writer.Write(AAA_LocalRate);
			writer.Write(AAA_CommonGlobalEconomyRate);
			writer.Write(Stone);
			writer.Write(PreciousStones);
			writer.Write(Wood);
			writer.Write(Metal);
			writer.Write(Cloths);
			writer.Write(SavageAnimals);
			writer.Write(HomeAnimals);
			writer.Write(Fish);
			writer.Write(Alcohol);
			writer.Write(DrinkableWater);
			writer.Write(Cereals);
			writer.Write(FruitsAndVegetables);
			writer.Write(Other_0);
			writer.Write(Other_1);
			writer.Write(Other_2);
			writer.Write(Other_3);
		}

		public override void Deserialize( GenericReader reader )
		{
			base.Deserialize( reader );
			int version = reader.ReadInt();
			switch(version)
			{
				case 2:
					AAA_CommonStoneEconomyRate  = reader.ReadDouble();
					goto case 1;
				case 1:
					AAA_LocalRate  = reader.ReadDouble();
					break;
				default:
					break;
			}
			AAA_CommonGlobalEconomyRate  = reader.ReadDouble();
			Stone  = reader.ReadDouble();
			PreciousStones  = reader.ReadDouble();
			Wood  = reader.ReadDouble();
			Metal  = reader.ReadDouble();
			Cloths  = reader.ReadDouble();
			SavageAnimals  = reader.ReadDouble();
			HomeAnimals  = reader.ReadDouble();
			Fish  = reader.ReadDouble();
			Alcohol  = reader.ReadDouble();
			DrinkableWater  = reader.ReadDouble();
			Cereals  = reader.ReadDouble();
			FruitsAndVegetables  = reader.ReadDouble();
			Other_0  = reader.ReadDouble();
			Other_1  = reader.ReadDouble();
			Other_2  = reader.ReadDouble();
			Other_3  = reader.ReadDouble();
		}

		///////////// TARGET //////////////
		private class InternalTarget: Target
		{
			private MyVendorStone m_Stone;

			public InternalTarget(MyVendorStone stone) : base( -1, false, TargetFlags.None )			{
				m_Stone=stone;
			}

			protected override void OnTarget( Mobile from, object targeted )
			{
				if ( targeted is MyVendorVirtualBag )
				{
					((MyVendorVirtualBag)targeted).RegionalStone=m_Stone;
					((MyVendorVirtualBag)targeted).Name="Virtual bag: "+m_Stone.Name;
					from.SendMessage("The virtual bag is linked.");
					from.Target=new InternalTarget(m_Stone);
				}
			}
		}
		//////////////////////////////////////////////

	}
}
