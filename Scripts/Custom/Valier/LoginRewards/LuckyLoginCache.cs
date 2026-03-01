using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.LoginRewards
{
    /*
     * LuckyLoginCache (optional helper item)
     *
     * Use this as your "chance of rares" mechanic in LoginRewards.cfg:
     *   LoginRewards.Calendar7=...;7=Gold:7000|LuckyLoginCache:1|PowerHourToken:1
     *
     * When double-clicked, it gives a consumable bundle and has chances for extras:
     *  - Always: bandages + recall scrolls + mixed potions
     *  - 20%: extra consumables
     *  - 5%:  PowerHourToken (if the type exists; otherwise skipped)
     *  - 1%:  RareRewardToken (custom token you can later exchange via an NPC)
     */
    public class LuckyLoginCache : Item
    {
        [Constructable]
        public LuckyLoginCache() : base(0xE76)
        {
            Name = "a login reward cache";
            Weight = 1.0;
            LootType = LootType.Regular;
        }

        public LuckyLoginCache(Serial serial) : base(serial) { }

        public override void OnDoubleClick(Mobile from)
        {
            if (from == null || from.Deleted)
                return;

            if (!(from is PlayerMobile pm))
                return;

            if (!IsChildOf(pm.Backpack))
            {
                pm.SendMessage("That must be in your backpack to use it.");
                return;
            }

            // Base bundle (always)
            Give(pm, typeof(Bandage), 50);
            Give(pm, typeof(RecallScroll), 3);

            Give(pm, typeof(GreaterHealPotion), 3);
            Give(pm, typeof(GreaterCurePotion), 3);
            Give(pm, typeof(TotalRefreshPotion), 2);

            // 20% bonus consumables
            if (Utility.RandomDouble() < 0.20)
            {
                Give(pm, typeof(GreaterHealPotion), 4);
                Give(pm, typeof(GreaterCurePotion), 4);
                Give(pm, typeof(RecallScroll), 2);
                pm.SendMessage(0x59, "Bonus supplies found in the cache!");
            }

            // 5%: PowerHourToken (optional, only if script exists)
            if (Utility.RandomDouble() < 0.05)
            {
                if (TryGiveByTypeName(pm, "PowerHourToken", 1) ||
                    TryGiveByTypeName(pm, "Server.Custom.PowerHour.PowerHourToken", 1))
                {
                    pm.SendMessage(0x59, "Lucky! You found a Power Hour token.");
                }
            }

            // 1%: Rare token (custom)
            if (Utility.RandomDouble() < 0.01)
            {
                Give(pm, typeof(RareRewardToken), 1);
                pm.SendMessage(0x35, "Incredible luck! You found a rare reward token!");
            }

            pm.SendMessage(0x59, "You open the cache and collect the contents.");
            Delete();
        }

        private static void Give(PlayerMobile pm, Type itemType, int amount)
        {
            if (pm == null || pm.Deleted || itemType == null || amount < 1)
                return;

            Item item = null;

            try { item = Activator.CreateInstance(itemType) as Item; }
            catch { item = null; }

            if (item == null)
                return;

            if (item.Stackable)
            {
                item.Amount = amount;
                pm.AddToBackpack(item);
                return;
            }

            item.Delete();

            for (int i = 0; i < amount; i++)
            {
                try { item = Activator.CreateInstance(itemType) as Item; }
                catch { item = null; }

                if (item != null)
                    pm.AddToBackpack(item);
            }
        }

        private static bool TryGiveByTypeName(PlayerMobile pm, string typeName, int amount)
        {
            if (pm == null || pm.Deleted || string.IsNullOrEmpty(typeName) || amount < 1)
                return false;

            Type t = TypeCache.Resolve(typeName);

            if (t == null || !typeof(Item).IsAssignableFrom(t))
                return false;

            Give(pm, t, amount);
            return true;
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

        private static class TypeCache
        {
            private static readonly Dictionary<string, Type> _cache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            public static Type Resolve(string name)
            {
                if (string.IsNullOrEmpty(name))
                    return null;

                Type t;
                if (_cache.TryGetValue(name, out t))
                    return t;

                t = FindType(name);
                _cache[name] = t;
                return t;
            }

            private static Type FindType(string name)
            {
                // Try exact first (full name)
                Type t = Type.GetType(name, false, true);
                if (t != null)
                    return t;

                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

                for (int i = 0; i < assemblies.Length; i++)
                {
                    Assembly a = assemblies[i];
                    if (a == null)
                        continue;

                    Type[] types;

                    try { types = a.GetTypes(); }
                    catch { continue; }

                    for (int j = 0; j < types.Length; j++)
                    {
                        Type tt = types[j];
                        if (tt == null)
                            continue;

                        if (tt.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return tt;

                        if (tt.FullName != null && tt.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return tt;
                    }
                }

                return null;
            }
        }
    }

    public class RareRewardToken : Item
    {
        [Constructable]
        public RareRewardToken() : base(0x14F0)
        {
            Name = "a rare reward token";
            Hue = 0x489;
            Weight = 0.0;
            LootType = LootType.Regular;
        }

        public RareRewardToken(Serial serial) : base(serial) { }

        public override void OnDoubleClick(Mobile from)
        {
            from?.SendMessage("This token can be exchanged with a reward vendor (once you set one up).");
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
