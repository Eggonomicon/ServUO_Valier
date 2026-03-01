using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

using System.Reflection;
using Server;
using Server.Accounting;
using Server.Commands;
using Server.Items;
using Server.Misc;
using Server.Mobiles;
using Server.Network;

// Optional: if you want "skill gain boost" tokens to reuse your existing PowerHour system:
using Server.Custom.PowerHour;

namespace Server.Custom.ValierBoosters
{
    /*
     * Valier Boosters (Loot, Fame, Skill Gain) — ServUO 57.x friendly
     *
     * Goals:
     * - "Sovereign shop" style boosters you can sell for Valier wallet coins.
     * - Account-bound booster timers (like a wallet/flag, not tradable once activated).
     * - No core edits required:
     *     * Loot & Fame hook via EventSink.CreatureDeath (Publish 57 supports this).
     *
     * What this script does:
     * 1) Adds booster token items (storeable in your ValierShop):
     *    - LootBoostToken30m / LootBoostToken60m
     *    - FameBoostToken30m / FameBoostToken60m
     *    - SkillGainBoostToken30m / SkillGainBoostToken60m  (reuses PowerHourSystem to boost skill gains)
     *
     * 2) Implements account-bound "active boosters" saved to:
     *    - Saves/ValierBoosters.xml
     *
     * 3) Applies boosters:
     *    - Loot: adds BONUS GOLD to corpse based on the gold already in the corpse
     *           and (optional) can add a small extra loot roll (e.g., a random gem)
     *    - Fame: awards extra fame on creature death (on top of normal fame)
     *    - SkillGain: uses PowerHourSystem.TryActivate(...) with configured multiplier/duration
     *
     * 4) Config file (direct read, no global config dependency):
     *    - Config/ValierBoosters.cfg
     *
     * Commands:
     * - Player:
     *     [ValierBoosters   -> shows remaining booster times / values
     *
     * - GM:
     *     [ReloadValierBoosters
     *     [ClearValierBoosters <playerName>
     *     [ValierBoostersInfo
     *
     * Notes:
     * - Loot boost is intentionally "safe": it scales from gold already dropped by the creature
     *   to avoid injecting gold into creatures that normally drop none.
     * - Fame boost uses a conservative OSI-ish approximation:
     *     baseAward ~= max(0, (creatureFame - playerFame) / 100)
     *   and then awards a % of that as extra via Titles.AwardFame(...)
     */

    // =========================
    // Config (direct file read)
    // =========================
    public static class ValierBoostersCfg
    {
        private static readonly object _sync = new object();
        private static Dictionary<string, string> _kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ConfigFilePath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "ValierBoosters.cfg"); }
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

                    // Support "\" line continuation
                    List<string> lines = new List<string>();

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

                        while (line.EndsWith("\\") && i + 1 < raw.Length)
                        {
                            line = line.Substring(0, line.Length - 1);
                            string next = raw[++i];

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

        public static bool Enabled { get { return GetBool("ValierBoosters.Enabled", true); } }

        // Stacking behavior
        public static bool AllowExtend { get { return GetBool("ValierBoosters.AllowExtend", true); } }
        public static int MaxMinutes { get { return GetInt("ValierBoosters.MaxMinutes", 240); } }

        // Loot booster behavior
        public static bool LootEnabled { get { return GetBool("ValierBoosters.Loot.Enabled", true); } }
        public static int LootGoldBonusPercent_Default { get { return GetInt("ValierBoosters.Loot.GoldBonusPercent", 50); } }

        public static double LootExtraItemChance { get { return GetDouble("ValierBoosters.Loot.ExtraItemChance", 0.10); } }
        public static string LootExtraItemTypes { get { return GetString("ValierBoosters.Loot.ExtraItemTypes", "Amber|Amethyst|Citrine|Diamond|Emerald|Ruby|Sapphire|StarSapphire|Tourmaline"); } }

        // Fame booster behavior
        public static bool FameEnabled { get { return GetBool("ValierBoosters.Fame.Enabled", true); } }
        public static int FameBonusPercent_Default { get { return GetInt("ValierBoosters.Fame.BonusPercent", 50); } }

        // Skill gain booster behavior (reuses PowerHourSystem)
        public static bool SkillGainEnabled { get { return GetBool("ValierBoosters.SkillGain.Enabled", true); } }
        public static double SkillGainMultiplier_Default { get { return GetDouble("ValierBoosters.SkillGain.Multiplier", 2.5); } }

        // Per-token overrides (optional)
        public static int TokenMinutes(string tokenName, int def)
        {
            return GetInt("ValierBoosters.Token." + tokenName + ".Minutes", def);
        }

        public static int TokenPercent(string tokenName, int def)
        {
            return GetInt("ValierBoosters.Token." + tokenName + ".Percent", def);
        }

        public static double TokenMultiplier(string tokenName, double def)
        {
            return GetDouble("ValierBoosters.Token." + tokenName + ".Multiplier", def);
        }
    }

    // =========================
    // State (account-bound)
    // =========================
    public enum BoosterType
    {
        Loot,
        Fame
    }

    public class BoosterState
    {
        public DateTime LootEndUtc = DateTime.MinValue;
        public int LootGoldBonusPercent = 0;

        public DateTime FameEndUtc = DateTime.MinValue;
        public int FameBonusPercent = 0;
    }

    public static class ValierBoostersSystem
    {
        private static readonly string SavePath = Path.Combine(Core.BaseDirectory, "Saves", "ValierBoosters.xml");
        private static readonly Dictionary<string, BoosterState> _states = new Dictionary<string, BoosterState>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            ValierBoostersCfg.Reload();

            EventSink.WorldLoad += OnWorldLoad;
            EventSink.WorldSave += OnWorldSave;

            // Loot & Fame: no core edits required
            EventSink.CreatureDeath += EventSink_CreatureDeath;

            // Commands
            CommandSystem.Register("ValierBoosters", AccessLevel.Player, OnPlayerStatus);

            CommandSystem.Register("ReloadValierBoosters", AccessLevel.GameMaster, OnReload);
            CommandSystem.Register("ClearValierBoosters", AccessLevel.GameMaster, OnClear);
            CommandSystem.Register("ValierBoostersInfo", AccessLevel.GameMaster, OnInfo);
        }

        private static void OnWorldLoad()
        {
            Load();
        }

        private static void OnWorldSave(WorldSaveEventArgs e)
        {
            Save();
        }

        private static void OnReload(CommandEventArgs e)
        {
            ValierBoostersCfg.Reload();
            e.Mobile.SendMessage(0x59, "ValierBoosters: reloaded Config/ValierBoosters.cfg");
        }

        private static void OnInfo(CommandEventArgs e)
        {
            Mobile m = e.Mobile;

            m.SendMessage(0x59, "ValierBoosters cfg: {0}", ValierBoostersCfg.ConfigFilePath);
            m.SendMessage(0x59, "Enabled={0}  AllowExtend={1}  MaxMinutes={2}", ValierBoostersCfg.Enabled, ValierBoostersCfg.AllowExtend, ValierBoostersCfg.MaxMinutes);
            m.SendMessage(0x59, "Loot.Enabled={0}  Loot.GoldBonusPercent(default)={1}  ExtraItemChance={2}",
                ValierBoostersCfg.LootEnabled, ValierBoostersCfg.LootGoldBonusPercent_Default, ValierBoostersCfg.LootExtraItemChance.ToString(CultureInfo.InvariantCulture));
            m.SendMessage(0x59, "Fame.Enabled={0}  Fame.BonusPercent(default)={1}", ValierBoostersCfg.FameEnabled, ValierBoostersCfg.FameBonusPercent_Default);
            m.SendMessage(0x59, "SkillGain.Enabled={0}  SkillGain.Multiplier(default)={1}",
                ValierBoostersCfg.SkillGainEnabled, ValierBoostersCfg.SkillGainMultiplier_Default.ToString(CultureInfo.InvariantCulture));
        }

        private static void OnClear(CommandEventArgs e)
        {
            Mobile from = e.Mobile;

            if (e.Length < 1)
            {
                from.SendMessage("Usage: [ClearValierBoosters <playerName>");
                return;
            }

            string name = e.GetString(0);
            PlayerMobile pm = FindOnlinePlayerByName(name);

            if (pm == null)
            {
                from.SendMessage("Player not found (must be online).");
                return;
            }

            string key = GetAccountKey(pm);

            BoosterState st;
            if (_states.TryGetValue(key, out st))
            {
                _states.Remove(key);
                from.SendMessage(0x59, "Cleared Valier boosters for {0}.", pm.Name);
                pm.SendMessage(0x59, "Your Valier boosters were cleared by a GM.");
            }
            else
            {
                from.SendMessage("No booster state found for that player.");
            }
        }

        private static void OnPlayerStatus(CommandEventArgs e)
        {
            PlayerMobile pm = e.Mobile as PlayerMobile;
            if (pm == null)
                return;

            string key = GetAccountKey(pm);
            BoosterState st = GetOrCreate(key);

            DateTime now = DateTime.UtcNow;

            TimeSpan lootRem = (st.LootEndUtc > now) ? (st.LootEndUtc - now) : TimeSpan.Zero;
            TimeSpan fameRem = (st.FameEndUtc > now) ? (st.FameEndUtc - now) : TimeSpan.Zero;

            if (lootRem <= TimeSpan.Zero && fameRem <= TimeSpan.Zero)
            {
                pm.SendMessage("You have no active Valier boosters.");
                return;
            }

            if (lootRem > TimeSpan.Zero)
                pm.SendMessage(0x59, "Loot Boost: +{0}% gold on corpses, remaining {1:mm\\:ss}.", st.LootGoldBonusPercent, lootRem);

            if (fameRem > TimeSpan.Zero)
                pm.SendMessage(0x59, "Fame Boost: +{0}% fame, remaining {1:mm\\:ss}.", st.FameBonusPercent, fameRem);

            // SkillGain boost is handled by PowerHourSystem itself (token activates that system)
        }

        private static void EventSink_CreatureDeath(CreatureDeathEventArgs e)
        {
            if (!ValierBoostersCfg.Enabled)
                return;

            if (e == null || e.Creature == null || e.Corpse == null || e.Killer == null)
                return;

            Corpse corpse = e.Corpse as Corpse;
            if (corpse == null)
                return;

            BaseCreature creature = e.Creature as BaseCreature;
            if (creature == null)
                return;

            PlayerMobile pm = ResolveKillerPlayer(e.Killer);
            if (pm == null || pm.Deleted)
                return;

            string key = GetAccountKey(pm);
            BoosterState st = GetOrCreate(key);

            DateTime now = DateTime.UtcNow;

            // Loot boost (gold + optional extra item roll)
            if (ValierBoostersCfg.LootEnabled && st.LootEndUtc > now && st.LootGoldBonusPercent > 0)
            {
                ApplyLootBoost(pm, creature, corpse, st.LootGoldBonusPercent);
            }

            // Fame boost
            if (ValierBoostersCfg.FameEnabled && st.FameEndUtc > now && st.FameBonusPercent > 0)
            {
                ApplyFameBoost(pm, creature, st.FameBonusPercent);
            }
        }

        private static void ApplyLootBoost(PlayerMobile pm, BaseCreature creature, Corpse corpse, int goldBonusPercent)
        {
            try
            {
                if (corpse == null || corpse.Deleted)
                    return;

                // Sum gold already on corpse
                int gold = 0;

                foreach (Item item in corpse.FindItemsByType(typeof(Gold), true))
                {
                    Gold g = item as Gold;
                    if (g != null && !g.Deleted)
                        gold += g.Amount;
                }

                if (gold <= 0)
                {
                    // Safe by default: don't inject gold into creatures that drop none.
                    // If you want a fallback, we can add one later.
                    return;
                }

                int bonus = (int)Math.Ceiling(gold * (goldBonusPercent / 100.0));

                if (bonus > 0)
                {
                    corpse.AddItem(new Gold(bonus));
                    // Feedback to player is intentionally subtle (no spam). You can enable messaging later.
                }

                // Optional extra loot roll (e.g., random gem)
                double chance = ValierBoostersCfg.LootExtraItemChance;
                if (chance > 0.0 && Utility.RandomDouble() < chance)
                {
                    Type t = ResolveTypeFromList(ValierBoostersCfg.LootExtraItemTypes);
                    if (t != null && typeof(Item).IsAssignableFrom(t))
                    {
                        Item item = null;

                        try { item = Activator.CreateInstance(t) as Item; }
                        catch { item = null; }

                        if (item != null)
                            corpse.AddItem(item);
                    }
                }
            }
            catch
            {
                // swallow: never break creature death flow
            }
        }

        private static void ApplyFameBoost(PlayerMobile pm, BaseCreature creature, int fameBonusPercent)
        {
            try
            {
                if (pm == null || pm.Deleted || creature == null)
                    return;

                // Conservative OSI-ish approximation of fame gained:
                // base ~= max(0, (creatureFame - playerFame) / 100)
                int creatureFame = Math.Max(0, creature.Fame);
                int playerFame = Math.Max(0, pm.Fame);

                int baseAward = 0;

                if (creatureFame > playerFame)
                    baseAward = (creatureFame - playerFame) / 100;

                if (baseAward <= 0)
                    return;

                int bonus = (int)Math.Floor(baseAward * (fameBonusPercent / 100.0));

                if (bonus <= 0)
                    return;

                Titles.AwardFame(pm, bonus, true);
            }
            catch
            {
            }
        }

        private static Type ResolveTypeFromList(string list)
        {
            if (string.IsNullOrWhiteSpace(list))
                return null;

            // Types separated by | or ;
            string[] parts = list.Split(new char[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts == null || parts.Length == 0)
                return null;

            string pick = parts[Utility.Random(parts.Length)].Trim();

            if (string.IsNullOrWhiteSpace(pick))
                return null;

            return TypeCache.Resolve(pick);
        }

        public static bool ActivateLootBooster(Mobile from, int minutes, int goldBonusPercent)
        {
            if (!ValierBoostersCfg.Enabled || !ValierBoostersCfg.LootEnabled)
                return false;

            PlayerMobile pm = from as PlayerMobile;
            if (pm == null)
                return false;

            minutes = Math.Max(1, minutes);
            goldBonusPercent = Math.Max(1, goldBonusPercent);

            string key = GetAccountKey(pm);
            BoosterState st = GetOrCreate(key);

            DateTime now = DateTime.UtcNow;

            TimeSpan add = TimeSpan.FromMinutes(minutes);
            TimeSpan cap = TimeSpan.FromMinutes(Math.Max(1, ValierBoostersCfg.MaxMinutes));

            if (st.LootEndUtc > now)
            {
                if (!ValierBoostersCfg.AllowExtend)
                    return false;

                TimeSpan rem = st.LootEndUtc - now;
                TimeSpan newRem = rem + add;

                if (newRem > cap)
                    newRem = cap;

                st.LootEndUtc = now + newRem;
                st.LootGoldBonusPercent = Math.Max(st.LootGoldBonusPercent, goldBonusPercent);
            }
            else
            {
                st.LootEndUtc = now + add;
                st.LootGoldBonusPercent = goldBonusPercent;
            }

            return true;
        }

        public static bool ActivateFameBooster(Mobile from, int minutes, int fameBonusPercent)
        {
            if (!ValierBoostersCfg.Enabled || !ValierBoostersCfg.FameEnabled)
                return false;

            PlayerMobile pm = from as PlayerMobile;
            if (pm == null)
                return false;

            minutes = Math.Max(1, minutes);
            fameBonusPercent = Math.Max(1, fameBonusPercent);

            string key = GetAccountKey(pm);
            BoosterState st = GetOrCreate(key);

            DateTime now = DateTime.UtcNow;

            TimeSpan add = TimeSpan.FromMinutes(minutes);
            TimeSpan cap = TimeSpan.FromMinutes(Math.Max(1, ValierBoostersCfg.MaxMinutes));

            if (st.FameEndUtc > now)
            {
                if (!ValierBoostersCfg.AllowExtend)
                    return false;

                TimeSpan rem = st.FameEndUtc - now;
                TimeSpan newRem = rem + add;

                if (newRem > cap)
                    newRem = cap;

                st.FameEndUtc = now + newRem;
                st.FameBonusPercent = Math.Max(st.FameBonusPercent, fameBonusPercent);
            }
            else
            {
                st.FameEndUtc = now + add;
                st.FameBonusPercent = fameBonusPercent;
            }

            return true;
        }

        private static BoosterState GetOrCreate(string key)
        {
            if (string.IsNullOrEmpty(key))
                key = "unknown";

            BoosterState st;
            if (!_states.TryGetValue(key, out st) || st == null)
            {
                st = new BoosterState();
                _states[key] = st;
            }

            return st;
        }

        private static string GetAccountKey(Mobile m)
        {
            if (m == null)
                return null;

            IAccount acc = m.Account as IAccount;

            if (acc != null && !string.IsNullOrEmpty(acc.Username))
                return acc.Username;

            return "char:" + m.Serial.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static PlayerMobile ResolveKillerPlayer(Mobile killer)
        {
            // Player directly
            PlayerMobile pm = killer as PlayerMobile;
            if (pm != null)
                return pm;

            // Controlled pet
            BaseCreature bc = killer as BaseCreature;
            if (bc != null)
            {
                if (bc.Controlled && bc.ControlMaster is PlayerMobile cm)
                    return cm;

                if (bc.Summoned && bc.SummonMaster is PlayerMobile sm)
                    return sm;
            }

            return null;
        }

        private static PlayerMobile FindOnlinePlayerByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            foreach (NetState ns in NetState.Instances)
            {
                if (ns == null || ns.Mobile == null)
                    continue;

                PlayerMobile pm = ns.Mobile as PlayerMobile;
                if (pm != null && pm.Name != null && pm.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return pm;
            }

            return null;
        }

        private static void Load()
        {
            _states.Clear();

            try
            {
                if (!File.Exists(SavePath))
                    return;

                XDocument doc = XDocument.Load(SavePath);

                XElement root = doc.Root;
                if (root == null)
                    return;

                foreach (XElement el in root.Elements("acct"))
                {
                    XAttribute k = el.Attribute("key");
                    if (k == null || string.IsNullOrEmpty(k.Value))
                        continue;

                    BoosterState st = new BoosterState();

                    st.LootEndUtc = ParseUtc(el.Attribute("lootEnd"), DateTime.MinValue);
                    st.LootGoldBonusPercent = ParseInt(el.Attribute("lootPct"), 0);

                    st.FameEndUtc = ParseUtc(el.Attribute("fameEnd"), DateTime.MinValue);
                    st.FameBonusPercent = ParseInt(el.Attribute("famePct"), 0);

                    _states[k.Value] = st;
                }
            }
            catch
            {
                _states.Clear();
            }
        }

        private static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SavePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                XElement root = new XElement("valierBoosters");

                foreach (KeyValuePair<string, BoosterState> kv in _states)
                {
                    BoosterState st = kv.Value;
                    if (st == null)
                        continue;

                    root.Add(new XElement("acct",
                        new XAttribute("key", kv.Key),
                        new XAttribute("lootEnd", st.LootEndUtc.ToString("o", CultureInfo.InvariantCulture)),
                        new XAttribute("lootPct", st.LootGoldBonusPercent.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("fameEnd", st.FameEndUtc.ToString("o", CultureInfo.InvariantCulture)),
                        new XAttribute("famePct", st.FameBonusPercent.ToString(CultureInfo.InvariantCulture))
                    ));
                }

                XDocument doc = new XDocument(root);
                doc.Save(SavePath);
            }
            catch
            {
            }
        }

        private static DateTime ParseUtc(XAttribute a, DateTime def)
        {
            if (a == null || string.IsNullOrEmpty(a.Value))
                return def;

            DateTime d;
            if (DateTime.TryParse(a.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d))
                return d;

            return def;
        }

        private static int ParseInt(XAttribute a, int def)
        {
            if (a == null || string.IsNullOrEmpty(a.Value))
                return def;

            int n;
            if (int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;

            return def;
        }
    }

    // =========================
    // Token Items
    // =========================

    public abstract class BaseBoosterToken : Item
    {
        protected BaseBoosterToken(int itemID) : base(itemID)
        {
            LootType = LootType.Regular;
            Weight = 1.0;
        }

        
        // REQUIRED for serialization
        public BaseBoosterToken(Serial serial) : base(serial)
        {
        }

protected abstract string TokenKey { get; }

        protected virtual int DefaultMinutes { get { return 30; } }

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

        protected bool EnsureBackpack(Mobile from)
        {
            if (from == null || from.Deleted)
                return false;

            if (!IsChildOf(from.Backpack))
            {
                from.SendMessage("That must be in your backpack to use it.");
                return false;
            }

            return true;
        }

        protected int GetMinutes(int def)
        {
            return ValierBoostersCfg.TokenMinutes(TokenKey, def);
        }
    }

    // ---- Loot tokens ----
    public class LootBoostToken30m : BaseBoosterToken
    {
        [Constructable]
        public LootBoostToken30m() : base(0x14F0)
        {
            Name = "a loot booster (30m)";
            Hue = 0x489;
        }

        // REQUIRED for serialization
        public LootBoostToken30m(Serial serial) : base(serial)
        {
        }


        protected override string TokenKey { get { return "Loot30m"; } }
        protected override int DefaultMinutes { get { return 30; } }

        private int GetPercent()
        {
            return ValierBoostersCfg.TokenPercent(TokenKey, ValierBoostersCfg.LootGoldBonusPercent_Default);
        }

        public override void OnDoubleClick(Mobile from)
        {
            if (!EnsureBackpack(from))
                return;

            int minutes = GetMinutes(DefaultMinutes);
            int pct = Math.Max(1, GetPercent());

            if (ValierBoostersSystem.ActivateLootBooster(from, minutes, pct))
            {
                from.SendMessage(0x59, "Loot boost activated: +{0}% corpse gold for {1} minutes.", pct, minutes);
                Delete();
            }
            else
            {
                from.SendMessage("Could not activate loot boost.");
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

    public class LootBoostToken60m : BaseBoosterToken
    {
        [Constructable]
        public LootBoostToken60m() : base(0x14F0)
        {
            Name = "a loot booster (60m)";
            Hue = 0x489;
        }

        // REQUIRED for serialization
        public LootBoostToken60m(Serial serial) : base(serial)
        {
        }


        protected override string TokenKey { get { return "Loot60m"; } }
        protected override int DefaultMinutes { get { return 60; } }

        private int GetPercent()
        {
            return ValierBoostersCfg.TokenPercent(TokenKey, ValierBoostersCfg.LootGoldBonusPercent_Default);
        }

        public override void OnDoubleClick(Mobile from)
        {
            if (!EnsureBackpack(from))
                return;

            int minutes = GetMinutes(DefaultMinutes);
            int pct = Math.Max(1, GetPercent());

            if (ValierBoostersSystem.ActivateLootBooster(from, minutes, pct))
            {
                from.SendMessage(0x59, "Loot boost activated: +{0}% corpse gold for {1} minutes.", pct, minutes);
                Delete();
            }
            else
            {
                from.SendMessage("Could not activate loot boost.");
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

    // ---- Fame tokens ----
    public class FameBoostToken30m : BaseBoosterToken
    {
        [Constructable]
        public FameBoostToken30m() : base(0x14F0)
        {
            Name = "a fame booster (30m)";
            Hue = 0x482;
        }

        // REQUIRED for serialization
        public FameBoostToken30m(Serial serial) : base(serial)
        {
        }


        protected override string TokenKey { get { return "Fame30m"; } }
        protected override int DefaultMinutes { get { return 30; } }

        private int GetPercent()
        {
            return ValierBoostersCfg.TokenPercent(TokenKey, ValierBoostersCfg.FameBonusPercent_Default);
        }

        public override void OnDoubleClick(Mobile from)
        {
            if (!EnsureBackpack(from))
                return;

            int minutes = GetMinutes(DefaultMinutes);
            int pct = Math.Max(1, GetPercent());

            if (ValierBoostersSystem.ActivateFameBooster(from, minutes, pct))
            {
                from.SendMessage(0x59, "Fame boost activated: +{0}% fame for {1} minutes.", pct, minutes);
                Delete();
            }
            else
            {
                from.SendMessage("Could not activate fame boost.");
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

    public class FameBoostToken60m : BaseBoosterToken
    {
        [Constructable]
        public FameBoostToken60m() : base(0x14F0)
        {
            Name = "a fame booster (60m)";
            Hue = 0x482;
        }

        // REQUIRED for serialization
        public FameBoostToken60m(Serial serial) : base(serial)
        {
        }


        protected override string TokenKey { get { return "Fame60m"; } }
        protected override int DefaultMinutes { get { return 60; } }

        private int GetPercent()
        {
            return ValierBoostersCfg.TokenPercent(TokenKey, ValierBoostersCfg.FameBonusPercent_Default);
        }

        public override void OnDoubleClick(Mobile from)
        {
            if (!EnsureBackpack(from))
                return;

            int minutes = GetMinutes(DefaultMinutes);
            int pct = Math.Max(1, GetPercent());

            if (ValierBoostersSystem.ActivateFameBooster(from, minutes, pct))
            {
                from.SendMessage(0x59, "Fame boost activated: +{0}% fame for {1} minutes.", pct, minutes);
                Delete();
            }
            else
            {
                from.SendMessage("Could not activate fame boost.");
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

    // ---- Skill gain tokens (reuses your PowerHour system) ----
    // These do NOT use the account-bound state above; they activate the existing PowerHourSystem
    // so players get skill gains increased immediately for the duration.
    public class SkillGainBoostToken30m : Item
    {
        [Constructable]
        public SkillGainBoostToken30m() : base(0x14F0)
        {
            Name = "a skill gain booster (30m)";
            Hue = 0x48C;
            Weight = 1.0;
        }

        // REQUIRED for serialization
        public SkillGainBoostToken30m(Serial serial) : base(serial)
        {
        }


        private int Minutes
        {
            get { return ValierBoostersCfg.TokenMinutes("Skill30m", 30); }
        }

        private double Mult
        {
            get { return ValierBoostersCfg.TokenMultiplier("Skill30m", ValierBoostersCfg.SkillGainMultiplier_Default); }
        }

        public override void OnDoubleClick(Mobile from)
        {
            if (from == null || from.Deleted)
                return;

            if (!ValierBoostersCfg.SkillGainEnabled)
            {
                from.SendMessage("Skill gain boosters are disabled.");
                return;
            }

            if (!IsChildOf(from.Backpack))
            {
                from.SendMessage("That must be in your backpack to use it.");
                return;
            }

            int mins = Math.Max(1, Minutes);
            double mult = Math.Max(1.01, Mult);

            if (PowerHourSystem.TryActivate(from, TimeSpan.FromMinutes(mins), mult, true))
            {
                from.SendMessage(0x59, "Skill gain boost activated: x{0} for {1} minutes.", mult.ToString("0.##", CultureInfo.InvariantCulture), mins);
                Delete();
            }
            else
            {
                from.SendMessage("Could not activate skill gain boost.");
            }
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    public class SkillGainBoostToken60m : Item
    {
        [Constructable]
        public SkillGainBoostToken60m() : base(0x14F0)
        {
            Name = "a skill gain booster (60m)";
            Hue = 0x48C;
            Weight = 1.0;
        }

        // REQUIRED for serialization
        public SkillGainBoostToken60m(Serial serial) : base(serial)
        {
        }


        private int Minutes
        {
            get { return ValierBoostersCfg.TokenMinutes("Skill60m", 60); }
        }

        private double Mult
        {
            get { return ValierBoostersCfg.TokenMultiplier("Skill60m", ValierBoostersCfg.SkillGainMultiplier_Default); }
        }

        public override void OnDoubleClick(Mobile from)
        {
            if (from == null || from.Deleted)
                return;

            if (!ValierBoostersCfg.SkillGainEnabled)
            {
                from.SendMessage("Skill gain boosters are disabled.");
                return;
            }

            if (!IsChildOf(from.Backpack))
            {
                from.SendMessage("That must be in your backpack to use it.");
                return;
            }

            int mins = Math.Max(1, Minutes);
            double mult = Math.Max(1.01, Mult);

            if (PowerHourSystem.TryActivate(from, TimeSpan.FromMinutes(mins), mult, true))
            {
                from.SendMessage(0x59, "Skill gain boost activated: x{0} for {1} minutes.", mult.ToString("0.##", CultureInfo.InvariantCulture), mins);
                Delete();
            }
            else
            {
                from.SendMessage("Could not activate skill gain boost.");
            }
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    // =========================
    // Type cache helper
    // =========================
    internal static class TypeCache
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
            Type t = Type.GetType(name, false, true);
            if (t != null)
                return t;

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;

                try { types = a.GetTypes(); }
                catch { continue; }

                for (int i = 0; i < types.Length; i++)
                {
                    Type tt = types[i];

                    if (tt == null)
                        continue;

                    if (tt.Name != null && tt.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return tt;

                    if (tt.FullName != null && tt.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return tt;
                }
            }

            return null;
        }
    }
}