using Server;
using Server.Items;

namespace Server.Custom.ValierShop.Packs
{
    /*
     * Example “Pack Deeds” for the Valier Shop.
     *
     * IMPORTANT (ServUO serialization):
     * Every concrete Item type MUST have a constructor:
     *   public ClassName(Serial serial) : base(serial) { }
     *
     * This file includes those constructors so your world can load without:
     *   "does not have a serialization constructor"
     */

    public abstract class BasePackDeed : Item
    {
        [Constructable]
        protected BasePackDeed(int itemID) : base(itemID)
        {
            LootType = LootType.Regular;
            Weight = 1.0;
        }

        // REQUIRED for serialization (even though this is abstract)
        protected BasePackDeed(Serial serial) : base(serial)
        {
        }

        protected abstract string PackName { get; }
        protected abstract void Fill(Container pack);

        public override void OnDoubleClick(Mobile from)
        {
            if (from == null || from.Deleted)
                return;

            if (!IsChildOf(from.Backpack))
            {
                from.SendMessage("That must be in your backpack to use it.");
                return;
            }

            Bag bag = new Bag { Name = PackName };
            Fill(bag);

            if (from.Backpack != null && from.Backpack.TryDropItem(from, bag, false))
            {
                from.SendMessage(0x59, "You open the pack and receive the supplies.");
                Delete();
            }
            else
            {
                bag.Delete();
                from.SendMessage(0x22, "You don't have room in your backpack.");
            }
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
        }
    }

    // =========================
    // Crafting Packs
    // =========================
    public class BasicSmithPack : BasePackDeed
    {
        [Constructable]
        public BasicSmithPack() : base(0x14F0)
        {
            Name = "a basic blacksmith pack";
        }

        // REQUIRED for serialization
        public BasicSmithPack(Serial serial) : base(serial)
        {
        }

        protected override string PackName => "Basic Blacksmith Pack";

        protected override void Fill(Container pack)
        {
            pack.DropItem(new IronIngot(200));
            pack.DropItem(new Tongs());
            pack.DropItem(new SmithHammer());
            pack.DropItem(new Shovel());
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
        }
    }

    public class BasicTailorPack : BasePackDeed
    {
        [Constructable]
        public BasicTailorPack() : base(0x14F0)
        {
            Name = "a basic tailor pack";
        }

        // REQUIRED for serialization
        public BasicTailorPack(Serial serial) : base(serial)
        {
        }

        protected override string PackName => "Basic Tailor Pack";

        protected override void Fill(Container pack)
        {
            pack.DropItem(new Cloth(200));
            pack.DropItem(new Scissors());
            pack.DropItem(new SewingKit());
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
        }
    }

    // =========================
    // Adventure Packs
    // =========================
    public class BasicAdventurerPack : BasePackDeed
    {
        [Constructable]
        public BasicAdventurerPack() : base(0x14F0)
        {
            Name = "a basic adventurer pack";
        }

        // REQUIRED for serialization
        public BasicAdventurerPack(Serial serial) : base(serial)
        {
        }

        protected override string PackName => "Basic Adventurer Pack";

        protected override void Fill(Container pack)
        {
            pack.DropItem(new Bandage(100));
            pack.DropItem(new RecallScroll(5));
            pack.DropItem(new GreaterHealPotion(5));
            pack.DropItem(new GreaterCurePotion(5));
            pack.DropItem(new TotalRefreshPotion(3));
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
        }
    }

    public class BasicMageReagentPack : BasePackDeed
    {
        [Constructable]
        public BasicMageReagentPack() : base(0x14F0)
        {
            Name = "a basic mage reagent pack";
        }

        // REQUIRED for serialization
        public BasicMageReagentPack(Serial serial) : base(serial)
        {
        }

        protected override string PackName => "Basic Mage Reagent Pack";

        protected override void Fill(Container pack)
        {
            pack.DropItem(new BlackPearl(50));
            pack.DropItem(new Bloodmoss(50));
            pack.DropItem(new Garlic(50));
            pack.DropItem(new Ginseng(50));
            pack.DropItem(new MandrakeRoot(50));
            pack.DropItem(new Nightshade(50));
            pack.DropItem(new SulfurousAsh(50));
            pack.DropItem(new SpidersSilk(50));
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
        }
    }
}
