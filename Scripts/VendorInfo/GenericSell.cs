using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using Server;
using Server.Commands;
using Server.Items;

namespace Server.Mobiles
{
    /*
     * GenericSellInfo (ServUO 57.x)
     *
     * This version adds CONFIG-DRIVEN crafted sell multipliers (no "min sell" feature).
     * - Reads: Config/CraftedSellBoost.cfg (key=value)
     * - GM command: [ReloadCraftedSellBoost
     * - Boosts ONLY player-crafted items (to prevent NPC buy->sell loops)
     *
     * Categories:
     * - Weapon (BaseWeapon)      CraftedSellBoost.Weapon.Mult / ExceptionalMult
     * - Ranged (BaseRanged)     CraftedSellBoost.Ranged.Mult / ExceptionalMult  (bows/crossbows/mechanical xbows)
     * - Armor  (BaseArmor)      CraftedSellBoost.Armor.Mult / ExceptionalMult
     * - Clothing (BaseClothing) CraftedSellBoost.Clothing.Mult / ExceptionalMult (cloaks/robes/etc.)
     * - Jewelry (BaseJewel)     CraftedSellBoost.Jewelry.Mult / ExceptionalMult
     *
     * Important:
     * - The multiplier is applied to the BASE price only.
     * - Magic add-ons (durability/damage/protection levels) are added AFTER and are not multiplied.
     */

    public static class CraftedSellBoostConfig
    {
        private static readonly object _sync = new object();
        private static readonly Dictionary<string, string> _kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ConfigFilePath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "CraftedSellBoost.cfg"); }
        }

        public static void Reload()
        {
            lock (_sync)
            {
                _kv.Clear();

                try
                {
                    if (!File.Exists(ConfigFilePath))
                        return;

                    string[] raw = File.ReadAllLines(ConfigFilePath);

                    for (int i = 0; i < raw.Length; i++)
                    {
                        string line = raw[i];

                        if (line == null)
                            continue;

                        line = line.Trim();

                        if (line.Length == 0)
                            continue;

                        if (line.StartsWith("#") || line.StartsWith("//"))
                            continue;

                        int eq = line.IndexOf('=');

                        if (eq <= 0)
                            continue;

                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();

                        if (key.Length == 0)
                            continue;

                        _kv[key] = val;
                    }
                }
                catch
                {
                    _kv.Clear();
                }
            }
        }

        private static string GetString(string key, string def)
        {
            lock (_sync)
            {
                string v;
                if (_kv.TryGetValue(key, out v) && !string.IsNullOrWhiteSpace(v))
                    return v;

                return def;
            }
        }

        private static bool GetBool(string key, bool def)
        {
            string s = GetString(key, null);

            if (string.IsNullOrWhiteSpace(s))
                return def;

            bool b;
            if (bool.TryParse(s, out b))
                return b;

            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n != 0;

            return def;
        }

        private static int GetInt(string key, int def)
        {
            string s = GetString(key, null);

            if (string.IsNullOrWhiteSpace(s))
                return def;

            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;

            return def;
        }

        private static double GetDouble(string key, double def)
        {
            string s = GetString(key, null);

            if (string.IsNullOrWhiteSpace(s))
                return def;

            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d;

            return def;
        }

        public static bool Enabled { get { return GetBool("CraftedSellBoost.Enabled", true); } }

        // Optional absolute cap on final price for crafted items (0 disables)
        public static int Cap { get { return GetInt("CraftedSellBoost.Cap", 0); } }

        public static double WeaponMult { get { return GetDouble("CraftedSellBoost.Weapon.Mult", 8.0); } }
        public static double WeaponExceptionalMult { get { return GetDouble("CraftedSellBoost.Weapon.ExceptionalMult", 12.0); } }

        public static double RangedMult { get { return GetDouble("CraftedSellBoost.Ranged.Mult", 8.0); } }
        public static double RangedExceptionalMult { get { return GetDouble("CraftedSellBoost.Ranged.ExceptionalMult", 12.0); } }

        public static double ArmorMult { get { return GetDouble("CraftedSellBoost.Armor.Mult", 7.0); } }
        public static double ArmorExceptionalMult { get { return GetDouble("CraftedSellBoost.Armor.ExceptionalMult", 10.0); } }

        public static double ClothingMult { get { return GetDouble("CraftedSellBoost.Clothing.Mult", 3.0); } }
        public static double ClothingExceptionalMult { get { return GetDouble("CraftedSellBoost.Clothing.ExceptionalMult", 5.0); } }

        public static double JewelryMult { get { return GetDouble("CraftedSellBoost.Jewelry.Mult", 3.0); } }
        public static double JewelryExceptionalMult { get { return GetDouble("CraftedSellBoost.Jewelry.ExceptionalMult", 5.0); } }

        public static int ApplyCap(int value)
        {
            int cap = Cap;

            if (cap > 0 && value > cap)
                return cap;

            return value;
        }
    }

    public static class CraftedSellBoostCommands
    {
        public static void Initialize()
        {
            CraftedSellBoostConfig.Reload();

            CommandSystem.Register("ReloadCraftedSellBoost", AccessLevel.GameMaster, OnReload);
        }

        private static void OnReload(CommandEventArgs e)
        {
            CraftedSellBoostConfig.Reload();
            e.Mobile.SendMessage(0x59, "CraftedSellBoost: reloaded Config/CraftedSellBoost.cfg");
        }
    }

    public class GenericSellInfo : IShopSellInfo
    {
        private readonly Dictionary<Type, int> m_Table = new Dictionary<Type, int>();
        private Type[] m_Types;

        public GenericSellInfo()
        {
        }

        public Type[] Types
        {
            get
            {
                if (m_Types == null)
                {
                    m_Types = new Type[m_Table.Keys.Count];
                    m_Table.Keys.CopyTo(m_Types, 0);
                }

                return m_Types;
            }
        }

        public void Add(Type type, int price)
        {
            m_Table[type] = price;
            m_Types = null;
        }

        public int GetSellPriceFor(Item item)
        {
            return GetSellPriceFor(item, null);
        }

        public int GetSellPriceFor(Item item, BaseVendor vendor)
        {
            int price = 0;
            m_Table.TryGetValue(item.GetType(), out price);

            // Vendor economy baseline (do NOT early return; crafted boosts may still apply)
            if (vendor != null && BaseVendor.UseVendorEconomy)
            {
                IBuyItemInfo buyInfo = vendor.GetBuyInfo()
                    .OfType<GenericBuyInfo>()
                    .FirstOrDefault(info => info.EconomyItem && info.Type == item.GetType());

                if (buyInfo != null)
                {
                    price = Math.Max(1, (int)((double)buyInfo.Price * .75));
                }
            }

            // ===== Normal ServUO adjustments (base price modifiers) =====
            // We'll keep these as "base" for multipliers, but we will not multiply the magic add-ons below.

            int magicAdds = 0;

            if (item is BaseArmor)
            {
                BaseArmor armor = (BaseArmor)item;

                if (armor.Quality == ItemQuality.Low)
                    price = (int)(price * 0.60);
                else if (armor.Quality == ItemQuality.Exceptional)
                    price = (int)(price * 1.25);

                magicAdds += 100 * (int)armor.Durability;
                magicAdds += 100 * (int)armor.ProtectionLevel;
            }
            else if (item is BaseWeapon)
            {
                BaseWeapon weapon = (BaseWeapon)item;

                if (weapon.Quality == ItemQuality.Low)
                    price = (int)(price * 0.60);
                else if (weapon.Quality == ItemQuality.Exceptional)
                    price = (int)(price * 1.25);

                magicAdds += 100 * (int)weapon.DurabilityLevel;
                magicAdds += 100 * (int)weapon.DamageLevel;
            }
            else if (item is BaseClothing)
            {
                BaseClothing clothing = (BaseClothing)item;

                if (clothing.Quality == ItemQuality.Low)
                    price = (int)(price * 0.60);
                else if (clothing.Quality == ItemQuality.Exceptional)
                    price = (int)(price * 1.25);
            }
            else if (item is BaseJewel)
            {
                BaseJewel jewel = (BaseJewel)item;

                if (jewel.Quality == ItemQuality.Low)
                    price = (int)(price * 0.60);
                else if (jewel.Quality == ItemQuality.Exceptional)
                    price = (int)(price * 1.25);
            }
            else if (item is BaseBeverage)
            {
                int price1 = price, price2 = price;

                if (item is Pitcher)
                {
                    price1 = 3;
                    price2 = 5;
                }
                else if (item is BeverageBottle)
                {
                    price1 = 3;
                    price2 = 3;
                }
                else if (item is Jug)
                {
                    price1 = 6;
                    price2 = 6;
                }

                BaseBeverage bev = (BaseBeverage)item;

                if (bev.IsEmpty || bev.Content == BeverageType.Milk)
                    price = price1;
                else
                    price = price2;
            }

            if (price < 1)
                price = 1;

            // ===== Crafted-only sell boost (config-driven) =====
            if (CraftedSellBoostConfig.Enabled && IsPlayerCrafted(item))
            {
                bool exceptional = IsExceptional(item);

                double mult = 1.0;

                // Ranged (bows/crossbows/etc.) get their own multipliers
                BaseWeapon w = item as BaseWeapon;
                if (w != null && w is BaseRanged)
                    mult = exceptional ? CraftedSellBoostConfig.RangedExceptionalMult : CraftedSellBoostConfig.RangedMult;
                else if (item is BaseWeapon)
                    mult = exceptional ? CraftedSellBoostConfig.WeaponExceptionalMult : CraftedSellBoostConfig.WeaponMult;
                else if (item is BaseArmor)
                    mult = exceptional ? CraftedSellBoostConfig.ArmorExceptionalMult : CraftedSellBoostConfig.ArmorMult;
                else if (item is BaseClothing)
                    mult = exceptional ? CraftedSellBoostConfig.ClothingExceptionalMult : CraftedSellBoostConfig.ClothingMult;
                else if (item is BaseJewel)
                    mult = exceptional ? CraftedSellBoostConfig.JewelryExceptionalMult : CraftedSellBoostConfig.JewelryMult;

                if (mult > 1.0)
                    price = Math.Max(1, (int)Math.Floor(price * mult));
            }

            // Add magic add-ons after crafted multiplier (not multiplied)
            price += magicAdds;

            // Optional cap for crafted items
            if (CraftedSellBoostConfig.Enabled && IsPlayerCrafted(item))
                price = CraftedSellBoostConfig.ApplyCap(price);

            if (price < 1)
                price = 1;

            return price;
        }

        public int GetBuyPriceFor(Item item)
        {
            return GetBuyPriceFor(item, null);
        }

        public int GetBuyPriceFor(Item item, BaseVendor vendor)
        {
            int price = 0;
            m_Table.TryGetValue(item.GetType(), out price);

            if (vendor != null && BaseVendor.UseVendorEconomy)
            {
                IBuyItemInfo buyInfo = vendor.GetBuyInfo()
                    .OfType<GenericBuyInfo>()
                    .FirstOrDefault(info => info.EconomyItem && info.Type == item.GetType());

                if (buyInfo != null)
                {
                    // OSI-ish buy pricing (what players pay vendors)
                    price = buyInfo.Price;
                    return Math.Max(1, price);
                }
            }

            return Math.Max(1, price);
        }

        // =========================
        // Crafted detection helpers
        // =========================
        private static readonly string[] BoolCraftFlags = new[]
        {
            "PlayerConstructed",
            "PlayerCrafted",
            "Crafted",
            "MadeByPlayer"
        };

        private static bool IsPlayerCrafted(Item item)
        {
            if (item == null || item.Deleted)
                return false;

            // 1) Crafter property (Mobile) if present and non-null
            object crafter = TryGetPropertyValue(item, "Crafter");
            if (crafter != null)
                return true;

            // 2) Known boolean flags (reflective)
            for (int i = 0; i < BoolCraftFlags.Length; i++)
            {
                object v = TryGetPropertyValue(item, BoolCraftFlags[i]);

                if (v is bool b && b)
                    return true;
            }

            return false;
        }

        private static bool IsExceptional(Item item)
        {
            if (item == null)
                return false;

            // Preferred: ItemQuality enum
            object q = TryGetPropertyValue(item, "Quality");
            if (q != null)
            {
                // Compare by name to avoid enum-value mismatch between shards
                if (q.ToString().Equals("Exceptional", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Fallback: Exceptional bool
            object ex = TryGetPropertyValue(item, "Exceptional");
            if (ex is bool b)
                return b;

            return false;
        }

        private static object TryGetPropertyValue(object obj, string propName)
        {
            try
            {
                Type t = obj.GetType();
                PropertyInfo p = t.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (p == null)
                    return null;

                return p.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }
    }
}
