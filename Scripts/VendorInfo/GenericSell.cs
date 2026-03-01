using System;
using System.Collections.Generic;
using Server.Items;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Reflection;
using Server.Commands;
using Server;

namespace Server.Mobiles
{
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

        
        // =========================
        // Crafted-only sell boost (config-driven)
        // =========================
        private static int CapSellPrice(int price)
        {
            int cap = CraftedSellBoostConfig.PerItemCap;

            if (cap > 0 && price > cap)
                return cap;

            return price < 0 ? 0 : price;
        }

        private static bool IsPlayerCrafted(Mobile crafter)
        {
            var pm = crafter as PlayerMobile;
            return pm != null && pm.AccessLevel == AccessLevel.Player;
        }

        private static bool IsTrueBoolProperty(object obj, string propName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(propName))
                return false;

            try
            {
                PropertyInfo pi = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);

                if (pi == null || pi.PropertyType != typeof(bool))
                    return false;

                return (bool)pi.GetValue(obj, null);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlayerMade(Item item, Mobile crafter)
        {
            if (IsPlayerCrafted(crafter))
                return true;

            // Fallback for custom crafted items that don't set Crafter consistently.
            // These bool flags are safe because vendor-bought items won't have them set.
            if (IsTrueBoolProperty(item, "PlayerConstructed") ||
                IsTrueBoolProperty(item, "PlayerCrafted") ||
                IsTrueBoolProperty(item, "CraftedByPlayer") ||
                IsTrueBoolProperty(item, "MadeByPlayer") ||
                IsTrueBoolProperty(item, "IsCrafted"))
            {
                return true;
            }

            return false;
        }

        private static bool IsAmmoCommodity(Item item)
        {
            // Keep ammo near-normal; it's too easy to mass-produce and sell
            return item is Arrow || item is Bolt;
        }

        private static int ApplyCraftedMultiplier(Item item, int basePrice)
        {
            if (!CraftedSellBoostConfig.Enabled)
                return basePrice;

            if (basePrice <= 0 || item == null)
                return basePrice;

            if (CraftedSellBoostConfig.IgnoreAmmo && IsAmmoCommodity(item))
                return basePrice;

            // Weapons
            if (item is BaseWeapon weapon)
            {
                if (!IsPlayerMade(weapon, weapon.Crafter))
                    return basePrice;

                double mult = (weapon.Quality == ItemQuality.Exceptional)
                    ? CraftedSellBoostConfig.WeaponExceptionalMult
                    : CraftedSellBoostConfig.WeaponMult;

                int p = (int)Math.Round(basePrice * mult);

                if (weapon.Quality == ItemQuality.Low)
                    p = (int)(p * CraftedSellBoostConfig.LowQualityPenalty);

                return CapSellPrice(p);
            }

            // Armor (includes shields)
            if (item is BaseArmor armor)
            {
                if (!IsPlayerMade(armor, armor.Crafter))
                    return basePrice;

                double mult = (armor.Quality == ItemQuality.Exceptional)
                    ? CraftedSellBoostConfig.ArmorExceptionalMult
                    : CraftedSellBoostConfig.ArmorMult;

                int p = (int)Math.Round(basePrice * mult);

                if (armor.Quality == ItemQuality.Low)
                    p = (int)(p * CraftedSellBoostConfig.LowQualityPenalty);

                return CapSellPrice(p);
            }

            // Clothing (tailoring)
            if (item is BaseClothing clothing)
            {
                if (!IsPlayerMade(clothing, clothing.Crafter))
                    return basePrice;

                double mult = (clothing.Quality == ItemQuality.Exceptional)
                    ? CraftedSellBoostConfig.ClothingExceptionalMult
                    : CraftedSellBoostConfig.ClothingMult;

                int p = (int)Math.Round(basePrice * mult);

                if (clothing.Quality == ItemQuality.Low)
                    p = (int)(p * CraftedSellBoostConfig.LowQualityPenalty);

                return CapSellPrice(p);
            }

            // Jewelry
            if (item is BaseJewel jewel)
            {
                if (!IsPlayerMade(jewel, jewel.Crafter))
                    return basePrice;

                double mult = (jewel.Quality == ItemQuality.Exceptional)
                    ? CraftedSellBoostConfig.JewelryExceptionalMult
                    : CraftedSellBoostConfig.JewelryMult;

                int p = (int)Math.Round(basePrice * mult);

                if (jewel.Quality == ItemQuality.Low)
                    p = (int)(p * CraftedSellBoostConfig.LowQualityPenalty);

                return CapSellPrice(p);
            }

            return basePrice;
        }

        public int GetSellPriceFor(Item item)
        {
            return GetSellPriceFor(item, null);
        }

        public int GetSellPriceFor(Item item, BaseVendor vendor)
        {
            int price = 0;
            m_Table.TryGetValue(item.GetType(), out price);

            // Vendor economy path (if enabled) — keep it, but still allow crafted-only boost.
            if (vendor != null && BaseVendor.UseVendorEconomy)
            {
                IBuyItemInfo buyInfo = vendor.GetBuyInfo()
                    .OfType<GenericBuyInfo>()
                    .FirstOrDefault(info => info.EconomyItem && info.Type == item.GetType());

                if (buyInfo != null)
                {
                    price = (int)((double)buyInfo.Price * 0.75);

                    // Crafted-only boost (safe — requires player-made)
                    price = ApplyCraftedMultiplier(item, price);

                    return Math.Max(1, CapSellPrice(price));
                }
            }

            // Crafted-only multiplier for known craft categories
            if (item is BaseArmor armor)
            {
                int boosted = ApplyCraftedMultiplier(item, price);

                if (boosted != price)
                {
                    price = boosted;
                }
                else
                {
                    if (armor.Quality == ItemQuality.Low)
                        price = (int)(price * 0.60);
                    else if (armor.Quality == ItemQuality.Exceptional)
                        price = (int)(price * 1.25);
                }

                // Keep existing “magic adds” un-multiplied
                price += 100 * (int)armor.Durability;
                price += 100 * (int)armor.ProtectionLevel;

                return Math.Max(1, CapSellPrice(price));
            }
            else if (item is BaseWeapon weapon)
            {
                int boosted = ApplyCraftedMultiplier(item, price);

                if (boosted != price)
                {
                    price = boosted;
                }
                else
                {
                    if (weapon.Quality == ItemQuality.Low)
                        price = (int)(price * 0.60);
                    else if (weapon.Quality == ItemQuality.Exceptional)
                        price = (int)(price * 1.25);
                }

                price += 100 * (int)weapon.DurabilityLevel;
                price += 100 * (int)weapon.DamageLevel;

                return Math.Max(1, CapSellPrice(price));
            }
            else if (item is BaseClothing clothing)
            {
                int boosted = ApplyCraftedMultiplier(item, price);

                if (boosted != price)
                {
                    price = boosted;
                }
                else
                {
                    if (clothing.Quality == ItemQuality.Low)
                        price = (int)(price * 0.60);
                    else if (clothing.Quality == ItemQuality.Exceptional)
                        price = (int)(price * 1.25);
                }

                return Math.Max(1, CapSellPrice(price));
            }
            else if (item is BaseJewel jewel)
            {
                int boosted = ApplyCraftedMultiplier(item, price);

                if (boosted != price)
                {
                    price = boosted;
                }
                else
                {
                    if (jewel.Quality == ItemQuality.Low)
                        price = (int)(price * 0.60);
                    else if (jewel.Quality == ItemQuality.Exceptional)
                        price = (int)(price * 1.25);
                }

                return Math.Max(1, CapSellPrice(price));
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

                return Math.Max(1, CapSellPrice(price));
            }

            // Default: allow crafted multiplier if it applies; otherwise return base table price
            price = ApplyCraftedMultiplier(item, price);

            return Math.Max(1, CapSellPrice(price));
        }


        public int GetBuyPriceFor(Item item)
        {
            return GetBuyPriceFor(item, null);
        }

        public int GetBuyPriceFor(Item item, BaseVendor vendor)
        {
            return (int)(1.90 * GetSellPriceFor(item, vendor));
        }

        public string GetNameFor(Item item)
        {
            if (item.Name != null)
                return item.Name;
            else
                return item.LabelNumber.ToString();
        }

        public bool IsSellable(Item item)
        {
            if (item.QuestItem)
                return false;

            //if ( item.Hue != 0 )
            //return false;

            return IsInList(item.GetType());
        }

        public bool IsResellable(Item item)
        {
            if (item.QuestItem)
                return false;

            //if ( item.Hue != 0 )
            //return false;

            return IsInList(item.GetType());
        }

        public bool IsInList(Type type)
        {
            return m_Table.ContainsKey(type);
        }
    
    // ==========================================================
    // Crafted Sell Boost Config (key=value) + GM reload command
    // File: Config/CraftedSellBoost.cfg
    // ==========================================================
    public static class CraftedSellBoostConfig
    {
        private static readonly object _sync = new object();
        private static Dictionary<string, string> _kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        public static string ConfigFilePath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "CraftedSellBoost.cfg"); }
        }

        public static void Reload()
        {
            lock (_sync)
            {
                _kv.Clear();
                _loaded = true;

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

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            Reload();
        }

        private static string GetString(string key, string def)
        {
            EnsureLoaded();

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

            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;

            return def;
        }

        private static double GetDouble(string key, double def)
        {
            string s = GetString(key, null);

            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d;

            return def;
        }

        // Master switches
        public static bool Enabled { get { return GetBool("CraftedSellBoost.Enabled", true); } }
        public static bool IgnoreAmmo { get { return GetBool("CraftedSellBoost.IgnoreAmmo", true); } }

        // Safety cap (per item)
        public static int PerItemCap { get { return GetInt("CraftedSellBoost.PerItemCap", 200000); } }

        // Low quality penalty applied AFTER multiplier
        public static double LowQualityPenalty { get { return GetDouble("CraftedSellBoost.LowQualityPenalty", 0.60); } }

        // Multipliers
        public static double WeaponMult { get { return GetDouble("CraftedSellBoost.Weapon.Mult", 8.0); } }
        public static double WeaponExceptionalMult { get { return GetDouble("CraftedSellBoost.Weapon.ExceptionalMult", 12.0); } }

        public static double ArmorMult { get { return GetDouble("CraftedSellBoost.Armor.Mult", 7.0); } }
        public static double ArmorExceptionalMult { get { return GetDouble("CraftedSellBoost.Armor.ExceptionalMult", 10.0); } }

        public static double ClothingMult { get { return GetDouble("CraftedSellBoost.Clothing.Mult", 3.0); } }
        public static double ClothingExceptionalMult { get { return GetDouble("CraftedSellBoost.Clothing.ExceptionalMult", 5.0); } }

        public static double JewelryMult { get { return GetDouble("CraftedSellBoost.Jewelry.Mult", 3.0); } }
        public static double JewelryExceptionalMult { get { return GetDouble("CraftedSellBoost.Jewelry.ExceptionalMult", 5.0); } }
    }

    public static class CraftedSellBoostCommands
    {
        public static void Initialize()
        {
            // Load once at startup
            CraftedSellBoostConfig.Reload();

            CommandSystem.Register("ReloadCraftedSellBoost", AccessLevel.GameMaster, e =>
            {
                CraftedSellBoostConfig.Reload();
                e.Mobile.SendMessage(0x59, "Reloaded Config/CraftedSellBoost.cfg");
            });
        }
    }

}
}