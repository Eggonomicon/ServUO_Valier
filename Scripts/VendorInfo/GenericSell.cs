using System;
using System.Collections.Generic;
using System.Linq;
using Server;
using Server.Items;

namespace Server.Mobiles
{
    public class GenericSellInfo : IShopSellInfo
    {
        private readonly Dictionary<Type, int> m_Table = new Dictionary<Type, int>();
        private Type[] m_Types;

        // ---- Crafted-only sell boost tunables (small-pop economy) ----
        private const double WeaponMult = 8.0;
        private const double WeaponExceptionalMult = 12.0;

        private const double ArmorMult = 7.0;
        private const double ArmorExceptionalMult = 10.0;

        private const double ClothingMult = 3.0;
        private const double ClothingExceptionalMult = 5.0;

        private const double JewelryMult = 4.0;
        private const double JewelryExceptionalMult = 6.0;

        // Per-item cap BEFORE any outside stack/amount handling (keeps outliers sane)
        private const int PerItemCap = 200000; // set to 0 to disable

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

        private static int CapSellPrice(int price)
        {
            if (price < 1)
                return 1;

            if (PerItemCap > 0 && price > PerItemCap)
                return PerItemCap;

            return price;
        }

        private static bool IsPlayerCrafted(Mobile crafter)
        {
            var pm = crafter as PlayerMobile;
            return pm != null && pm.AccessLevel == AccessLevel.Player;
        }

        private static bool IsTrueBoolProperty(object obj, string name)
        {
            if (obj == null)
                return false;

            var p = obj.GetType().GetProperty(name);

            if (p != null && p.PropertyType == typeof(bool))
            {
                try
                {
                    return (bool)p.GetValue(obj, null);
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool IsPlayerMade(Item item, Mobile crafter)
        {
            if (IsPlayerCrafted(crafter))
                return true;

            // Fallback for some custom crafted items that forget to set Crafter (e.g. mechanical/clockwork weapons)
            // Only accept this fallback if Crafter is null to avoid boosting GM/NPC-made items.
            if (crafter == null && IsTrueBoolProperty(item, "PlayerConstructed"))
                return true;

            return false;
        }

        private static bool IsAmmoCommodity(Item item)
        {
            // keep commodity ammo near-normal; it's too easy to mass-produce
            return item is Arrow || item is Bolt;
        }

        private static int ApplyCraftedMultiplier(Item item, int basePrice)
        {
            if (basePrice <= 0 || item == null || IsAmmoCommodity(item))
                return basePrice;

            double mult = 1.0;

            // NOTE: We intentionally do NOT rely on ICraftable here.
            // On many ServUO branches ICraftable does not expose Crafter/Quality.
            if (item is BaseWeapon weapon)
            {
                if (!IsPlayerMade(weapon, weapon.Crafter))
                    return basePrice;

                mult = weapon.Quality == ItemQuality.Exceptional ? WeaponExceptionalMult : WeaponMult;

                int p = (int)Math.Round(basePrice * mult);

                if (weapon.Quality == ItemQuality.Low)
                    p = (int)(p * 0.60);

                return CapSellPrice(p);
            }

            if (item is BaseArmor armor)
            {
                if (!IsPlayerMade(armor, armor.Crafter))
                    return basePrice;

                mult = armor.Quality == ItemQuality.Exceptional ? ArmorExceptionalMult : ArmorMult;

                int p = (int)Math.Round(basePrice * mult);

                if (armor.Quality == ItemQuality.Low)
                    p = (int)(p * 0.60);

                return CapSellPrice(p);
            }

            if (item is BaseClothing clothing)
            {
                if (!IsPlayerMade(clothing, clothing.Crafter))
                    return basePrice;

                mult = clothing.Quality == ItemQuality.Exceptional ? ClothingExceptionalMult : ClothingMult;

                int p = (int)Math.Round(basePrice * mult);

                if (clothing.Quality == ItemQuality.Low)
                    p = (int)(p * 0.60);

                return CapSellPrice(p);
            }

            if (item is BaseJewel jewel)
            {
                if (!IsPlayerMade(jewel, jewel.Crafter))
                    return basePrice;

                mult = jewel.Quality == ItemQuality.Exceptional ? JewelryExceptionalMult : JewelryMult;

                int p = (int)Math.Round(basePrice * mult);

                if (jewel.Quality == ItemQuality.Low)
                    p = (int)(p * 0.60);

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

                    // Crafted-only boost (safe — requires player crafter)
                    price = ApplyCraftedMultiplier(item, price);

                    return CapSellPrice(price);
                }
            }

            // BaseVendor price table path (standard)
            if (item is BaseArmor armor)
            {
                // Crafted-only override
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

                return CapSellPrice(price);
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

                return CapSellPrice(price);
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

                return CapSellPrice(price);
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

                return CapSellPrice(price);
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

                return CapSellPrice(price);
            }

            return CapSellPrice(price);
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

            return item.LabelNumber.ToString();
        }

        public bool IsSellable(Item item)
        {
            if (item.QuestItem)
                return false;

            return IsInList(item.GetType());
        }

        public bool IsResellable(Item item)
        {
            if (item.QuestItem)
                return false;

            return IsInList(item.GetType());
        }

        public bool IsInList(Type type)
        {
            return m_Table.ContainsKey(type);
        }
    }
}
