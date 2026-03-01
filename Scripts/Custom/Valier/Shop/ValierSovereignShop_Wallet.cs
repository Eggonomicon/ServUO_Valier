using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Server;
using Server.Commands;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Custom.ValierCurrency;

namespace Server.Custom.ValierShop
{
    /*
     * Valier Sovereign-Style Shop (Wallet currency)
     *
     * - Currency: account wallet coins via ValierWalletSystem
     * - UI: Gump (categories + item list + details + buy)
     * - Config: reads Config/ValierShop.shop directly
     *
     * Commands:
     *  - Player: [ValierShop
     *  - GM: [ValierShopReload
     *  - GM: [ValierShopInfo
     *
     * Add to world:
     *  [add ValierShopStone
     *  [add ValierShopkeeper
     *
     * Config format:
     *  category|Crafting Packs
     *  item|DisplayName|TypeName|Amount|Price|Hue|Description
     */

    public static class ValierShopConfig
    {
        private static readonly object _sync = new object();

        public static string ConfigFilePath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "ValierShop.shop"); }
        }

        public static readonly List<ShopCategory> Categories = new List<ShopCategory>();

        public static void Reload()
        {
            lock (_sync)
            {
                Categories.Clear();

                if (!File.Exists(ConfigFilePath))
                {
                    Categories.Add(new ShopCategory("Shop"));
                    return;
                }

                ShopCategory current = null;

                string[] rawLines = File.ReadAllLines(ConfigFilePath);

                // Support "\" continuation
                List<string> lines = new List<string>();

                for (int i = 0; i < rawLines.Length; i++)
                {
                    string line = rawLines[i];

                    if (line == null)
                        continue;

                    line = line.Trim();

                    if (line.Length == 0)
                        continue;

                    if (line.StartsWith("#") || line.StartsWith("//"))
                        continue;

                    while (line.EndsWith("\\") && i + 1 < rawLines.Length)
                    {
                        line = line.Substring(0, line.Length - 1);
                        string next = rawLines[++i];

                        if (next == null)
                            break;

                        next = next.Trim();

                        if (next.StartsWith("#") || next.StartsWith("//"))
                            continue;

                        line += next;
                    }

                    lines.Add(line);
                }

                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i];

                    if (line.StartsWith("category|", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = SafePart(line, 1);
                        if (string.IsNullOrWhiteSpace(name))
                            name = "Shop";

                        current = new ShopCategory(name);
                        Categories.Add(current);
                        continue;
                    }

                    if (line.StartsWith("item|", StringComparison.OrdinalIgnoreCase))
                    {
                        if (current == null)
                        {
                            current = new ShopCategory("Shop");
                            Categories.Add(current);
                        }

                        string display = SafePart(line, 1);
                        string typeName = SafePart(line, 2);

                        int amount = ParseInt(SafePart(line, 3), 1);
                        int price = ParseInt(SafePart(line, 4), 0);
                        int hue = ParseInt(SafePart(line, 5), 0);
                        string desc = SafePart(line, 6);

                        if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(typeName) || price <= 0)
                            continue;

                        current.Items.Add(new ShopItemEntry
                        {
                            DisplayName = display,
                            TypeName = typeName,
                            Amount = Math.Max(1, amount),
                            Price = price,
                            Hue = hue,
                            Description = desc ?? ""
                        });
                    }
                }

                if (Categories.Count == 0)
                    Categories.Add(new ShopCategory("Shop"));
            }
        }

        private static string SafePart(string line, int index)
        {
            string[] parts = line.Split(new[] { '|' }, StringSplitOptions.None);

            if (index < 0 || index >= parts.Length)
                return "";

            return (parts[index] ?? "").Trim();
        }

        private static int ParseInt(string s, int def)
        {
            if (string.IsNullOrWhiteSpace(s))
                return def;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n;

            return def;
        }
    }

    public class ShopCategory
    {
        public string Name;
        public List<ShopItemEntry> Items = new List<ShopItemEntry>();

        public ShopCategory(string name) { Name = name ?? "Shop"; }
    }

    public class ShopItemEntry
    {
        public string DisplayName;
        public string TypeName;
        public int Amount;
        public int Price;
        public int Hue;
        public string Description;
    }

    public class ValierShopStone : Item
    {
        [Constructable]
        public ValierShopStone() : base(0xEDC)
        {
            Name = "Valier Shop";
            Movable = false;
        }

        public ValierShopStone(Serial serial) : base(serial) { }

        public override void OnDoubleClick(Mobile from)
        {
            if (from is PlayerMobile pm)
                pm.SendGump(new ValierShopGump(pm, 0, 0, null));
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    public class ValierShopkeeper : BaseVendor
    {
        private readonly List<SBInfo> m_SBInfos = new List<SBInfo>();

        [Constructable]
        public ValierShopkeeper() : base("the shopkeeper")
        {
            Name = "Valier Shopkeeper";
        }

        protected override List<SBInfo> SBInfos { get { return m_SBInfos; } }

        public override void InitSBInfo() { /* no traditional buy/sell */ }

        public override void OnDoubleClick(Mobile from)
        {
            if (from is PlayerMobile pm)
                pm.SendGump(new ValierShopGump(pm, 0, 0, null));
        }

        public ValierShopkeeper(Serial serial) : base(serial) { }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    public static class ValierShopSystem
    {
        public static void Initialize()
        {
            ValierShopConfig.Reload();

            CommandSystem.Register("ValierShopReload", AccessLevel.GameMaster, e =>
            {
                ValierShopConfig.Reload();
                e.Mobile.SendMessage(0x59, "ValierShop: reloaded Config/ValierShop.shop");
            });

            CommandSystem.Register("ValierShopInfo", AccessLevel.GameMaster, e =>
            {
                e.Mobile.SendMessage(0x59, "ValierShop cfg: {0}", ValierShopConfig.ConfigFilePath);
                e.Mobile.SendMessage(0x59, "Categories loaded: {0}", ValierShopConfig.Categories.Count);

                int items = 0;
                foreach (var c in ValierShopConfig.Categories)
                    items += (c.Items != null ? c.Items.Count : 0);

                e.Mobile.SendMessage(0x59, "Items loaded: {0}", items);
            });

            CommandSystem.Register("ValierShop", AccessLevel.Player, e =>
            {
                if (e.Mobile is PlayerMobile pm)
                    pm.SendGump(new ValierShopGump(pm, 0, 0, null));
            });
        }
    }

    public class ValierShopGump : Gump
    {
        private const int W = 700, H = 460;

        private readonly PlayerMobile _pm;
        private readonly int _catIndex, _page;
        private readonly ShopItemEntry _selected;

        public ValierShopGump(PlayerMobile pm, int catIndex, int page, ShopItemEntry selected) : base(40, 40)
        {
            _pm = pm;
            _catIndex = Math.Max(0, catIndex);
            _page = Math.Max(0, page);
            _selected = selected;

            Closable = true; Disposable = true; Dragable = true; Resizable = false;

            AddPage(0);
            AddBackground(0, 0, W, H, 9270);
            AddAlphaRegion(10, 10, W - 20, H - 20);

            AddHtml(20, 18, W - 40, 22, Color("<CENTER><B>Valier Shop</B></CENTER>", 0xFFFFFF), false, false);

            int balance = ValierWalletSystem.GetBalance(pm);
            AddHtml(20, 44, W - 40, 18, Color(string.Format("Valier Coins (Wallet): <B>{0}</B>", balance), 0xDDDDDD), false, false);

            DrawCategories();
            DrawItems();
            DrawDetails();
        }

        private void DrawCategories()
        {
            int x = 20, y = 70, w = 170;

            AddHtml(x, y, w, 18, Color("<B>Categories</B>", 0xFFFFFF), false, false);
            y += 22;

            var cats = ValierShopConfig.Categories;
            if (cats == null || cats.Count == 0)
            {
                AddHtml(x, y, w, 18, Color("No categories configured.", 0xAAAAAA), false, false);
                return;
            }

            for (int i = 0; i < cats.Count && i < 14; i++)
            {
                int btn = 1000 + i;
                string name = cats[i].Name ?? "Shop";

                bool selected = (i == _catIndex);
                int hue = selected ? 0x59 : 0x34;

                AddButton(x, y, 4005, 4007, btn, GumpButtonType.Reply, 0);
                AddLabel(x + 35, y + 2, hue, Trunc(name, 18));

                y += 22;
            }
        }

        private void DrawItems()
        {
            int x = 210, y = 70, w = 300;

            AddHtml(x, y, w, 18, Color("<B>Items</B>", 0xFFFFFF), false, false);
            y += 22;

            var cat = GetCategory(_catIndex);
            if (cat == null || cat.Items == null || cat.Items.Count == 0)
            {
                AddHtml(x, y, w, 18, Color("No items in this category.", 0xAAAAAA), false, false);
                return;
            }

            const int perPage = 12;
            int start = _page * perPage;
            int end = Math.Min(cat.Items.Count, start + perPage);

            for (int i = start; i < end; i++)
            {
                var entry = cat.Items[i];

                AddButton(x, y, 4005, 4007, 2000 + i, GumpButtonType.Reply, 0);
                AddLabel(x + 35, y + 2, 0x34, Trunc(string.Format("{0}  ({1}c)", entry.DisplayName, entry.Price), 34));
                y += 22;
            }

            if (cat.Items.Count > perPage)
            {
                int totalPages = (cat.Items.Count + perPage - 1) / perPage;
                AddHtml(x, 410, w, 18, Color(string.Format("Page {0}/{1}", _page + 1, totalPages), 0xBBBBBB), false, false);

                if (_page > 0) AddButton(x + 170, 408, 4014, 4016, 3001, GumpButtonType.Reply, 0);
                if (_page < totalPages - 1) AddButton(x + 220, 408, 4005, 4007, 3002, GumpButtonType.Reply, 0);
            }
        }

        private void DrawDetails()
        {
            int x = 525, y = 70, w = 155;

            AddHtml(x, y, w, 18, Color("<B>Details</B>", 0xFFFFFF), false, false);
            y += 22;

            if (_selected == null)
            {
                AddHtml(x, y, w, 60, Color("Select an item to see details.", 0xAAAAAA), false, false);
                return;
            }

            AddHtml(x, y, w, 18, Color(string.Format("<B>{0}</B>", Escape(_selected.DisplayName)), 0xDDDDDD), false, false); y += 20;
            AddHtml(x, y, w, 18, Color(string.Format("Cost: <B>{0}</B>", _selected.Price), 0xDDDDDD), false, false); y += 20;
            AddHtml(x, y, w, 18, Color(string.Format("Amount: <B>{0}</B>", _selected.Amount), 0xDDDDDD), false, false); y += 20;

            string desc = string.IsNullOrWhiteSpace(_selected.Description) ? "—" : Escape(_selected.Description);
            AddHtml(x, y, w, 120, Color(desc, 0xBBBBBB), false, true); y += 130;

            AddButton(x, y, 4005, 4007, 4001, GumpButtonType.Reply, 0);
            AddLabel(x + 35, y + 2, 0x59, "Buy");
        }

        public override void OnResponse(NetState sender, RelayInfo info)
        {
            if (_pm == null || _pm.Deleted) return;

            int id = info.ButtonID;

            if (id >= 1000 && id < 2000)
            {
                _pm.SendGump(new ValierShopGump(_pm, id - 1000, 0, null));
                return;
            }

            if (id >= 2000 && id < 3000)
            {
                var cat = GetCategory(_catIndex);
                int itemIndex = id - 2000;

                if (cat != null && cat.Items != null && itemIndex >= 0 && itemIndex < cat.Items.Count)
                    _pm.SendGump(new ValierShopGump(_pm, _catIndex, _page, cat.Items[itemIndex]));

                return;
            }

            if (id == 3001) { _pm.SendGump(new ValierShopGump(_pm, _catIndex, Math.Max(0, _page - 1), _selected)); return; }
            if (id == 3002) { _pm.SendGump(new ValierShopGump(_pm, _catIndex, _page + 1, _selected)); return; }

            if (id == 4001)
            {
                if (_selected == null) { _pm.SendMessage("Select an item first."); return; }
                TryPurchase(_pm, _selected);
                _pm.SendGump(new ValierShopGump(_pm, _catIndex, _page, _selected));
            }
        }

        private static void TryPurchase(PlayerMobile pm, ShopItemEntry entry)
        {
            if (pm == null || pm.Deleted || entry == null)
                return;

            int price = entry.Price;

            if (price <= 0)
            {
                pm.SendMessage("That item is not purchasable.");
                return;
            }

            if (!ValierWalletSystem.Consume(pm, price))
            {
                pm.SendMessage(0x22, "You don't have enough Valier coins.");
                return;
            }

            Type t = TypeCache.Resolve(entry.TypeName);

            if (t == null || !typeof(Item).IsAssignableFrom(t))
            {
                pm.SendMessage(0x22, "Item type not found: {0}", entry.TypeName);
                ValierWalletSystem.Add(pm, price); // refund
                return;
            }

            int amount = Math.Max(1, entry.Amount);

            Item created = null;
            try { created = Activator.CreateInstance(t) as Item; }
            catch { created = null; }

            if (created == null)
            {
                pm.SendMessage(0x22, "Could not create item: {0}", entry.TypeName);
                ValierWalletSystem.Add(pm, price); // refund
                return;
            }

            if (entry.Hue != 0)
                created.Hue = entry.Hue;

            if (created.Stackable)
            {
                created.Amount = amount;

                if (!TryDrop(pm, created))
                {
                    created.Delete();
                    ValierWalletSystem.Add(pm, price); // refund
                    pm.SendMessage(0x22, "You don't have room for that.");
                    return;
                }
            }
            else
            {
                if (!TryDrop(pm, created))
                {
                    created.Delete();
                    ValierWalletSystem.Add(pm, price); // refund
                    pm.SendMessage(0x22, "You don't have room for that.");
                    return;
                }

                for (int i = 1; i < amount; i++)
                {
                    Item item = null;
                    try { item = Activator.CreateInstance(t) as Item; }
                    catch { item = null; }

                    if (item == null) continue;
                    if (entry.Hue != 0) item.Hue = entry.Hue;

                    if (!TryDrop(pm, item))
                        pm.BankBox?.DropItem(item);
                }
            }

            pm.SendMessage(0x59, "Purchase complete!");
        }

        private static bool TryDrop(PlayerMobile pm, Item item)
        {
            if (pm == null || pm.Deleted || item == null)
                return false;

            if (pm.Backpack != null && pm.Backpack.TryDropItem(pm, item, false))
                return true;

            if (pm.BankBox != null)
            {
                pm.BankBox.DropItem(item);
                pm.SendMessage(0x59, "Your purchase was placed in your bank box.");
                return true;
            }

            return false;
        }

        private ShopCategory GetCategory(int idx)
        {
            var cats = ValierShopConfig.Categories;

            if (cats == null || cats.Count == 0)
                return null;

            if (idx < 0 || idx >= cats.Count)
                idx = 0;

            return cats[idx];
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return (s.Length <= max) ? s : s.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string Color(string text, int rgb)
        {
            return string.Format("<BASEFONT COLOR=\"#{0:X6}\">{1}</BASEFONT>", rgb, text);
        }

        private static class TypeCache
        {
            private static readonly Dictionary<string, Type> _cache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            public static Type Resolve(string name)
            {
                if (string.IsNullOrEmpty(name)) return null;
                if (_cache.TryGetValue(name, out var t)) return t;

                t = FindType(name);
                _cache[name] = t;
                return t;
            }

            private static Type FindType(string name)
            {
                Type t = Type.GetType(name, false, true);
                if (t != null) return t;

                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = a.GetTypes(); }
                    catch { continue; }

                    foreach (Type tt in types)
                    {
                        if (tt?.Name != null && tt.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return tt;
                        if (tt?.FullName != null && tt.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)) return tt;
                    }
                }

                return null;
            }
        }
    }
}
