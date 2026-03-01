using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Server;
using Server.Accounting;
using Server.Commands;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;

namespace Server.Custom.LoginRewards
{
    /*
     * Login Rewards (reads Config/LoginRewards.cfg directly) + Claim Gump + ChoiceEveryDays
     *
     * NEW (requested):
     *  - GM command: [ResetLoginRewardsToday [playerName] [keepStreak]
     *      - Default: keepStreak = true
     *      - Works for ONLINE players (by name). If no name provided, targets the GM.
     *
     * Helpful GM commands:
     *  - [ReloadLoginRewards   -> reloads Config/LoginRewards.cfg
     *  - [LoginRewardsInfo     -> prints loaded config values
     *  - [ResetLoginRewardsToday <name> [keepStreak]
     *
     * Save/state:
     *  - Saves/LoginRewards.xml
     */

    // =========================
    // Config file reader (local)
    // =========================
    public static class LoginRewardsCfg
    {
        private static readonly object _sync = new object();
        private static Dictionary<string, string> _kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ConfigFilePath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "LoginRewards.cfg"); }
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

                    // Support line continuation with trailing "\" (handy for long Calendar7 strings)
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
        public static bool Enabled { get { return GetBool("LoginRewards.Enabled", true); } }
        public static bool OncePerAccountPerDay { get { return GetBool("LoginRewards.OncePerAccountPerDay", true); } }

        public static string Mode { get { return GetString("LoginRewards.Mode", "DailyStreak"); } }           // DailyStreak, Calendar7
        public static string ClaimMode { get { return GetString("LoginRewards.ClaimMode", "Auto"); } }        // Auto, Gump

        public static string Rewards { get { return GetString("LoginRewards.Rewards", "PowerHourToken:1"); } } // base fallback / DailyStreak
        public static string Calendar7 { get { return GetString("LoginRewards.Calendar7", ""); } }
        public static string Milestones { get { return GetString("LoginRewards.Milestones", ""); } }

        public static int ChoiceEveryDays { get { return GetInt("LoginRewards.ChoiceEveryDays", 0); } }
        public static bool ChoiceReplacesBase { get { return GetBool("LoginRewards.ChoiceReplacesBase", true); } }

        // Optional: force extra specific choice days (calendar day in Calendar7; streak day in DailyStreak)
        public static string ChoiceDays { get { return GetString("LoginRewards.ChoiceDays", ""); } }

        public static bool UseGump
        {
            get { return (ClaimMode ?? "Auto").Trim().Equals("Gump", StringComparison.OrdinalIgnoreCase); }
        }

        public static string GetChoicesForDay(int day)
        {
            return GetString("LoginRewards.Choices." + day.ToString(CultureInfo.InvariantCulture), "");
        }

        public static string GetChoicesForEveryN(int n)
        {
            string s = GetString("LoginRewards.Choices.Every" + n.ToString(CultureInfo.InvariantCulture), "");
            if (!string.IsNullOrWhiteSpace(s))
                return s;

            return GetChoicesForDay(n);
        }
    }

    // =========================
    // Login Rewards System
    // =========================
    public static class LoginRewardsSystem
    {
        private static readonly string SavePath = Path.Combine(Core.BaseDirectory, "Saves", "LoginRewards.xml");
        private static readonly Dictionary<string, RewardState> _states = new Dictionary<string, RewardState>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            LoginRewardsCfg.Reload();

            EventSink.Login += OnLogin;
            EventSink.WorldLoad += OnWorldLoad;
            EventSink.WorldSave += OnWorldSave;

            CommandSystem.Register("LoginRewards", AccessLevel.Player, OnLoginRewards);

            // GM tools
            CommandSystem.Register("ReloadLoginRewards", AccessLevel.GameMaster, OnReload);
            CommandSystem.Register("LoginRewardsInfo", AccessLevel.GameMaster, OnInfo);
            CommandSystem.Register("ResetLoginRewardsToday", AccessLevel.GameMaster, OnResetToday);
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
            LoginRewardsCfg.Reload();
            e.Mobile.SendMessage(0x59, "LoginRewards: reloaded Config/LoginRewards.cfg");
        }

        private static void OnInfo(CommandEventArgs e)
        {
            Mobile m = e.Mobile;

            m.SendMessage(0x59, "LoginRewards.Enabled={0}", LoginRewardsCfg.Enabled);
            m.SendMessage(0x59, "LoginRewards.Mode={0}", LoginRewardsCfg.Mode);
            m.SendMessage(0x59, "LoginRewards.ClaimMode={0}", LoginRewardsCfg.ClaimMode);
            m.SendMessage(0x59, "LoginRewards.OncePerAccountPerDay={0}", LoginRewardsCfg.OncePerAccountPerDay);
            m.SendMessage(0x59, "LoginRewards.ChoiceEveryDays={0}", LoginRewardsCfg.ChoiceEveryDays);
            m.SendMessage(0x59, "LoginRewards.ChoiceReplacesBase={0}", LoginRewardsCfg.ChoiceReplacesBase);
        }

        /// <summary>
        /// GM: Reset today's claim flag so the player can claim again immediately.
        /// Usage:
        ///   [ResetLoginRewardsToday
        ///   [ResetLoginRewardsToday <playerName>
        ///   [ResetLoginRewardsToday <playerName> <keepStreak>
        ///
        /// keepStreak:
        ///   true  -> player can claim again today, and after claiming their streak will end up the same as it was before reset
        ///   false -> fully resets claim+streak (fresh start)
        /// </summary>
        private static void OnResetToday(CommandEventArgs e)
        {
            Mobile gm = e.Mobile;

            string name = (e.Length >= 1) ? e.GetString(0) : null;
            bool keepStreak = true;

            if (e.Length >= 2)
            {
                try { keepStreak = e.GetBoolean(1); }
                catch { keepStreak = true; }
            }

            PlayerMobile target = null;

            if (!string.IsNullOrWhiteSpace(name))
                target = FindOnlinePlayerByName(name);
            else
                target = gm as PlayerMobile;

            if (target == null || target.Deleted)
            {
                gm.SendMessage("Target player not found (must be online). Usage: [ResetLoginRewardsToday <playerName> [keepStreak]");
                return;
            }

            string key = GetIdentityKey(target);

            if (string.IsNullOrEmpty(key))
            {
                gm.SendMessage("Could not determine identity key for that player.");
                return;
            }

            RewardState st = GetOrCreateState(key);

            DateTime today = DateTime.UtcNow.Date;

            if (keepStreak)
            {
                // Trick: PredictStreak uses (yesterday) => st.Streak + 1.
                // To allow claiming again today WITHOUT changing the final streak:
                //  - move last claim date to yesterday
                //  - decrement streak by 1 so next claim increments back to original
                st.LastClaimDateUtc = today.AddDays(-1);
                if (st.Streak > 0)
                    st.Streak -= 1;
            }
            else
            {
                st.LastClaimDateUtc = DateTime.MinValue.Date;
                st.Streak = 0;
            }

            gm.SendMessage(0x59, "LoginRewards reset for {0}. keepStreak={1}.", target.Name, keepStreak);
            target.SendMessage(0x59, "Your login rewards claim state was reset by a GM.");
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

        private static void OnLogin(LoginEventArgs e)
        {
            if (!LoginRewardsCfg.Enabled)
                return;

            PlayerMobile pm = e.Mobile as PlayerMobile;

            if (pm == null || pm.Deleted)
                return;

            if (AlreadyClaimedToday(pm))
                return;

            if (LoginRewardsCfg.UseGump)
            {
                ShowClaimGump(pm);
            }
            else
            {
                // Auto mode: if choice day, show gump so player can pick
                RewardState st = GetOrCreateState(GetIdentityKey(pm));
                PreviewRewards preview = BuildPreview(PredictStreak(st, DateTime.UtcNow.Date));

                if (preview.IsChoiceDay)
                    ShowClaimGump(pm);
                else
                    TryClaim(pm, preview, choiceIndex: -1, showMessages: true);
            }
        }

        private static void OnLoginRewards(CommandEventArgs e)
        {
            PlayerMobile pm = e.Mobile as PlayerMobile;

            if (pm == null)
                return;

            if (!LoginRewardsCfg.Enabled)
            {
                pm.SendMessage("Login rewards are disabled.");
                return;
            }

            if (LoginRewardsCfg.UseGump)
            {
                ShowClaimGump(pm);
                return;
            }

            if (AlreadyClaimedToday(pm))
                pm.SendMessage(0x59, "Login rewards already claimed today.");
            else
                pm.SendMessage(0x59, "Login rewards are in Auto mode and will be granted on login.");
        }

        public static bool AlreadyClaimedToday(PlayerMobile pm)
        {
            if (pm == null || pm.Deleted)
                return true;

            string key = GetIdentityKey(pm);
            if (string.IsNullOrEmpty(key))
                return true;

            RewardState st = GetOrCreateState(key);
            return st.LastClaimDateUtc == DateTime.UtcNow.Date;
        }

        public static void ShowClaimGump(PlayerMobile pm)
        {
            if (pm == null || pm.Deleted)
                return;

            pm.CloseGump(typeof(LoginRewardsClaimGump));

            string key = GetIdentityKey(pm);
            RewardState st = GetOrCreateState(key);

            DateTime today = DateTime.UtcNow.Date;
            bool alreadyClaimed = (st.LastClaimDateUtc == today);

            int predictedStreak = PredictStreak(st, today);
            PreviewRewards preview = BuildPreview(predictedStreak);

            pm.SendGump(new LoginRewardsClaimGump(pm, alreadyClaimed, st.Streak, preview));
        }

        public static bool TryClaimChoice(PlayerMobile pm, int choiceIndex, bool showMessages)
        {
            if (pm == null || pm.Deleted)
                return false;

            string key = GetIdentityKey(pm);
            RewardState st = GetOrCreateState(key);

            DateTime today = DateTime.UtcNow.Date;
            int predictedStreak = PredictStreak(st, today);

            PreviewRewards preview = BuildPreview(predictedStreak);

            if (!preview.IsChoiceDay)
            {
                if (showMessages)
                    pm.SendMessage(0x59, "Today's reward does not require a choice.");
                return false;
            }

            if (choiceIndex < 0 || choiceIndex >= preview.Choices.Count)
            {
                if (showMessages)
                    pm.SendMessage(0x59, "Invalid choice.");
                return false;
            }

            return TryClaim(pm, preview, choiceIndex, showMessages);
        }

        private static bool TryClaim(PlayerMobile pm, PreviewRewards preview, int choiceIndex, bool showMessages)
        {
            if (pm == null || pm.Deleted)
                return false;

            if (!LoginRewardsCfg.Enabled)
                return false;

            string key = GetIdentityKey(pm);
            if (string.IsNullOrEmpty(key))
                return false;

            RewardState st = GetOrCreateState(key);

            DateTime today = DateTime.UtcNow.Date;

            if (st.LastClaimDateUtc == today)
            {
                if (showMessages)
                    pm.SendMessage(0x59, "You have already claimed today's login rewards.");
                return false;
            }

            // Apply state
            st.Streak = preview.PredictedStreak;
            st.LastClaimDateUtc = today;
            st.TotalClaims++;

            // Base rewards / choice
            if (preview.IsChoiceDay)
            {
                ChoiceOption chosen = preview.Choices[choiceIndex];

                if (!LoginRewardsCfg.ChoiceReplacesBase)
                    GrantRewardString(pm, preview.BaseRewardsRaw);

                GrantRewardString(pm, chosen.RewardsRaw);

                if (showMessages)
                    pm.SendMessage(0x59, "You claimed: {0}", chosen.Name);
            }
            else
            {
                GrantRewardString(pm, preview.BaseRewardsRaw);
            }

            // Milestone extras
            if (!string.IsNullOrWhiteSpace(preview.MilestoneRewardsRaw))
                GrantRewardString(pm, preview.MilestoneRewardsRaw);

            if (showMessages)
            {
                if (preview.CalendarDayIndex > 0)
                    pm.SendMessage(0x59, "Daily login reward claimed (Calendar Day {0}/7). Streak: {1} day(s).", preview.CalendarDayIndex, preview.PredictedStreak);
                else
                    pm.SendMessage(0x59, "Daily login reward claimed. Streak: {0} day(s).", preview.PredictedStreak);

                if (preview.MilestoneHit > 0)
                    pm.SendMessage(0x59, "Milestone reward claimed for {0}-day streak!", preview.MilestoneHit);
            }

            return true;
        }

        private static int PredictStreak(RewardState st, DateTime todayUtcDate)
        {
            if (st.LastClaimDateUtc == todayUtcDate.AddDays(-1))
                return Math.Max(1, st.Streak + 1);

            return 1;
        }

        private static string GetIdentityKey(PlayerMobile pm)
        {
            if (pm == null)
                return null;

            if (!LoginRewardsCfg.OncePerAccountPerDay)
                return "char:" + pm.Serial.Value.ToString(CultureInfo.InvariantCulture);

            IAccount acc = pm.Account as IAccount;
            if (acc != null && !string.IsNullOrEmpty(acc.Username))
                return "acct:" + acc.Username;

            return "char:" + pm.Serial.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static RewardState GetOrCreateState(string key)
        {
            if (string.IsNullOrEmpty(key))
                key = "unknown";

            RewardState st;
            if (!_states.TryGetValue(key, out st) || st == null)
            {
                st = new RewardState();
                _states[key] = st;
            }

            return st;
        }

        private static PreviewRewards BuildPreview(int predictedStreak)
        {
            PreviewRewards pr = new PreviewRewards();
            pr.PredictedStreak = predictedStreak;

            string mode = (LoginRewardsCfg.Mode ?? "DailyStreak").Trim();

            // Base rewards
            if (mode.Equals("Calendar7", StringComparison.OrdinalIgnoreCase))
            {
                int day = ((predictedStreak - 1) % 7) + 1; // 1..7
                pr.CalendarDayIndex = day;

                string calReward = GetCalendar7Reward(day, LoginRewardsCfg.Calendar7);
                pr.BaseRewardsRaw = !string.IsNullOrWhiteSpace(calReward) ? calReward : LoginRewardsCfg.Rewards;
            }
            else
            {
                pr.CalendarDayIndex = 0;
                pr.BaseRewardsRaw = LoginRewardsCfg.Rewards;
            }

            pr.BaseEntries = ParseEntries(pr.BaseRewardsRaw);

            // Milestones
            pr.MilestoneRewardsRaw = GetMilestoneRewards(predictedStreak, LoginRewardsCfg.Milestones, out pr.MilestoneHit);
            pr.MilestoneEntries = ParseEntries(pr.MilestoneRewardsRaw);

            // Choice logic:
            // - Explicit ChoiceDays (calendar day in Calendar7; streak day in DailyStreak)
            // - ChoiceEveryDays (based on STREAK day always)
            bool isChoice = false;
            int choiceKey = 0;

            HashSet<int> explicitDays = ParseIntSet(LoginRewardsCfg.ChoiceDays);

            if (mode.Equals("Calendar7", StringComparison.OrdinalIgnoreCase))
            {
                if (pr.CalendarDayIndex > 0 && explicitDays.Contains(pr.CalendarDayIndex))
                {
                    isChoice = true;
                    choiceKey = pr.CalendarDayIndex;
                }
            }
            else
            {
                if (explicitDays.Contains(predictedStreak))
                {
                    isChoice = true;
                    choiceKey = predictedStreak;
                }
            }

            int every = LoginRewardsCfg.ChoiceEveryDays;
            if (!isChoice && every > 0 && (predictedStreak % every == 0))
            {
                isChoice = true;
                choiceKey = every; // repeating choice list key (EveryN)
            }

            if (isChoice)
            {
                string rawChoices = (every > 0 && (predictedStreak % every == 0) && choiceKey == every)
                    ? LoginRewardsCfg.GetChoicesForEveryN(every)
                    : LoginRewardsCfg.GetChoicesForDay(choiceKey);

                List<ChoiceOption> opts = ParseChoices(rawChoices);

                if (opts.Count > 0)
                {
                    pr.IsChoiceDay = true;
                    pr.ChoiceDayKey = choiceKey;
                    pr.Choices = opts;
                }
            }

            return pr;
        }

        private static HashSet<int> ParseIntSet(string s)
        {
            HashSet<int> set = new HashSet<int>();

            if (string.IsNullOrWhiteSpace(s))
                return set;

            string[] parts = s.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0)
                    set.Add(n);
            }

            return set;
        }

        private static List<ChoiceOption> ParseChoices(string raw)
        {
            List<ChoiceOption> list = new List<ChoiceOption>();

            if (string.IsNullOrWhiteSpace(raw))
                return list;

            // Options separated by ';'
            string[] parts = raw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0)
                    continue;

                int eq = p.IndexOf('=');
                if (eq <= 0 || eq >= p.Length - 1)
                    continue;

                string name = p.Substring(0, eq).Trim();
                string rewards = p.Substring(eq + 1).Trim();

                if (name.Length == 0 || rewards.Length == 0)
                    continue;

                ChoiceOption opt = new ChoiceOption();
                opt.Name = name;
                opt.RewardsRaw = rewards;
                opt.Entries = ParseEntries(rewards);

                list.Add(opt);
            }

            return list;
        }

        private static string GetCalendar7Reward(int day, string calendar7)
        {
            if (string.IsNullOrWhiteSpace(calendar7))
                return null;

            string[] parts = calendar7.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0)
                    continue;

                int eq = p.IndexOf('=');
                if (eq <= 0)
                    continue;

                string left = p.Substring(0, eq).Trim();
                string right = p.Substring(eq + 1).Trim();

                int d;
                if (!int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out d))
                    continue;

                if (d == day)
                    return right;
            }

            return null;
        }

        private static string GetMilestoneRewards(int streak, string milestones, out int hit)
        {
            hit = 0;

            if (string.IsNullOrWhiteSpace(milestones))
                return null;

            string[] parts = milestones.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0)
                    continue;

                int eq = p.IndexOf('=');
                if (eq <= 0)
                    continue;

                string left = p.Substring(0, eq).Trim();
                string right = p.Substring(eq + 1).Trim();

                int milestone;
                if (!int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out milestone))
                    continue;

                if (milestone == streak)
                {
                    hit = milestone;
                    return right;
                }
            }

            return null;
        }

        private static void GrantRewardString(PlayerMobile pm, string rewards)
        {
            if (pm == null || pm.Deleted || string.IsNullOrWhiteSpace(rewards))
                return;

            string[] entries = rewards.Split(new char[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i].Trim();
                if (entry.Length == 0)
                    continue;

                string key;
                int amount;

                if (!TryParseReward(entry, out key, out amount))
                    continue;

                GrantOne(pm, key, amount);
            }
        }

        private static bool TryParseReward(string entry, out string key, out int amount)
        {
            key = null;
            amount = 1;

            int colon = entry.IndexOf(':');
            if (colon < 0)
            {
                key = entry.Trim();
                amount = 1;
                return key.Length > 0;
            }

            key = entry.Substring(0, colon).Trim();
            string amtStr = entry.Substring(colon + 1).Trim();

            int n;
            if (!int.TryParse(amtStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                n = 1;

            if (n < 1)
                n = 1;

            amount = n;
            return key.Length > 0;
        }

        private static void GrantOne(PlayerMobile pm, string key, int amount)
        {
            if (pm == null || pm.Deleted || amount < 1 || string.IsNullOrEmpty(key))
                return;

            if (key.Equals("Gold", StringComparison.OrdinalIgnoreCase))
            {
                pm.AddToBackpack(new Gold(amount));
                return;
            }

            Type t = ItemTypeCache.Resolve(key);

            if (t == null || !typeof(Item).IsAssignableFrom(t))
            {
                pm.SendMessage("Login reward item type not found: {0}", key);
                return;
            }

            Item test = null;
            try { test = Activator.CreateInstance(t) as Item; }
            catch { test = null; }

            if (test == null)
            {
                pm.SendMessage("Could not create login reward item: {0}", key);
                return;
            }

            if (test.Stackable)
            {
                test.Amount = amount;
                pm.AddToBackpack(test);
                return;
            }

            test.Delete();

            for (int i = 0; i < amount; i++)
            {
                Item item = null;
                try { item = Activator.CreateInstance(t) as Item; }
                catch { item = null; }

                if (item != null)
                    pm.AddToBackpack(item);
                else
                    pm.SendMessage("Could not create login reward item: {0}", key);
            }
        }

        private static List<RewardEntry> ParseEntries(string rewards)
        {
            List<RewardEntry> list = new List<RewardEntry>();

            if (string.IsNullOrWhiteSpace(rewards))
                return list;

            string[] entries = rewards.Split(new char[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i].Trim();
                if (entry.Length == 0)
                    continue;

                string key;
                int amount;

                if (!TryParseReward(entry, out key, out amount))
                    continue;

                list.Add(new RewardEntry(key, amount));
            }

            return list;
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

                foreach (XElement el in root.Elements("state"))
                {
                    XAttribute k = el.Attribute("key");
                    if (k == null || string.IsNullOrEmpty(k.Value))
                        continue;

                    RewardState st = new RewardState();

                    DateTime d;
                    int streak;
                    int total;

                    if (DateTime.TryParse(el.Attribute("last") != null ? el.Attribute("last").Value : "",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out d))
                    {
                        st.LastClaimDateUtc = d.Date;
                    }

                    if (int.TryParse(el.Attribute("streak") != null ? el.Attribute("streak").Value : "0",
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out streak))
                    {
                        st.Streak = streak;
                    }

                    if (int.TryParse(el.Attribute("total") != null ? el.Attribute("total").Value : "0",
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out total))
                    {
                        st.TotalClaims = total;
                    }

                    _states[k.Value] = st;
                }
            }
            catch
            {
                // ignore load errors
            }
        }

        private static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SavePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                XElement root = new XElement("loginRewards");

                foreach (KeyValuePair<string, RewardState> kv in _states)
                {
                    RewardState st = kv.Value;
                    if (st == null)
                        continue;

                    root.Add(new XElement("state",
                        new XAttribute("key", kv.Key),
                        new XAttribute("last", st.LastClaimDateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XAttribute("streak", st.Streak.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("total", st.TotalClaims.ToString(CultureInfo.InvariantCulture))
                    ));
                }

                XDocument doc = new XDocument(root);
                doc.Save(SavePath);
            }
            catch
            {
                // ignore save errors
            }
        }

        private class RewardState
        {
            public DateTime LastClaimDateUtc = DateTime.MinValue.Date;
            public int Streak = 0;
            public int TotalClaims = 0;
        }

        private struct RewardEntry
        {
            public string Key;
            public int Amount;

            public RewardEntry(string key, int amt)
            {
                Key = key;
                Amount = amt;
            }
        }

        private class ChoiceOption
        {
            public string Name;
            public string RewardsRaw;
            public List<RewardEntry> Entries = new List<RewardEntry>();
        }

        private class PreviewRewards
        {
            public int PredictedStreak;

            public int CalendarDayIndex;
            public string BaseRewardsRaw;

            public string MilestoneRewardsRaw;
            public int MilestoneHit;

            public List<RewardEntry> BaseEntries = new List<RewardEntry>();
            public List<RewardEntry> MilestoneEntries = new List<RewardEntry>();

            public bool IsChoiceDay;
            public int ChoiceDayKey;
            public List<ChoiceOption> Choices = new List<ChoiceOption>();
        }

        private class LoginRewardsClaimGump : Gump
        {
            private readonly bool _alreadyClaimed;
            private readonly int _currentStreak;
            private readonly PreviewRewards _preview;

            public LoginRewardsClaimGump(PlayerMobile pm, bool alreadyClaimed, int currentStreak, PreviewRewards preview)
                : base(50, 50)
            {
                _alreadyClaimed = alreadyClaimed;
                _currentStreak = currentStreak;
                _preview = preview ?? new PreviewRewards();

                Closable = true;
                Disposable = true;
                Dragable = true;
                Resizable = false;

                AddPage(0);

                int w = 520;
                int h = 420;

                AddBackground(0, 0, w, h, 9270);
                AddAlphaRegion(10, 10, w - 20, h - 20);

                AddHtml(20, 18, w - 40, 20, Color("<CENTER><B>Daily Login Rewards</B></CENTER>", 0xFFFFFF), false, false);

                int y = 50;

                if (_preview.CalendarDayIndex > 0)
                    AddHtml(20, y, w - 40, 20, Color(string.Format("Calendar Day: {0}/7", _preview.CalendarDayIndex), 0xFFFFFF), false, false);
                else
                    AddHtml(20, y, w - 40, 20, Color("Mode: Daily Streak", 0xFFFFFF), false, false);

                y += 22;

                AddHtml(20, y, w - 40, 20, Color(string.Format("Current Streak: {0} day(s)", _currentStreak), 0xDDDDDD), false, false);
                y += 18;

                if (!_alreadyClaimed)
                    AddHtml(20, y, w - 40, 20, Color(string.Format("Streak After Claim: {0} day(s)", _preview.PredictedStreak), 0xDDDDDD), false, false);
                else
                    AddHtml(20, y, w - 40, 20, Color("Already claimed today.", 0xFF8080), false, false);

                y += 28;

                if (_preview.IsChoiceDay && !_alreadyClaimed)
                {
                    AddHtml(20, y, w - 40, 18, Color("<B>Choose One Reward</B>", 0xFFFFFF), false, false);
                    y += 22;

                    y = DrawChoiceList(20, y, w - 40, 240);
                }
                else
                {
                    AddHtml(20, y, w - 40, 18, Color("<B>Today's Rewards</B>", 0xFFFFFF), false, false);
                    y += 20;

                    y = DrawEntryList(20, y, w - 40, 170, _preview.BaseEntries);

                    if (_preview.MilestoneHit > 0 && _preview.MilestoneEntries.Count > 0)
                    {
                        y += 6;
                        AddHtml(20, y, w - 40, 18, Color(string.Format("<B>Milestone Bonus (Day {0})</B>", _preview.MilestoneHit), 0xFFFFFF), false, false);
                        y += 20;
                        y = DrawEntryList(20, y, w - 40, 80, _preview.MilestoneEntries);
                    }
                }

                y = h - 60;

                if (!_alreadyClaimed && !_preview.IsChoiceDay)
                {
                    AddButton(20, y, 4005, 4007, 1, GumpButtonType.Reply, 0);
                    AddLabel(55, y + 2, 0x34, "Claim Rewards");
                }
                else
                {
                    AddButton(20, y, 4020, 4022, 2, GumpButtonType.Reply, 0);
                    AddLabel(55, y + 2, 0x34, "Close");
                }

                AddHtml(260, y + 2, 240, 40, Color("<RIGHT>Use <B>[LoginRewards</B> to reopen this window.</RIGHT>", 0xBBBBBB), false, false);
            }

            private int DrawChoiceList(int x, int y, int w, int h)
            {
                if (_preview.Choices == null || _preview.Choices.Count == 0)
                {
                    AddHtml(x, y, w, 18, Color("No choices configured for today.", 0xAAAAAA), false, false);
                    return y + 20;
                }

                int optionY = y;
                int buttonBase = 100;

                for (int i = 0; i < _preview.Choices.Count; i++)
                {
                    ChoiceOption opt = _preview.Choices[i];

                    AddHtml(x, optionY, w - 120, 18, Color("<B>" + opt.Name + "</B>", 0xDDDDDD), false, false);
                    AddButton(x + w - 95, optionY - 1, 4005, 4007, buttonBase + i, GumpButtonType.Reply, 0);
                    AddLabel(x + w - 60, optionY + 1, 0x34, "Pick");

                    optionY += 18;

                    int lines = Math.Min(3, opt.Entries != null ? opt.Entries.Count : 0);
                    for (int j = 0; j < lines; j++)
                    {
                        RewardEntry e = opt.Entries[j];
                        AddHtml(x + 15, optionY, w - 20, 18, Color(FriendlyName(e.Key) + " x" + e.Amount, 0xAAAAAA), false, false);
                        optionY += 18;
                    }

                    if (opt.Entries != null && opt.Entries.Count > lines)
                    {
                        AddHtml(x + 15, optionY, w - 20, 18, Color("...and " + (opt.Entries.Count - lines) + " more", 0x888888), false, false);
                        optionY += 18;
                    }

                    optionY += 10;

                    if (optionY > y + h - 30)
                        break;
                }

                if (_preview.MilestoneHit > 0 && _preview.MilestoneEntries.Count > 0)
                {
                    AddHtml(x, optionY, w, 18, Color(string.Format("<B>Milestone Bonus (Day {0})</B>", _preview.MilestoneHit), 0xFFFFFF), false, false);
                    optionY += 20;
                    optionY = DrawEntryList(x, optionY, w, 70, _preview.MilestoneEntries);
                }

                return optionY;
            }

            private int DrawEntryList(int x, int y, int w, int h, List<RewardEntry> entries)
            {
                if (entries == null || entries.Count == 0)
                {
                    AddHtml(x, y, w, 18, Color("No rewards configured.", 0xAAAAAA), false, false);
                    return y + 20;
                }

                int maxLines = Math.Max(1, h / 18);
                int lines = Math.Min(entries.Count, maxLines);

                for (int i = 0; i < lines; i++)
                {
                    RewardEntry e = entries[i];
                    AddHtml(x, y, w, 18, Color(FriendlyName(e.Key) + " x" + e.Amount, 0xDDDDDD), false, false);
                    y += 18;
                }

                if (entries.Count > lines)
                {
                    AddHtml(x, y, w, 18, Color("...and " + (entries.Count - lines) + " more", 0xAAAAAA), false, false);
                    y += 18;
                }

                return y;
            }

            public override void OnResponse(Server.Network.NetState sender, RelayInfo info)
            {
                PlayerMobile pm = sender.Mobile as PlayerMobile;
                if (pm == null || pm.Deleted)
                    return;

                int id = info.ButtonID;

                if (id == 1)
                {
                    // Claim base rewards when not a choice day
                    if (LoginRewardsSystem.TryClaimChoiceOrBase(pm))
                        LoginRewardsSystem.ShowClaimGump(pm);
                    else
                        pm.SendMessage("Unable to claim rewards.");

                    return;
                }

                if (id >= 100 && id < 200)
                {
                    int idx = id - 100;

                    if (LoginRewardsSystem.TryClaimChoice(pm, idx, showMessages: true))
                        LoginRewardsSystem.ShowClaimGump(pm);
                    else
                        pm.SendMessage("Unable to claim that choice.");
                }
            }

            private static string FriendlyName(string key)
            {
                if (string.IsNullOrEmpty(key))
                    return "Unknown";

                if (key.Equals("Gold", StringComparison.OrdinalIgnoreCase))
                    return "Gold";

                return key;
            }

            private static string Color(string text, int rgb)
            {
                return string.Format("<BASEFONT COLOR=\"#{0:X6}\">{1}</BASEFONT>", rgb, text);
            }
        }

        // Button-1 helper: claim base rewards only if today isn't a choice day
        public static bool TryClaimChoiceOrBase(PlayerMobile pm)
        {
            if (pm == null || pm.Deleted)
                return false;

            string key = GetIdentityKey(pm);
            RewardState st = GetOrCreateState(key);

            DateTime today = DateTime.UtcNow.Date;
            int predictedStreak = PredictStreak(st, today);
            PreviewRewards preview = BuildPreview(predictedStreak);

            if (preview.IsChoiceDay)
            {
                pm.SendMessage(0x59, "Please pick one of the choices.");
                return false;
            }

            return TryClaim(pm, preview, -1, showMessages: true);
        }

        private static class ItemTypeCache
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
}
