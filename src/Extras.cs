// Extras.cs - GPS, distances, range log, mesh stats, sound/vibration,
// text size and a simple text screen.
// Part of MeshChatCE for Windows Mobile (.NET Compact Framework 3.5).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MeshChatCE
{
    // ---------------------------------------------------------------
    // Text size shared by every screen
    // ---------------------------------------------------------------
    public class Fonts
    {
        public static float Size = 8F;
        public static string Family = "Tahoma";

        public static void FromSetting(string text)
        {
            if (text == "large") Size = 11F;
            else if (text == "normal") Size = 9F;
            else Size = 8F;
        }

        public static Font Regular()
        {
            return new Font(Family, Size, FontStyle.Regular);
        }

        public static Font Bold()
        {
            return new Font(Family, Size, FontStyle.Bold);
        }

        // smaller text for names, times and signal under the bubbles
        public static Font Small()
        {
            return new Font(Family, Size > 8F ? Size - 1F : 7F, FontStyle.Regular);
        }

        public static Font SmallBold()
        {
            return new Font(Family, Size > 8F ? Size - 1F : 7F, FontStyle.Bold);
        }
    }

    // ---------------------------------------------------------------
    // Color themes for the chat bubbles
    // ---------------------------------------------------------------
    public class Theme
    {
        public static string Name = "light";
        public static Color Back, Mine, MineText, Theirs, TheirsText, Meta, Name1, Day;

        public static void Set(string name)
        {
            Name = name;
            if (name == "dark")
            {
                Back = Color.FromArgb(18, 18, 18);
                Mine = Color.FromArgb(0, 95, 170);
                MineText = Color.White;
                Theirs = Color.FromArgb(58, 58, 58);
                TheirsText = Color.White;
                Meta = Color.FromArgb(150, 150, 150);
                Name1 = Color.FromArgb(120, 190, 255);
                Day = Color.FromArgb(110, 110, 110);
            }
            else if (name == "night")
            {
                // red on black keeps your eyes used to the dark
                Back = Color.Black;
                Mine = Color.FromArgb(110, 0, 0);
                MineText = Color.FromArgb(255, 170, 170);
                Theirs = Color.FromArgb(50, 0, 0);
                TheirsText = Color.FromArgb(230, 120, 120);
                Meta = Color.FromArgb(150, 60, 60);
                Name1 = Color.FromArgb(220, 90, 90);
                Day = Color.FromArgb(120, 40, 40);
            }
            else
            {
                Name = "light";
                Back = Color.White;
                Mine = Color.FromArgb(0, 120, 215);
                MineText = Color.White;
                Theirs = Color.FromArgb(232, 232, 232);
                TheirsText = Color.Black;
                Meta = Color.FromArgb(120, 120, 120);
                Name1 = Color.FromArgb(0, 90, 160);
                Day = Color.FromArgb(150, 150, 150);
            }
        }

        static Theme() { Set("light"); }
    }

    // ---------------------------------------------------------------
    // Distances and directions
    // ---------------------------------------------------------------
    public class Geo
    {
        public static string Units = "mi";

        public static double Distance(double lat1, double lon1, double lat2, double lon2,
                                      out string direction)
        {
            double r = 6371000.0;
            double p1 = lat1 * Math.PI / 180.0, p2 = lat2 * Math.PI / 180.0;
            double dp = p2 - p1;
            double dl = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) +
                       Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
            double meters = 2 * r * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            double y = Math.Sin(dl) * Math.Cos(p2);
            double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
            double bearing = (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;
            string[] names = new string[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            direction = names[((int)((bearing + 22.5) / 45.0)) % 8];
            return meters;
        }

        // compass bearing from point 1 to point 2, degrees (0 = north)
        public static double Bearing(double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Math.PI / 180.0, p2 = lat2 * Math.PI / 180.0;
            double dl = (lon2 - lon1) * Math.PI / 180.0;
            double y = Math.Sin(dl) * Math.Cos(p2);
            double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
            return (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;
        }

        public static string Format(double meters)
        {
            if (Units == "km")
            {
                if (meters < 1000) return ((int)meters) + "m";
                return (meters / 1000.0).ToString("0.0") + "km";
            }
            double feet = meters * 3.28084;
            if (feet < 1000) return ((int)feet) + "ft";
            return (meters / 1609.344).ToString("0.0") + "mi";
        }

        // our position: the GPS if it has a fix, else the Heltec's own
        public static bool MyPosition(Mesh mesh, Gps gps, out double lat, out double lon)
        {
            if (gps != null && gps.HasFix)
            {
                lat = gps.Lat;
                lon = gps.Lon;
                return true;
            }
            Node me = mesh.MyNode();
            if (me != null && !double.IsNaN(me.Lat))
            {
                lat = me.Lat;
                lon = me.Lon;
                return true;
            }
            lat = lon = double.NaN;
            return false;
        }

        // "3.1mi N", or "" if we can't tell
        public static string To(Mesh mesh, Gps gps, Node n)
        {
            double lat, lon;
            if (double.IsNaN(n.Lat) || !MyPosition(mesh, gps, out lat, out lon)) return "";
            string dir;
            double m = Distance(lat, lon, n.Lat, n.Lon, out dir);
            return Format(m) + " " + dir;
        }
    }

    // ---------------------------------------------------------------
    // The Bluetooth GPS (GlobalSat BT-359 on COM5)
    // ---------------------------------------------------------------
    public class Gps
    {
        public string Port = "COM5";
        public int Baud = 4800;
        public int GiveUpTries = 3;
        public bool Share = true;
        public int ShareEverySeconds = 300;
        public Mesh Mesh;

        public volatile bool Wanted = false;
        public string Status = "off";
        public int Sats = 0;
        public double Lat = double.NaN, Lon = double.NaN, Alt = double.NaN;
        public long Utc = 0;
        public DateTime LastFix = DateTime.MinValue;
        public DateTime LastShared = DateTime.MinValue;
        public event MeshEvent Changed;

        bool fix = false;
        bool active = false;
        readonly object ctl = new object();
        string lastReported = "";

        public bool HasFix
        {
            get { return fix && (DateTime.Now - LastFix).TotalSeconds < 10; }
        }

        public string Summary()
        {
            if (!Wanted) return "GPS " + Status;
            if (HasFix) return "GPS " + Sats + " sats";
            if (Status == "searching") return "GPS searching (" + Sats + ")";
            return "GPS " + Status;
        }

        void Fire()
        {
            // only tell the screen when something visible changed
            string now = Summary();
            if (now == lastReported) return;
            lastReported = now;
            MeshEvent e = Changed;
            if (e != null)
            {
                try { e(); }
                catch (Exception) { }
            }
        }

        public void TurnOn()
        {
            lock (ctl)
            {
                Wanted = true;
                Status = "starting";
                if (!active)
                {
                    active = true;
                    Thread t = new Thread(new ThreadStart(Run));
                    t.IsBackground = true;
                    t.Start();
                }
            }
            Fire();
        }

        public void TurnOff(string why)
        {
            Wanted = false;
            fix = false;
            Status = why;
            Fire();
        }

        void Run()
        {
            try { Loop(); }
            catch (Exception) { }
            finally
            {
                lock (ctl) { active = false; }
            }
        }

        void Loop()
        {
            int fails = 0;
            while (Wanted)
            {
                SerialPort sp = null;
                try
                {
                    Status = "connecting";
                    Fire();
                    sp = new SerialPort(Port, Baud);
                    sp.ReadTimeout = 2000;
                    sp.Open();
                    Status = "waiting for data";
                    Fire();
                    DateTime quietSince = DateTime.Now;
                    while (Wanted)
                    {
                        string line = null;
                        try { line = sp.ReadLine(); }
                        catch (TimeoutException) { line = null; }
                        if (line == null)
                        {
                            if ((DateTime.Now - quietSince).TotalSeconds > 30)
                                throw new IOException("no data from GPS");
                            continue;
                        }
                        quietSince = DateTime.Now;
                        fails = 0;
                        line = line.Trim();
                        if (line.StartsWith("$") && ChecksumOk(line))
                        {
                            Handle(line);
                            Status = HasFix ? "fix" : "searching";
                            Fire();
                            MaybeShare();
                        }
                    }
                    sp.Close();                     // switched off by the user
                    return;
                }
                catch (Exception)
                {
                    fix = false;
                    try { if (sp != null && sp.IsOpen) sp.Close(); }
                    catch (Exception) { }
                    if (!Wanted) return;
                    fails++;
                    if (fails >= GiveUpTries)
                    {
                        // stop searching - it upsets the phone's Wi-Fi
                        TurnOff("not found - off");
                        return;
                    }
                    Status = "not found, retry " + (fails + 1) + "/" + GiveUpTries;
                    Fire();
                    for (int i = 0; i < 30 && Wanted; i++) Thread.Sleep(500);
                }
            }
        }

        static bool ChecksumOk(string line)
        {
            int star = line.IndexOf('*');
            if (star < 0 || star + 3 > line.Length) return false;
            int calc = 0;
            for (int i = 1; i < star; i++) calc ^= line[i];
            try
            {
                return calc == Convert.ToInt32(line.Substring(star + 1, 2), 16);
            }
            catch (Exception) { return false; }
        }

        static double Degrees(string value, string hemi)
        {
            if (value.Length == 0) return double.NaN;
            int dot = value.IndexOf('.');
            if (dot < 0) dot = value.Length;
            double deg = double.Parse(value.Substring(0, dot - 2),
                                      System.Globalization.CultureInfo.InvariantCulture);
            double min = double.Parse(value.Substring(dot - 2),
                                      System.Globalization.CultureInfo.InvariantCulture);
            double result = deg + min / 60.0;
            if (hemi == "S" || hemi == "W") result = -result;
            return result;
        }

        void Handle(string line)
        {
            string[] p = line.Substring(0, line.IndexOf('*')).Split(',');
            if (p[0].Length < 6) return;
            string kind = p[0].Substring(3);
            try
            {
                if (kind == "GGA" && p.Length >= 10)
                {
                    Sats = p[7].Length > 0 ? int.Parse(p[7]) : 0;
                    bool ok = p[6].Length > 0 && p[6] != "0";
                    if (ok && p[2].Length > 0 && p[4].Length > 0)
                    {
                        Lat = Degrees(p[2], p[3]);
                        Lon = Degrees(p[4], p[5]);
                        if (p[9].Length > 0)
                            Alt = double.Parse(p[9], System.Globalization.CultureInfo.InvariantCulture);
                        LastFix = DateTime.Now;
                    }
                    fix = ok;
                }
                else if (kind == "RMC" && p.Length >= 10)
                {
                    string hms = p[1], dmy = p[9];
                    if (p[2] == "A" && hms.Length >= 6 && dmy.Length == 6)
                    {
                        DateTime t = new DateTime(2000 + int.Parse(dmy.Substring(4, 2)),
                            int.Parse(dmy.Substring(2, 2)), int.Parse(dmy.Substring(0, 2)),
                            int.Parse(hms.Substring(0, 2)), int.Parse(hms.Substring(2, 2)),
                            int.Parse(hms.Substring(4, 2)));
                        Utc = (long)(t - new DateTime(1970, 1, 1)).TotalSeconds;
                    }
                }
            }
            catch (Exception) { }
        }

        void MaybeShare()
        {
            if (!Share || !HasFix || Utc == 0 || Mesh == null || !Mesh.Connected) return;
            if ((DateTime.Now - LastShared).TotalSeconds < ShareEverySeconds) return;
            try
            {
                Mesh.SendPosition(Lat, Lon, Alt, Utc, Sats);
                LastShared = DateTime.Now;
            }
            catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------
    // Range log - everything the Heltec hears, with our GPS position
    // (same CSV format as the Python version)
    // ---------------------------------------------------------------
    public class RangeLog
    {
        public string File = "";
        public bool Active = false;
        public int Lines = 0;
        public DateTime Started;
        public Gps Gps;
        Dictionary<uint, DateTime> last = new Dictionary<uint, DateTime>();

        public string Start()
        {
            bool isNew = !System.IO.File.Exists(File);
            try
            {
                using (StreamWriter w = new StreamWriter(File, true, Encoding.ASCII))
                {
                    if (isNew)
                        w.WriteLine("time,my_lat,my_lon,gps_fix,from_id,from_name,snr_db," +
                                    "rssi_dbm,hops,distance_m,their_lat,their_lon,port");
                }
            }
            catch (Exception ex)
            {
                return "Could not open the log: " + ex.Message;
            }
            Active = true;
            Lines = 0;
            Started = DateTime.Now;
            last.Clear();
            return "";
        }

        public void Stop()
        {
            Active = false;
        }

        static string F(double v, string fmt)
        {
            if (double.IsNaN(v)) return "";
            return v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Add(Node n, string name, float snr, int rssi, int hops, int port)
        {
            if (!Active) return;
            DateTime prev;
            if (last.TryGetValue(n.Num, out prev) && (DateTime.Now - prev).TotalSeconds < 10)
                return;
            last[n.Num] = DateTime.Now;
            bool fix = Gps != null && Gps.HasFix;
            string dist = "";
            if (fix && !double.IsNaN(n.Lat))
            {
                string dir;
                dist = ((int)Geo.Distance(Gps.Lat, Gps.Lon, n.Lat, n.Lon, out dir)).ToString();
            }
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "," +
                (fix ? F(Gps.Lat, "0.000000") : "") + "," +
                (fix ? F(Gps.Lon, "0.000000") : "") + "," +
                (fix ? "yes" : "no") + "," +
                "!" + n.Num.ToString("x8") + "," +
                name.Replace(',', ' ') + "," +
                F(snr, "0.00") + "," +
                (rssi != 0 ? rssi.ToString() : "") + "," +
                (hops >= 0 ? hops.ToString() : "") + "," +
                dist + "," +
                F(n.Lat, "0.000000") + "," + F(n.Lon, "0.000000") + "," + port;
            try
            {
                using (StreamWriter w = new StreamWriter(File, true, Encoding.ASCII))
                {
                    w.WriteLine(line);
                }
                Lines++;
            }
            catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------
    // Mesh statistics for the last 24 hours (not saved)
    // ---------------------------------------------------------------
    public class HourStats
    {
        public int Packets, Messages;
        public Dictionary<uint, bool> Nodes = new Dictionary<uint, bool>();
        public List<float> ChannelUse = new List<float>();
    }

    public class MeshStats
    {
        public Dictionary<long, HourStats> Hours = new Dictionary<long, HourStats>();
        public Dictionary<uint, int> ByNode = new Dictionary<uint, int>();
        public DateTime Started = DateTime.Now;
        readonly object lk = new object();

        HourStats Hour()
        {
            long h = DateTime.Now.Ticks / TimeSpan.TicksPerHour;
            HourStats rec;
            if (!Hours.TryGetValue(h, out rec))
            {
                rec = new HourStats();
                Hours[h] = rec;
                List<long> old = new List<long>();
                foreach (long k in Hours.Keys) if (k < h - 23) old.Add(k);
                foreach (long k in old) Hours.Remove(k);
            }
            return rec;
        }

        public void Packet(uint from, bool isText)
        {
            lock (lk)
            {
                HourStats rec = Hour();
                rec.Packets++;
                if (isText) rec.Messages++;
                rec.Nodes[from] = true;
                int n;
                ByNode.TryGetValue(from, out n);
                ByNode[from] = n + 1;
            }
        }

        public void ChannelUse(float v)
        {
            if (float.IsNaN(v)) return;
            lock (lk) { Hour().ChannelUse.Add(v); }
        }

        public string Report(Mesh mesh)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Since MeshChatCE started, last 24 h\r\n\r\n");
            sb.Append("Hour   Pkts  Msgs  Nodes  Ch%\r\n");
            int tp = 0, tm = 0;
            List<KeyValuePair<uint, int>> top = new List<KeyValuePair<uint, int>>();
            lock (lk)
            {
                List<long> keys = new List<long>(Hours.Keys);
                keys.Sort();
                keys.Reverse();
                foreach (long h in keys)
                {
                    HourStats r = Hours[h];
                    tp += r.Packets;
                    tm += r.Messages;
                    string ch = "-";
                    if (r.ChannelUse.Count > 0)
                    {
                        float sum = 0;
                        foreach (float v in r.ChannelUse) sum += v;
                        ch = (sum / r.ChannelUse.Count).ToString("0");
                    }
                    DateTime when = new DateTime(h * TimeSpan.TicksPerHour);
                    sb.Append(when.ToString("HH:00") + "  " + Pad(r.Packets, 5) +
                              Pad(r.Messages, 6) + Pad(r.Nodes.Count, 7) + "  " + ch + "\r\n");
                }
                foreach (KeyValuePair<uint, int> kv in ByNode) top.Add(kv);
            }
            if (tp == 0) sb.Append("(nothing heard yet)\r\n");
            sb.Append("\r\nTotal: " + tp + " packets, " + tm + " messages\r\n");
            if (top.Count > 0)
            {
                top.Sort(CompareCount);
                sb.Append("\r\nMost active nodes:\r\n");
                for (int i = 0; i < top.Count && i < 5; i++)
                    sb.Append("  " + mesh.NodeName(top[i].Key) + ": " + top[i].Value +
                              (top[i].Value == 1 ? " packet" : " packets") + "\r\n");
            }
            return sb.ToString();
        }

        static int CompareCount(KeyValuePair<uint, int> a, KeyValuePair<uint, int> b)
        {
            return b.Value.CompareTo(a.Value);
        }

        static string Pad(int n, int width)
        {
            string s = n.ToString();
            while (s.Length < width) s = " " + s;
            return s;
        }
    }

    // ---------------------------------------------------------------
    // Sound and vibration for new messages
    // ---------------------------------------------------------------
    public class Notifier
    {
        public bool Sound = true;
        public bool Vibrate = false;
        public string For = "all";         // "all" or "dm"
        public uint VibrateLed = 1;        // which "LED" is the vibrator (HTC: 1)
        public uint NotifyLed = 0;         // which LED to blink for unread messages
        public int VibrateMs = 400;

        [DllImport("coredll.dll")]
        static extern bool MessageBeep(uint type);

        struct NLED_SETTINGS_INFO
        {
            public uint LedNum;
            public uint OffOnBlink;
            public int TotalCycleTime;
            public int OnTime;
            public int OffTime;
            public int MetaCycleOn;
            public int MetaCycleOff;
        }

        [DllImport("coredll.dll")]
        static extern bool NLedSetDevice(uint deviceId, ref NLED_SETTINGS_INFO info);

        public void Message(ChatMessage m)
        {
            if (For == "dm" && !m.Key.StartsWith("d")) return;
            if (Sound) Beep();
            if (Vibrate) Buzz();
        }

        public void Beep()
        {
            try { MessageBeep(0x40); }
            catch (Exception) { }
        }

        public void Buzz()
        {
            Thread t = new Thread(new ThreadStart(BuzzNow));
            t.IsBackground = true;
            t.Start();
        }

        void BuzzNow()
        {
            try
            {
                SetLed(1);
                Thread.Sleep(VibrateMs);
                SetLed(0);
            }
            catch (Exception) { }
        }

        void SetLed(uint state)
        {
            NLED_SETTINGS_INFO info = new NLED_SETTINGS_INFO();
            info.LedNum = VibrateLed;
            info.OffOnBlink = state;
            NLedSetDevice(2, ref info);          // 2 = NLED_SETTINGS_INFO_ID
        }

        // blink the notification light until messages are read
        public void Blink(bool on)
        {
            try
            {
                NLED_SETTINGS_INFO info = new NLED_SETTINGS_INFO();
                info.LedNum = NotifyLed;
                info.OffOnBlink = on ? 2u : 0u;  // 2 = blink
                info.TotalCycleTime = 2000000;   // microseconds
                info.OnTime = 300000;
                info.OffTime = 1700000;
                NLedSetDevice(2, ref info);
            }
            catch (Exception) { }
        }

        [DllImport("coredll.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        public static void BringToFront(IntPtr window)
        {
            try { SetForegroundWindow(window); }
            catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------
    // Keep the phone from sleeping while MeshChatCE is open
    // ---------------------------------------------------------------
    public class Power
    {
        [DllImport("coredll.dll")]
        static extern void SystemIdleTimerReset();

        [DllImport("coredll.dll")]
        static extern int PowerPolicyNotify(int message, int flags);

        // call regularly: stops the phone suspending
        public static void Poke()
        {
            try { SystemIdleTimerReset(); }
            catch (Exception) { }
        }

        // "unattended mode": keeps running (and on Wi-Fi) with the screen off
        public static void Unattended(bool on)
        {
            try { PowerPolicyNotify(3, on ? 1 : 0); }    // 3 = PPN_UNATTENDEDMODE
            catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------
    // A simple screen that shows text and updates itself
    // ---------------------------------------------------------------
    public delegate string TextSource();

    public class TextForm : Form
    {
        TextSource source;
        TextBox text = new TextBox();
        System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        MainMenu menu = new MainMenu();
        MenuItem backKey = new MenuItem();
        MenuItem actionKey = new MenuItem();
        string last = null;

        public TextForm(string title, TextSource source, int refreshMs,
                        string actionText, EventHandler action)
        {
            this.source = source;
            Text = title;
            MinimizeBox = false;
            text.Multiline = true;
            text.ReadOnly = true;
            text.WordWrap = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.BackColor = Color.White;
            text.Font = Fonts.Regular();
            text.Dock = DockStyle.Fill;
            Controls.Add(text);

            backKey.Text = "Back";
            backKey.Click += new EventHandler(OnBack);
            menu.MenuItems.Add(backKey);
            if (actionText != null && action != null)
            {
                actionKey.Text = actionText;
                actionKey.Click += action;
                actionKey.Click += new EventHandler(OnTick);
                menu.MenuItems.Add(actionKey);
            }
            Menu = menu;

            tick.Interval = refreshMs;
            tick.Tick += new EventHandler(OnTick);
            tick.Enabled = true;
            Closed += new EventHandler(OnClosed);
            OnTick(this, EventArgs.Empty);
        }

        void OnTick(object sender, EventArgs e)
        {
            string s;
            try { s = source(); }
            catch (Exception ex) { s = "Problem: " + ex.Message; }
            if (s != last)
            {
                last = s;
                text.Text = s;
            }
        }

        void OnBack(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        void OnClosed(object sender, EventArgs e)
        {
            tick.Enabled = false;
        }
    }
}

namespace MeshChatCE
{
    // Saves whatever is on the phone's screen as a picture
    // (in a "Screenshots" folder next to MeshChatCE.exe).
    public class Screenshot
    {
        [DllImport("coredll.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("coredll.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("coredll.dll")]
        static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h,
                                  IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

        [DllImport("coredll.dll")]
        static extern int GetSystemMetrics(int index);

        const uint SRCCOPY = 0x00CC0020;

        // Returns the saved file's path. Throws on failure.
        public static string Take(string folder)
        {
            int w = GetSystemMetrics(0);   // SM_CXSCREEN
            int h = GetSystemMetrics(1);   // SM_CYSCREEN
            if (w <= 0 || h <= 0) { w = Screen.PrimaryScreen.Bounds.Width; h = Screen.PrimaryScreen.Bounds.Height; }

            string dir = Path.Combine(folder, "Screenshots");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string name = "MeshChatCE_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr screen = GetDC(IntPtr.Zero);
                    IntPtr dest = g.GetHdc();
                    try { BitBlt(dest, 0, 0, w, h, screen, 0, 0, SRCCOPY); }
                    finally
                    {
                        g.ReleaseHdc(dest);
                        ReleaseDC(IntPtr.Zero, screen);
                    }
                }
                // PNG is small and GitHub-friendly; fall back to BMP if this
                // phone's imaging library can't write PNG.
                string path = Path.Combine(dir, name + ".png");
                try
                {
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                catch
                {
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                    path = Path.Combine(dir, name + ".bmp");
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
                }
                return path;
            }
        }
    }
}
