using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Server;
using Server.Commands;
using Server.Items;

namespace Server.Mobiles
{
    /*
     * ResourceBroker (Config-driven + Hot Reload) — ServUO 57.x friendly
     *
     * What it does:
     *  - Vendor SELLS common resources to players (at a premium)
     *  - Vendor BUYS common resources from players (baseline income)
     *
     * Hot reload:
     *  - Command: [ReloadResourceBrokers
     *    Reloads Config/ResourceBroker.cfg and refreshes ALL existing ResourceBroker NPCs.
     *
     * Config philosophy:
     *  - If a price is <= 0, that entry is disabled (not added to the vendor).
     *  - If a stock is <= 0 (sell list), that entry is disabled.
     *
     * ----------------------------
     * CONFIG FILE
     * ----------------------------
     * This script reads:
     *   /Config/ResourceBroker.cfg
     *
     * If the file is missing, defaults are used.
     *
     * Example keys:
     *
     * ResourceBroker.Enabled=true
     *
     * # Vendor SELLS to players (premium)
     * ResourceBroker.Sell.IronIngot.Price=6
     * ResourceBroker.Sell.IronIngot.Stock=999
     *
     * # Vendor BUYS from players (baseline)
     * ResourceBroker.Buy.IronIngot.Price=3
     *
     * Notes:
     *  - Restart is NOT required if you use [ReloadResourceBrokers
     *  - If you have the buy/sell gump open during reload, close/reopen it.
     */

    public static class ResourceBrokerSystem
    {
        public static void Initialize()
        {
            // Load config at startup
            ResourceBrokerConfig.Reload();

            // GM command to hot reload
            CommandSystem.Register("ReloadResourceBrokers", AccessLevel.GameMaster, OnReload);
        }

        private static void OnReload(CommandEventArgs e)
        {
            Mobile from = e.Mobile;

            ResourceBrokerConfig.Reload();

            int refreshed = 0;

            // BaseVendor keeps a list of all vendors
            foreach (BaseVendor v in BaseVendor.AllVendors)
            {
                if (v is ResourceBroker rb && !rb.Deleted)
                {
                    rb.ReloadBroker();
                    refreshed++;
                }
            }

            from.SendMessage(0x59, "ResourceBroker: reloaded config and refreshed {0} broker(s).", refreshed);
        }
    }

    public static class ResourceBrokerConfig
    {
        private static readonly object _sync = new object();
        private static Dictionary<string, string> _kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Change this if you want a different filename:
        public static string ConfigFilePath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "ResourceBroker.cfg"); }
        }

        public static void Reload()
        {
            lock (_sync)
            {
                _kv.Clear();

                try
                {
                    string path = ConfigFilePath;

                    if (!File.Exists(path))
                        return;

                    string[] lines = File.ReadAllLines(path);

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];

                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        line = line.Trim();

                        // comments
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
                    // If config read fails, fall back to defaults silently.
                    _kv.Clear();
                }
            }
        }

        private static string GetString(string key, string def)
        {
            lock (_sync)
            {
                if (_kv.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                    return v;

                return def;
            }
        }

        private static int GetInt(string key, int def)
        {
            string s = GetString(key, null);

            if (string.IsNullOrWhiteSpace(s))
                return def;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n;

            return def;
        }

        private static bool GetBool(string key, bool def)
        {
            string s = GetString(key, null);

            if (string.IsNullOrWhiteSpace(s))
                return def;

            if (bool.TryParse(s, out var b))
                return b;

            // allow 0/1
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n != 0;

            return def;
        }

        public static bool Enabled
        {
            get { return GetBool("ResourceBroker.Enabled", true); }
        }

        // SELL (vendor -> player)
        public static int SellPrice(string itemKey, int def) { return GetInt("ResourceBroker.Sell." + itemKey + ".Price", def); }
        public static int SellStock(string itemKey, int def) { return GetInt("ResourceBroker.Sell." + itemKey + ".Stock", def); }

        // BUY (player -> vendor)
        public static int BuyPrice(string itemKey, int def) { return GetInt("ResourceBroker.Buy." + itemKey + ".Price", def); }
    }

    public class ResourceBroker : BaseVendor
    {
        private readonly List<SBInfo> m_SBInfos = new List<SBInfo>();

        [Constructable]
        public ResourceBroker() : base("the resource broker")
        {
        }

        protected override List<SBInfo> SBInfos { get { return m_SBInfos; } }

        public override void InitSBInfo()
        {
            m_SBInfos.Add(new SBResourceBroker());
        }

        /// <summary>
        /// Refresh this broker's buy/sell lists from current config values.
        /// </summary>
        public void ReloadBroker()
        {
            // BaseVendor.LoadSBInfo clears SBInfos and rebuilds internal lists.
            LoadSBInfo();
        }

        public ResourceBroker(Serial serial) : base(serial)
        {
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

    public class SBResourceBroker : SBInfo
    {
        private readonly IShopSellInfo m_SellInfo = new InternalSellInfo();
        private readonly List<GenericBuyInfo> m_BuyInfo = new InternalBuyInfo();

        public override IShopSellInfo SellInfo { get { return m_SellInfo; } }
        public override List<GenericBuyInfo> BuyInfo { get { return m_BuyInfo; } }

        private static bool Enabled()
        {
            return ResourceBrokerConfig.Enabled;
        }

        public class InternalBuyInfo : List<GenericBuyInfo>
        {
            public InternalBuyInfo()
            {
                if (!Enabled())
                    return;

                // Vendor SELLS to players (premium/convenience).
                // If price <= 0 or stock <= 0 => entry disabled.

                AddSellEntry(typeof(IronIngot), "IronIngot", 0x1BF2, 0);
                AddSellEntry(typeof(Board), "Board", 0x1BD7, 0);
                AddSellEntry(typeof(Leather), "Leather", 0x1081, 0);

                // Optional convenience resources:
                AddSellEntry(typeof(Cloth), "Cloth", 0x1766, 0);
                AddSellEntry(typeof(Log), "Log", 0x1BDD, 0);
            }

            private void AddSellEntry(Type type, string key, int itemID, int hue)
            {
                int price = ResourceBrokerConfig.SellPrice(key, DefaultSellPrice(key));
                int stock = ResourceBrokerConfig.SellStock(key, 999);

                if (price <= 0 || stock <= 0)
                    return;

                Add(new GenericBuyInfo(type, price, stock, itemID, hue));
            }

            private static int DefaultSellPrice(string key)
            {
                // Reasonable defaults (premium vs buyback)
                switch (key)
                {
                    case "IronIngot": return 6;
                    case "Board": return 6;
                    case "Leather": return 9;
                    case "Cloth": return 8;
                    case "Log": return 8;
                    default: return 0;
                }
            }
        }

        public class InternalSellInfo : GenericSellInfo
        {
            public InternalSellInfo()
            {
                if (!Enabled())
                    return;

                // Vendor BUYS from players (baseline income).
                // If price <= 0 => entry disabled.

                AddBuybackEntry(typeof(IronIngot), "IronIngot");
                AddBuybackEntry(typeof(Board), "Board");
                AddBuybackEntry(typeof(Leather), "Leather");

                // Optional:
                AddBuybackEntry(typeof(Cloth), "Cloth");
                AddBuybackEntry(typeof(Log), "Log");
            }

            private void AddBuybackEntry(Type type, string key)
            {
                int price = ResourceBrokerConfig.BuyPrice(key, DefaultBuyPrice(key));

                if (price <= 0)
                    return;

                Add(type, price);
            }

            private static int DefaultBuyPrice(string key)
            {
                switch (key)
                {
                    case "IronIngot": return 3;
                    case "Board": return 2;
                    case "Leather": return 3;
                    case "Cloth": return 2;
                    case "Log": return 2;
                    default: return 0;
                }
            }
        }
    }
}
