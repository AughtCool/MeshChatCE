// NodesForm.cs - the Nodes screen, the Traceroutes screen and saved settings.
// Part of MeshChatCE for Windows Mobile (.NET Compact Framework 3.5).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MeshChatCE
{
    // ---------------------------------------------------------------
    // meshchat_settings.txt - shared with the Python version.
    // Lines of key=value; favorites are "favs=beef,a11" (hex node IDs).
    // ---------------------------------------------------------------
    public class Prefs
    {
        string file;
        List<string> keys = new List<string>();
        Dictionary<string, string> values = new Dictionary<string, string>();

        public Prefs(string file)
        {
            this.file = file;
            Load();
        }

        void Load()
        {
            keys.Clear();
            values.Clear();
            if (!File.Exists(file)) return;
            try
            {
                using (StreamReader r = new StreamReader(file))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim();
                        string v = line.Substring(eq + 1).Trim();
                        if (!values.ContainsKey(k)) keys.Add(k);
                        values[k] = v;
                    }
                }
            }
            catch (Exception) { }
        }

        void Save()
        {
            try
            {
                using (StreamWriter w = new StreamWriter(file, false, Encoding.ASCII))
                {
                    foreach (string k in keys) w.WriteLine(k + "=" + values[k]);
                }
            }
            catch (Exception) { }
        }

        public string Get(string key, string dflt)
        {
            string v;
            if (values.TryGetValue(key, out v)) return v;
            return dflt;
        }

        public void Set(string key, string value)
        {
            if (!values.ContainsKey(key)) keys.Add(key);
            values[key] = value;
            Save();
        }

        public List<uint> Favorites()
        {
            List<uint> favs = new List<uint>();
            foreach (string part in Get("favs", "").Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                try { favs.Add(Convert.ToUInt32(p, 16)); }
                catch (Exception) { }
            }
            return favs;
        }

        public bool IsFavorite(uint num)
        {
            return Favorites().Contains(num);
        }

        public void ToggleFavorite(uint num)
        {
            List<uint> favs = Favorites();
            if (favs.Contains(num)) favs.Remove(num);
            else favs.Add(num);
            StringBuilder sb = new StringBuilder();
            foreach (uint f in favs)
            {
                if (sb.Length > 0) sb.Append(",");
                sb.Append(f.ToString("x"));
            }
            Set("favs", sb.ToString());
        }
    }

    // small text helpers shared by the screens
    public class Fmt
    {
        public static string Ago(long epoch)
        {
            if (epoch <= 0) return "?";
            long d = Mesh.NowEpoch() - epoch;
            if (d < 0 || d > 365L * 86400) return "?";
            if (d < 60) return "now";
            if (d < 3600) return (d / 60) + "m";
            if (d < 86400) return (d / 3600) + "h";
            return (d / 86400) + "d";
        }

        public static string Snr(float snr)
        {
            return float.IsNaN(snr) ? "" : snr.ToString("0.0");
        }

        public static string Hops(int hops)
        {
            if (hops < 0) return "";
            if (hops == 0) return "dir";
            return hops.ToString();
        }

        public static string Battery(int bat)
        {
            if (bat < 0) return "";
            if (bat > 100) return "plug";
            return bat + "%";
        }

        // traceroute SNR values are dB x 4, -128 means unknown
        public static string TraceSnr(List<int> values, int i)
        {
            if (i < values.Count && values[i] != -128)
                return (values[i] / 4.0).ToString("0.0") + " dB";
            return "? dB";
        }
    }

    // ---------------------------------------------------------------
    // The Nodes screen
    // ---------------------------------------------------------------
    public class NodesForm : Form
    {
        Mesh mesh;
        Prefs prefs;
        Gps gps;
        public uint DmTarget = 0;       // set when the user picks "Direct message"

        ListView list = new ListView();
        Label header = new Label();
        Timer tick = new Timer();
        List<uint> rowNums = new List<uint>();
        string lastSignature = null;

        MainMenu menu = new MainMenu();
        MenuItem backKey = new MenuItem();
        MenuItem actKey = new MenuItem();
        MenuItem dmItem = new MenuItem();
        MenuItem traceItem = new MenuItem();
        MenuItem favItem = new MenuItem();
        MenuItem detailsItem = new MenuItem();
        MenuItem tracesItem = new MenuItem();
        MenuItem swapItem = new MenuItem();
        int sortCol = -1;                 // -1 = favorites, then most recently heard
        bool sortReverse = false;
        static readonly string[] ColumnNames = new string[] {
            "name", "distance", "last heard", "signal", "hops", "battery" };

        public NodesForm(Mesh mesh, Prefs prefs, Gps gps)
        {
            this.mesh = mesh;
            this.prefs = prefs;
            this.gps = gps;
            Text = "Nodes";
            MinimizeBox = false;
            Font f = Fonts.Regular();

            header.Dock = DockStyle.Top;
            header.Height = 16;
            header.Font = f;

            list.View = View.Details;
            list.FullRowSelect = true;
            list.Font = f;
            list.Dock = DockStyle.Fill;
            list.Columns.Add("Name", 68, HorizontalAlignment.Left);
            list.Columns.Add("Dist", 56, HorizontalAlignment.Left);
            list.Columns.Add("Heard", 38, HorizontalAlignment.Left);
            list.Columns.Add("SNR", 36, HorizontalAlignment.Left);
            list.Columns.Add("Hops", 32, HorizontalAlignment.Left);
            list.Columns.Add("Bat", 38, HorizontalAlignment.Left);
            list.ItemActivate += new EventHandler(OnActivate);
            list.ColumnClick += new ColumnClickEventHandler(OnColumnClick);

            Controls.Add(list);
            Controls.Add(header);
            list.BringToFront();

            backKey.Text = "Back";
            backKey.Click += new EventHandler(OnBack);
            actKey.Text = "Actions";
            dmItem.Text = "Direct message";
            dmItem.Click += new EventHandler(OnDm);
            traceItem.Text = "Traceroute";
            traceItem.Click += new EventHandler(OnTrace);
            favItem.Text = "Favorite on/off";
            favItem.Click += new EventHandler(OnFavorite);
            detailsItem.Text = "Details";
            detailsItem.Click += new EventHandler(OnDetails);
            tracesItem.Text = "Show traceroutes";
            tracesItem.Click += new EventHandler(OnShowTraces);
            actKey.MenuItems.Add(dmItem);
            actKey.MenuItems.Add(traceItem);
            actKey.MenuItems.Add(favItem);
            swapItem.Text = "Swap info (names + keys)";
            swapItem.Click += new EventHandler(OnSwap);
            actKey.MenuItems.Add(detailsItem);
            actKey.MenuItems.Add(swapItem);
            actKey.MenuItems.Add(tracesItem);
            menu.MenuItems.Add(backKey);
            menu.MenuItems.Add(actKey);
            Menu = menu;

            tick.Interval = 5000;
            tick.Tick += new EventHandler(OnTick);
            tick.Enabled = true;
            Closed += new EventHandler(OnClosed);
            Fill();
        }

        void OnTick(object sender, EventArgs e)
        {
            Fill();
        }

        void Fill()
        {
            List<uint> favs = prefs.Favorites();
            List<Node> nodes = mesh.NodeList();

            // favorites first, then most recently heard
            List<Node> sorted = new List<Node>();
            foreach (Node n in nodes)
            {
                int at = 0;
                while (at < sorted.Count && Before(sorted[at], n, favs)) at++;
                sorted.Insert(at, n);
            }

            List<string[]> rows = new List<string[]>();
            StringBuilder sig = new StringBuilder();
            foreach (Node n in sorted)
            {
                string name = (favs.Contains(n.Num) ? "*" : "") + mesh.NodeName(n.Num);
                string[] row = new string[] {
                    name, Geo.To(mesh, gps, n), Fmt.Ago(n.Heard), Fmt.Snr(n.Snr),
                    Fmt.Hops(n.Hops), Fmt.Battery(n.Battery) };
                rows.Add(row);
                sig.Append(string.Join("|", row));
                sig.Append("\n");
            }

            double myLat, myLon;
            string where = Geo.MyPosition(mesh, gps, out myLat, out myLon)
                ? (gps != null && gps.HasFix ? "you: GPS" : "you: Heltec position")
                : "your position unknown";
            string by = sortCol >= 0
                ? ", by " + ColumnNames[sortCol] + (sortReverse ? " (reversed)" : "")
                : "";
            header.Text = sorted.Count + " nodes" + by + ", * = favorite, " + where;
            if (sig.ToString() == lastSignature) return;
            lastSignature = sig.ToString();

            uint selected = SelectedNode();
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                rowNums.Clear();
                for (int i = 0; i < rows.Count; i++)
                {
                    ListViewItem item = new ListViewItem(rows[i][0]);
                    for (int c = 1; c < rows[i].Length; c++)
                        item.SubItems.Add(rows[i][c]);
                    list.Items.Add(item);
                    rowNums.Add(sorted[i].Num);
                }
                int keep = rowNums.IndexOf(selected);
                if (keep < 0 && rowNums.Count > 0) keep = 0;
                if (keep >= 0) list.Items[keep].Selected = true;
            }
            finally
            {
                list.EndUpdate();
            }
        }

        // true if a should be listed before b
        bool Before(Node a, Node b, List<uint> favs)
        {
            bool fa = favs.Contains(a.Num), fb = favs.Contains(b.Num);
            if (fa != fb) return fa;                  // favorites always on top
            bool result;
            switch (sortCol)
            {
                case 0:
                    result = string.Compare(mesh.NodeName(a.Num), mesh.NodeName(b.Num), true) <= 0;
                    break;
                case 1:
                    result = DistanceOf(a) <= DistanceOf(b);          // nearest first
                    break;
                case 3:
                    result = SnrOf(a) >= SnrOf(b);                    // strongest first
                    break;
                case 4:
                    result = (a.Hops < 0 ? 99 : a.Hops) <= (b.Hops < 0 ? 99 : b.Hops);
                    break;
                case 5:
                    result = a.Battery >= b.Battery;                  // fullest first
                    break;
                default:
                    result = a.Heard >= b.Heard;                      // most recent first
                    break;
            }
            return sortReverse ? !result : result;
        }

        double DistanceOf(Node n)
        {
            double lat, lon;
            if (double.IsNaN(n.Lat) || !Geo.MyPosition(mesh, gps, out lat, out lon))
                return double.MaxValue;
            string dir;
            return Geo.Distance(lat, lon, n.Lat, n.Lon, out dir);
        }

        static float SnrOf(Node n)
        {
            return float.IsNaN(n.Snr) ? -1000F : n.Snr;
        }

        // tap a column heading to sort by it; tap again to reverse
        void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (sortCol == e.Column) sortReverse = !sortReverse;
            else
            {
                sortCol = e.Column;
                sortReverse = false;
            }
            lastSignature = null;
            Fill();
        }

        void OnSwap(object sender, EventArgs e)
        {
            uint n = NeedNode();
            if (n == 0) return;
            string why = mesh.SendMyInfo(n, true);
            MessageBox.Show(why.Length > 0 ? why :
                "Sent your name and key to " + mesh.NodeName(n) +
                ", and asked for theirs. Their reply usually arrives within a minute.",
                "Swap info");
        }

        uint SelectedNode()
        {
            if (list.SelectedIndices.Count == 0) return 0;
            int i = list.SelectedIndices[0];
            if (i < 0 || i >= rowNums.Count) return 0;
            return rowNums[i];
        }

        uint NeedNode()
        {
            uint n = SelectedNode();
            if (n == 0) MessageBox.Show("Select a node first.", "Nodes");
            return n;
        }

        void OnActivate(object sender, EventArgs e)
        {
            OnDetails(sender, e);
        }

        void OnDm(object sender, EventArgs e)
        {
            uint n = NeedNode();
            if (n == 0) return;
            DmTarget = n;
            DialogResult = DialogResult.OK;
        }

        void OnTrace(object sender, EventArgs e)
        {
            uint n = NeedNode();
            if (n == 0) return;
            string why = mesh.SendTrace(n);
            if (why.Length > 0)
            {
                MessageBox.Show(why, "Traceroute");
                return;
            }
            OnShowTraces(sender, e);
        }

        void OnShowTraces(object sender, EventArgs e)
        {
            TraceForm t = new TraceForm(mesh);
            t.ShowDialog();
        }

        void OnFavorite(object sender, EventArgs e)
        {
            uint n = NeedNode();
            if (n == 0) return;
            prefs.ToggleFavorite(n);
            lastSignature = null;
            Fill();
        }

        void OnDetails(object sender, EventArgs e)
        {
            uint num = NeedNode();
            if (num == 0) return;
            Node n = null;
            foreach (Node x in mesh.NodeList()) if (x.Num == num) n = x;
            if (n == null) return;
            MessageBox.Show(DetailsText(mesh, prefs, gps, n), "Node details");
        }

        // shared with the map screen
        public static string DetailsText(Mesh mesh, Prefs prefs, Gps gps, Node n)
        {
            uint num = n.Num;
            StringBuilder sb = new StringBuilder();
            sb.Append(mesh.NodeName(num));
            string longName = mesh.LongName(num);
            if (longName.Length > 0) sb.Append(" - " + longName);
            sb.Append("\r\nID: !" + num.ToString("x8"));
            sb.Append("\r\nHeard: " + Fmt.Ago(n.Heard));
            if (!float.IsNaN(n.Snr)) sb.Append("\r\nSNR: " + Fmt.Snr(n.Snr) + " dB");
            if (n.Hops >= 0)
                sb.Append("\r\nHops away: " + (n.Hops == 0 ? "direct" : n.Hops.ToString()));
            if (n.Battery >= 0) sb.Append("\r\nBattery: " + Fmt.Battery(n.Battery));
            if (!float.IsNaN(n.Voltage)) sb.Append("\r\nVoltage: " + n.Voltage.ToString("0.00") + " V");
            sb.Append("\r\nChannel: " + mesh.ChannelName(n.Channel));
            if (!double.IsNaN(n.Lat))
            {
                sb.Append("\r\nPosition: " + n.Lat.ToString("0.0000") + ", " + n.Lon.ToString("0.0000"));
                string d = Geo.To(mesh, gps, n);
                if (d.Length > 0) sb.Append("\r\nDistance: " + d);
            }
            if (prefs.IsFavorite(num)) sb.Append("\r\n* Favorite");
            return sb.ToString();
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

    // ---------------------------------------------------------------
    // The Traceroutes screen (updates itself while waiting)
    // ---------------------------------------------------------------
    public class TraceForm : Form
    {
        Mesh mesh;
        TextBox text = new TextBox();
        Timer tick = new Timer();
        MainMenu menu = new MainMenu();
        MenuItem backKey = new MenuItem();
        string last = null;

        public TraceForm(Mesh mesh)
        {
            this.mesh = mesh;
            Text = "Traceroutes";
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
            Menu = menu;

            tick.Interval = 2000;
            tick.Tick += new EventHandler(OnTick);
            tick.Enabled = true;
            Closed += new EventHandler(OnClosed);
            Fill();
        }

        void OnTick(object sender, EventArgs e)
        {
            Fill();
        }

        string RouteLine(string start, List<uint> hops, string end, List<int> snrs)
        {
            StringBuilder sb = new StringBuilder(start);
            List<string> names = new List<string>();
            foreach (uint h in hops) names.Add(mesh.NodeName(h));
            names.Add(end);
            for (int i = 0; i < names.Count; i++)
            {
                sb.Append(" > ");
                sb.Append(names[i]);
                if (snrs.Count > 0)
                {
                    sb.Append(" (");
                    sb.Append(Fmt.TraceSnr(snrs, i));
                    sb.Append(")");
                }
            }
            return sb.ToString();
        }

        void Fill()
        {
            StringBuilder sb = new StringBuilder();
            List<Trace> traces = new List<Trace>();
            lock (mesh.Lock)
            {
                mesh.ExpireTraces();
                traces.AddRange(mesh.Traces);
            }
            if (traces.Count == 0)
                sb.Append("No traceroutes yet.\r\nIn Nodes, select a node and choose Actions > Traceroute.\r\n");
            string me = mesh.NodeName(mesh.MyNum);
            foreach (Trace t in traces)
            {
                string target = mesh.NodeName(t.Target);
                sb.Append(target + "  " + t.Sent.ToString("HH:mm") + "\r\n");
                if (t.Status == "waiting")
                {
                    sb.Append("  waiting for answer... (" +
                              (int)(DateTime.Now - t.Sent).TotalSeconds + " s)\r\n");
                }
                else if (t.Status == "done")
                {
                    if (t.Route.Count == 0) sb.Append("  direct, no relays\r\n");
                    sb.Append("  Out: " + RouteLine(me, t.Route, target, t.SnrTo) + "\r\n");
                    if (t.Back.Count > 0 || t.SnrBack.Count > 0)
                        sb.Append("  Back: " + RouteLine(target, t.Back, me, t.SnrBack) + "\r\n");
                    sb.Append("  " + t.Route.Count + (t.Route.Count == 1 ? " relay" : " relays") +
                              ", answered in " + (int)(t.Answered - t.Sent).TotalSeconds + " s\r\n");
                }
                else
                {
                    sb.Append("  " + t.Status + "\r\n");
                }
                sb.Append("\r\n");
            }
            sb.Append("Wait " + Mesh.TraceGapSeconds + " s between traceroutes.\r\n");
            string s = sb.ToString();
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
