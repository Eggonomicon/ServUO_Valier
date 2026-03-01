using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Server;
using Server.Commands;
using Server.Items;
using Server.Mobiles;
using Server.Network;

namespace Server.Custom.PowerHour
{
    /*
     * Power Hour (Config/PowerHour.cfg, standalone, ServUO 57.x friendly)
     *
     * Why this version:
     * - Reads its OWN config file directly (Config/PowerHour.cfg) so it does not depend on ServUO's global Config loader.
     * - Adds hot reload command: [ReloadPowerHour (GM)
     * - Adds info command: [PowerHourInfo (GM) to verify loaded values
     *
     * Features:
     * - PowerHourToken: consumable item to activate Power Hour
     * - PowerHourSystem: multiplies skill gains by observing actual skill increases and applying bonus gain
     *
     * ----------------------------
     * CONFIG FILE: Config/PowerHour.cfg
     * ----------------------------
     * PowerHour.Enabled=true
     * PowerHour.DurationMinutes=60
     * PowerHour.Multiplier=2.0
     * PowerHour.AllowExtend=true
     * PowerHour.MaxExtensionMinutes=240
     * PowerHour.TickMs=250
     * PowerHour.MaxBonusPerTick=1.0
     */

    // =========================
    // Config file reader (local)
    // =========================
    public static class PowerHourCfg
    {
        private static readonly object _sync = new object();
        private static Dictionary<string, string> _kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ConfigFilePath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "PowerHour.cfg"); }
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

                    string[] rawLines = File.ReadAllLines(path);

                    // Support line continuation with trailing "\" (handy if you ever need multi-line values)
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

        // Public settings
        public static bool Enabled { get { return GetBool("PowerHour.Enabled", true); } }
        public static int DurationMinutes { get { return GetInt("PowerHour.DurationMinutes", 60); } }
        public static double Multiplier { get { return GetDouble("PowerHour.Multiplier", 2.0); } }

        public static bool AllowExtend { get { return GetBool("PowerHour.AllowExtend", true); } }
        public static int MaxExtensionMinutes { get { return GetInt("PowerHour.MaxExtensionMinutes", 240); } }

        public static int TickMs { get { return GetInt("PowerHour.TickMs", 250); } }
        public static double MaxBonusPerTick { get { return GetDouble("PowerHour.MaxBonusPerTick", 1.0); } }
    }

    // =========================
    // ITEM: Power Hour Token
    // =========================
    public class PowerHourToken : Item
    {
        [Constructable]
        public PowerHourToken() : base(0x14F0)
        {
            Name = "a power hour token";
            Weight = 1.0;
            LootType = LootType.Regular;
        }

        public PowerHourToken(Serial serial) : base(serial) { }

        public override void OnDoubleClick(Mobile from)
        {
            if (from == null || from.Deleted)
                return;

            if (!PowerHourCfg.Enabled)
            {
                from.SendMessage("Power Hour is currently disabled.");
                return;
            }

            if (!IsChildOf(from.Backpack))
            {
                from.SendMessage("That must be in your backpack to use it.");
                return;
            }

            TimeSpan duration = TimeSpan.FromMinutes(Math.Max(1, PowerHourCfg.DurationMinutes));
            double mult = PowerHourCfg.Multiplier;

            if (PowerHourSystem.TryActivate(from, duration, mult, true))
            {
                from.SendMessage(0x59, "Power Hour activated! Your skill gains are multiplied for {0} minutes.", (int)duration.TotalMinutes);
                Delete();
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
    // SYSTEM: Power Hour Manager
    // =========================
    public static class PowerHourSystem
    {
        private static readonly Dictionary<Serial, PowerHourContext> _active = new Dictionary<Serial, PowerHourContext>();

        public static void Initialize()
        {
            PowerHourCfg.Reload();

            // Player
            CommandSystem.Register("PowerHour", AccessLevel.Player, OnPowerHour);

            // GM
            CommandSystem.Register("GivePowerHourToken", AccessLevel.GameMaster, OnGiveToken);
            CommandSystem.Register("ReloadPowerHour", AccessLevel.GameMaster, OnReload);
            CommandSystem.Register("PowerHourInfo", AccessLevel.GameMaster, OnInfo);
        }

        private static void OnReload(CommandEventArgs e)
        {
            PowerHourCfg.Reload();
            e.Mobile.SendMessage(0x59, "PowerHour: reloaded Config/PowerHour.cfg");
        }

        private static void OnInfo(CommandEventArgs e)
        {
            Mobile m = e.Mobile;

            m.SendMessage(0x59, "PowerHour.Enabled={0}", PowerHourCfg.Enabled);
            m.SendMessage(0x59, "PowerHour.DurationMinutes={0}", PowerHourCfg.DurationMinutes);
            m.SendMessage(0x59, "PowerHour.Multiplier={0}", PowerHourCfg.Multiplier.ToString(CultureInfo.InvariantCulture));
            m.SendMessage(0x59, "PowerHour.AllowExtend={0}", PowerHourCfg.AllowExtend);
            m.SendMessage(0x59, "PowerHour.MaxExtensionMinutes={0}", PowerHourCfg.MaxExtensionMinutes);
            m.SendMessage(0x59, "PowerHour.TickMs={0}", PowerHourCfg.TickMs);
            m.SendMessage(0x59, "PowerHour.MaxBonusPerTick={0}", PowerHourCfg.MaxBonusPerTick.ToString(CultureInfo.InvariantCulture));
        }

        public static bool TryActivate(Mobile m, TimeSpan duration, double multiplier, bool showMessages)
        {
            if (!PowerHourCfg.Enabled)
                return false;

            if (m == null || m.Deleted)
                return false;

            if (!(m is PlayerMobile))
                return false;

            if (multiplier <= 1.0)
                multiplier = 2.0;

            if (duration <= TimeSpan.Zero)
                duration = TimeSpan.FromMinutes(60);

            PowerHourContext existing;
            if (_active.TryGetValue(m.Serial, out existing) && existing != null && !existing.Expired)
            {
                if (!PowerHourCfg.AllowExtend)
                {
                    if (showMessages)
                        m.SendMessage("You already have an active Power Hour.");
                    return false;
                }

                TimeSpan remaining = existing.End - DateTime.UtcNow;
                TimeSpan newRemaining = remaining + duration;

                TimeSpan cap = TimeSpan.FromMinutes(Math.Max(1, PowerHourCfg.MaxExtensionMinutes));
                if (newRemaining > cap)
                    newRemaining = cap;

                existing.End = DateTime.UtcNow + newRemaining;
                existing.Multiplier = Math.Max(existing.Multiplier, multiplier);

                if (showMessages)
                    m.SendMessage(0x59, "Power Hour extended. Time remaining: {0:mm\\:ss}.", newRemaining);

                return true;
            }

            PowerHourContext ctx = new PowerHourContext(m, DateTime.UtcNow + duration, multiplier);
            _active[m.Serial] = ctx;
            ctx.Start();

            return true;
        }

        public static TimeSpan GetRemaining(Mobile m)
        {
            PowerHourContext ctx;
            if (m != null && _active.TryGetValue(m.Serial, out ctx) && ctx != null && !ctx.Expired)
                return ctx.End - DateTime.UtcNow;

            return TimeSpan.Zero;
        }

        private static void Stop(Mobile m)
        {
            if (m == null)
                return;

            PowerHourContext ctx;
            if (_active.TryGetValue(m.Serial, out ctx) && ctx != null)
            {
                ctx.Stop();
                _active.Remove(m.Serial);
            }
        }

        private static void OnPowerHour(CommandEventArgs e)
        {
            Mobile m = e.Mobile;

            TimeSpan rem = GetRemaining(m);

            if (rem <= TimeSpan.Zero)
            {
                m.SendMessage("You do not have an active Power Hour.");
                return;
            }

            m.SendMessage(0x59, "Power Hour remaining: {0:mm\\:ss}.", rem);
        }

        private static void OnGiveToken(CommandEventArgs e)
        {
            Mobile from = e.Mobile;

            if (e.Length < 1)
            {
                from.SendMessage("Usage: [GivePowerHourToken <playerName> [amount]");
                return;
            }

            string name = e.GetString(0);
            int amount = 1;

            if (e.Length >= 2)
            {
                try { amount = e.GetInt32(1); }
                catch { amount = 1; }
            }

            if (amount < 1)
                amount = 1;

            PlayerMobile pm = FindPlayerByName(name);

            if (pm == null)
            {
                from.SendMessage("Player not found.");
                return;
            }

            if (pm.Backpack == null)
            {
                from.SendMessage("That player has no backpack.");
                return;
            }

            for (int i = 0; i < amount; i++)
                pm.Backpack.DropItem(new PowerHourToken());

            from.SendMessage("Gave {0} Power Hour token(s) to {1}.", amount, pm.Name);
            pm.SendMessage(0x59, "You received {0} Power Hour token(s).", amount);
        }

        private static PlayerMobile FindPlayerByName(string name)
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

        // =========================
        // Context / Timer
        // =========================
        private class PowerHourContext
        {
            public Mobile Mobile;
            public DateTime End;
            public double Multiplier;

            private double[] _last;
            private InternalTimer _timer;
            private bool _applying;

            public bool Expired { get { return DateTime.UtcNow >= End; } }

            public PowerHourContext(Mobile m, DateTime end, double mult)
            {
                Mobile = m;
                End = end;
                Multiplier = mult;

                int len = (m != null && m.Skills != null) ? m.Skills.Length : 0;
                _last = new double[len];

                for (int i = 0; i < len; i++)
                    _last[i] = m.Skills[i].Base;
            }

            public void Start()
            {
                if (_timer != null)
                    _timer.Stop();

                int tick = Math.Max(50, PowerHourCfg.TickMs);
                _timer = new InternalTimer(this, TimeSpan.FromMilliseconds(tick));
                _timer.Start();
            }

            public void Stop()
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer = null;
                }
            }

            public void Tick()
            {
                Mobile m = Mobile;

                if (m == null || m.Deleted || m.NetState == null || Expired)
                {
                    if (m != null && m.NetState != null)
                        m.SendMessage(0x59, "Power Hour ended.");

                    Stop();
                    PowerHourSystem.Stop(m);
                    return;
                }

                if (_applying)
                    return;

                int len = (m.Skills != null) ? m.Skills.Length : 0;
                if (_last == null || _last.Length != len)
                {
                    _last = new double[len];
                    for (int i = 0; i < len; i++)
                        _last[i] = m.Skills[i].Base;
                }

                double bonusFactor = Multiplier - 1.0;
                if (bonusFactor <= 0.0)
                    return;

                double maxBonus = PowerHourCfg.MaxBonusPerTick;
                if (maxBonus < 0.0)
                    maxBonus = 0.0;

                _applying = true;

                try
                {
                    for (int i = 0; i < len; i++)
                    {
                        Skill sk = m.Skills[i];
                        if (sk == null)
                            continue;

                        double cur = sk.Base;
                        double prev = _last[i];

                        if (cur > prev)
                        {
                            double delta = cur - prev;
                            double bonus = delta * bonusFactor;

                            if (maxBonus > 0.0 && bonus > maxBonus)
                                bonus = maxBonus;

                            double cap = sk.Cap;
                            if (cap > 0.0 && (cur + bonus) > cap)
                                bonus = cap - cur;

                            if (bonus > 0.0)
                                sk.Base = cur + bonus;

                            _last[i] = sk.Base;
                        }
                        else
                        {
                            _last[i] = cur;
                        }
                    }
                }
                finally
                {
                    _applying = false;
                }
            }

            private class InternalTimer : Timer
            {
                private readonly PowerHourContext _ctx;

                public InternalTimer(PowerHourContext ctx, TimeSpan interval)
                    : base(interval, interval)
                {
                    _ctx = ctx;
                    Priority = TimerPriority.TwoFiftyMS;
                }

                protected override void OnTick()
                {
                    if (_ctx != null)
                        _ctx.Tick();
                }
            }
        }
    }
}
