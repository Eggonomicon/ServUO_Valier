
ECONOMIE LOCAL

MyVendor system : in-game customizable vendors

SETUP:
A) Drag'n drop the files in your custom folder.

B) In Scripts\Mobiles\Normal\BaseCreature.cs
Add those lines in the switch in the procedure called "public void ChangeAIType(AIType NewAI)":
/*** ADDED MyVendor system ***/
                case AIType.AI_MyVendor:
                    m_AI = new MyVendorAI(this);
                    break;
/*** END***/

C) There is one OPTIONAL change to make in the core to make this work even further with hues:
In Scripts\VendorInfo\GenreicBuy.cs,
Replace this:

        public virtual IEntity GetEntity()
        {
            if (m_Args == null || m_Args.Length == 0)
                return (IEntity)Activator.CreateInstance(m_Type);

            return (IEntity)Activator.CreateInstance(m_Type, m_Args);
            //return (Item)Activator.CreateInstance( m_Type );	
        }
		
by this:

        public virtual IEntity GetEntity()
        {
/*** REMOVED ***
            if (m_Args == null || m_Args.Length == 0)
                return (IEntity)Activator.CreateInstance(m_Type);

            return (IEntity)Activator.CreateInstance(m_Type, m_Args);
            //return (Item)Activator.CreateInstance( m_Type );	
*** ADDED ***/
// "MyVendor" Alambik's system 
			object myObject;
			
			if ( m_Args == null || m_Args.Length == 0 )
				myObject=Activator.CreateInstance( m_Type );
			else
				myObject=Activator.CreateInstance( m_Type, m_Args );

			if ( m_Type == typeof(Item) )
			{
				((Item)myObject).ItemID=m_ItemID;
				((Item)myObject).Hue=m_Hue;
                /* Next line not well managed. Commented.*/
				/*((Item)myObject).Name=m_Name;*/
			}

			return (IEntity)myObject;
/*** END ***/
        }

1) BASIC USAGE:
- Add a MyVendor NPC
- Add a MyVendorBag container and drop it in the NPC's backpack
- Now add in the MyVendorBag all items you want it to sell, set the property of the items as follow:
 o Amount : the maximum quantity of items that the vendor propose
 o Weight : the price per unit
- Finaly, double click the NPC, he is ready to sell & buy

Properties of a MyVendor NPC:
- Rate (default 1.0): you can set a multiplier to sell the items cheaper or more expensive for this NPC. Example: value =1,1 will sell (and buy) 10% more than usual.
- RandomBuy (default (false): if set to true, the NPC will propose only one of the items, in a random manner, and will change periodicaly.

Basic capabilities of the MyVendorBag container:
A context menu is available on the MyVendorBag.
- Repeat : Dupe the bag with its containt (very useful for several vendor of same type)
- Copy : After targeting a standard vendor, it creates a bag with the items that the standard vendor is selling. It configure those items to mach the same price and amount. Very useful to start from a standard vendor and then customize it.

2) ADVANCE USAGE: LEVEL 1
Now that you have created a MyVendorBag, you may wish to dupe it to have another vendor that sell the same somewhere else.
You can do this with the "Repeat" context menu of the MyVendorBag item.
But wait! What if you want to add another item to sell? You will have to add it to all the duped MyVendorBags...
That's where the MyVendorVirtualbag.
- Move your MyVendorBag somewhere in a secure place, like in your backpack.
- Add a MyVendorVirtualBag
- Double click the MyVendorVirtualBag and target the MyVendorBag: the MyVendorVirtualBag is now pointing to the MyVendorBag
- Drop the MyVendorVirtualBag in the NPC's backpack
- Finaly, double click the NPC, he is ready to sell & buy
- Do the same for all the other vendors.
Now any change in the original MyVendorBag will impact all the vendors with the MyVendorBag (at restart or by doubl clicking the vendor)
And guess what... You can add several different MyVendorVirtualBag, mixing what the vendor sell...

3) ADVANCE USAGE: LEVEL 2
Ok now, let's develop commerce between cities, regions...
You can associate the type of resources a MyVendorBag is mainly related to.
Let's say you have a bag plenty of tools/furniture made of "wood".
You can set this resource in the MyVendorBag "ResourceType" property. Try it.
You can do it for other resource type on another MyVendorBag, like for example "vegetables".
Now, let's add some regional specific price rate according those resourcetypes:
- Add a MyVendorStone
- Move it somewhere in a secure place, like in your backpack
- Rename it with the region or city you want
- Double click on it and target a MyVendorVirtualBag, let's stay the one for "wood" stuffs in the example
- You can continue targetting other MyVendorVirtualBag, let's stay the one for "vegetables" stuffs of the example
- Now open the properties of the MyVendorStone and set the rate of each Resource Type (example: 1,2 well set the price 20% more expensive for wood, while 0,9 on stone will make the good cheapest by 10%)
This allows you to completely define econimical commerce system, where cities propose items cheaper for a type of resource because they have a lot of them, while other items are expensive because of the rare resource type.
Players will then start to understand that, and can do benefit by travelling from one city to another and buying/reselling goods.

Things you can also do with through the MyVendorStone properties:
- AAA_LocalRate : this is the rate to apply globally to all virtual bags associated to this region/city stone
- AAA_CommonGlobalEconomyRate (static): this is a common static rate that applies to the whole economy, if you want to raise all the price globaly, whatever place you are. If you are keen at developping, you may increase for example this value by a timer script to generate a global world inflation.
- AAA_CommonStoneEconomyRate (static): this rate applies only to virtual bags that are linked to a stones.

Things you may setup in the MyVendor properties:
- vendorLanguage (static): if set to 1, the language used will be french insted of English

Things you may setup in the MyVendorAI script:
- set the SpecilalSpeak to true if you use french and wants the vendor to randomly react to the player speech


And, that's it! Again another very old RunUO 1.0 script of mine updated for you. Enjoy!

ALAMBIK/ALCHEMY




