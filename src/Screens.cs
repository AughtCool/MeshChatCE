// Screens.cs - the Settings screen and the message Search screen.
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
    // Everything stored in MeshChatCE.ini
    // ---------------------------------------------------------------
    public class AppSettings
    {
        public string Host = "192.168.4.250";
        public int Port = 4403;
        public int Hops = 3;
        public string GpsPort = "COM5";
        public int GpsBaud = 4800;
        public bool GpsShare = true;
        public int ShareEvery = 300;
        public string Units = "mi";
        public string AlertFor = "all";
        public uint VibrateLed = 1;
        public uint NotifyLed = 0;
        public List<string> Quick = new List<string>();

        public static readonly string[] DefaultQuick = new string[] {
            "OK", "Yes", "No", "On my way", "Where are you?", "Call me",
            "Testing - can you hear me?", "Copy that, thanks" };

        public void Load(string file)
        {
            Quick.Clear();
            if (File.Exists(file))
            {
                try
                {
                    using (StreamReader r = new StreamReader(file))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (line.Length == 0 || line.StartsWith(";")) continue;
                            int eq = line.IndexOf('=');
                            if (eq < 0) continue;
                            string k = line.Substring(0, eq).Trim().ToLower();
                            string v = line.Substring(eq + 1).Trim();
                            try
                            {
                                if (k == "host") Host = v;
                                else if (k == "port") Port = int.Parse(v);
                                else if (k == "hops") Hops = int.Parse(v);
                                else if (k == "gps_port") GpsPort = v.TrimEnd(':');
                                else if (k == "gps_baud") GpsBaud = int.Parse(v);
                                else if (k == "gps_share") GpsShare = v.ToLower() != "no";
                                else if (k == "gps_share_every") ShareEvery = int.Parse(v);
                                else if (k == "units") Units = v.ToLower() == "km" ? "km" : "mi";
                                else if (k == "sound_for") AlertFor = v.ToLower() == "dm" ? "dm" : "all";
                                else if (k == "vibrate_led") VibrateLed = uint.Parse(v);
                                else if (k == "notify_led") NotifyLed = uint.Parse(v);
                                else if (k == "quick" && v.Length > 0) Quick.Add(v);
                            }
                            catch (Exception) { }
                        }
                    }
                }
                catch (Exception) { }
            }
            if (Quick.Count == 0) Quick.AddRange(DefaultQuick);
        }

        public void Save(string file)
        {
            try
            {
                using (StreamWriter w = new StreamWriter(file, false, Encoding.UTF8))
                {
                    w.WriteLine("; MeshChatCE settings (also editable in Menu > Options > Settings)");
                    w.WriteLine("host=" + Host);
                    w.WriteLine("port=" + Port);
                    w.WriteLine("hops=" + Hops);
                    w.WriteLine("; GPS (Bluetooth COM port) and position sharing");
                    w.WriteLine("gps_port=" + GpsPort);
                    w.WriteLine("gps_baud=" + GpsBaud);
                    w.WriteLine("gps_share=" + (GpsShare ? "yes" : "no"));
                    w.WriteLine("gps_share_every=" + ShareEvery);
                    w.WriteLine("; mi or km");
                    w.WriteLine("units=" + Units);
                    w.WriteLine("; alerts: all or dm (direct messages only)");
                    w.WriteLine("sound_for=" + AlertFor);
                    w.WriteLine("; which phone 'LEDs' are the vibrator and the notification light");
                    w.WriteLine("vibrate_led=" + VibrateLed);
                    w.WriteLine("notify_led=" + NotifyLed);
                    w.WriteLine("; quick replies, one per line");
                    foreach (string q in Quick) w.WriteLine("quick=" + q);
                }
            }
            catch (Exception) { }
        }

        public AppSettings Copy()
        {
            AppSettings c = (AppSettings)MemberwiseClone();
            c.Quick = new List<string>(Quick);
            return c;
        }
    }

    // ---------------------------------------------------------------
    // The Settings screen
    // ---------------------------------------------------------------
    public class SettingsForm : Form
    {
        public AppSettings Result;

        Panel page = new Panel();
        TextBox host = new TextBox(), port = new TextBox(), hops = new TextBox();
        TextBox gpsPort = new TextBox(), shareEvery = new TextBox();
        CheckBox gpsShare = new CheckBox();
        ComboBox units = new ComboBox(), alertFor = new ComboBox();
        TextBox vibrateLed = new TextBox(), notifyLed = new TextBox();
        TextBox quick = new TextBox();
        MainMenu menu = new MainMenu();
        MenuItem saveKey = new MenuItem(), cancelKey = new MenuItem();
        int y = 4;
        int lineH;

        public SettingsForm(AppSettings current)
        {
            Result = current.Copy();
            Text = "Settings";
            MinimizeBox = false;
            lineH = (int)(Fonts.Size * 2) + 4;

            page.Dock = DockStyle.Fill;
            page.AutoScroll = true;
            Controls.Add(page);

            Heading("Heltec (Meshtastic node)");
            Field("IP address", host, Result.Host);
            Field("Port", port, Result.Port.ToString());
            Field("Hop limit (1-7)", hops, Result.Hops.ToString());

            Heading("GPS");
            Field("Bluetooth COM port (e.g. COM5)", gpsPort, Result.GpsPort);
            gpsShare.Text = "Share my position on the mesh";
            gpsShare.Checked = Result.GpsShare;
            Place(gpsShare);
            Field("Share every (seconds)", shareEvery, Result.ShareEvery.ToString());

            Heading("Display and alerts");
            units.DropDownStyle = ComboBoxStyle.DropDownList;
            units.Items.Add("mi");
            units.Items.Add("km");
            units.SelectedIndex = Result.Units == "km" ? 1 : 0;
            Field("Distance units", units, null);
            alertFor.DropDownStyle = ComboBoxStyle.DropDownList;
            alertFor.Items.Add("all messages");
            alertFor.Items.Add("direct messages only");
            alertFor.SelectedIndex = Result.AlertFor == "dm" ? 1 : 0;
            Field("Sound/vibrate for", alertFor, null);
            Field("Vibrator LED number (HTC: 1)", vibrateLed, Result.VibrateLed.ToString());
            Field("Notification LED number", notifyLed, Result.NotifyLed.ToString());

            Heading("Quick replies (one per line)");
            quick.Multiline = true;
            quick.ScrollBars = ScrollBars.Vertical;
            quick.Height = lineH * 5;
            quick.Text = string.Join("\r\n", Result.Quick.ToArray());
            Place(quick);
            y += 8;

            saveKey.Text = "Save";
            saveKey.Click += new EventHandler(OnSave);
            cancelKey.Text = "Cancel";
            cancelKey.Click += new EventHandler(OnCancel);
            menu.MenuItems.Add(saveKey);
            menu.MenuItems.Add(cancelKey);
            Menu = menu;
        }

        int Width4()
        {
            int w = Screen.PrimaryScreen.WorkingArea.Width - 16;
            return w > 40 ? w : 200;
        }

        void Heading(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Fonts.Bold();
            l.Bounds = new Rectangle(4, y, Width4(), lineH);
            page.Controls.Add(l);
            y += lineH;
        }

        void Field(string label, Control box, string value)
        {
            Label l = new Label();
            l.Text = label;
            l.Font = Fonts.Regular();
            l.Bounds = new Rectangle(4, y, Width4(), lineH);
            page.Controls.Add(l);
            y += lineH - 2;
            if (value != null) box.Text = value;
            Place(box);
        }

        void Place(Control c)
        {
            c.Font = Fonts.Regular();
            int h = lineH + 2;
            TextBox tb = c as TextBox;
            if (tb != null && tb.Multiline) h = tb.Height;
            c.Bounds = new Rectangle(4, y, Width4(), h);
            page.Controls.Add(c);
            y += h + 4;
        }

        static int Number(TextBox box, int min, int max, string name)
        {
            int v;
            try { v = int.Parse(box.Text.Trim()); }
            catch (Exception) { throw new FormatException(name + " must be a number"); }
            if (v < min || v > max)
                throw new FormatException(name + " must be " + min + " to " + max);
            return v;
        }

        void OnSave(object sender, EventArgs e)
        {
            try
            {
                string h = host.Text.Trim();
                try { System.Net.IPAddress.Parse(h); }
                catch (Exception) { throw new FormatException("IP address looks wrong (e.g. 192.168.4.250)"); }
                Result.Host = h;
                Result.Port = Number(port, 1, 65535, "Port");
                Result.Hops = Number(hops, 1, 7, "Hop limit");
                string gp = gpsPort.Text.Trim().ToUpper().TrimEnd(':');
                if (!gp.StartsWith("COM")) throw new FormatException("GPS port should look like COM5");
                Result.GpsPort = gp;
                Result.GpsShare = gpsShare.Checked;
                Result.ShareEvery = Number(shareEvery, 30, 3600, "Share every");
                Result.Units = units.SelectedIndex == 1 ? "km" : "mi";
                Result.AlertFor = alertFor.SelectedIndex == 1 ? "dm" : "all";
                Result.VibrateLed = (uint)Number(vibrateLed, 0, 9, "Vibrator LED");
                Result.NotifyLed = (uint)Number(notifyLed, 0, 9, "Notification LED");
                List<string> q = new List<string>();
                foreach (string line in quick.Text.Split('\n'))
                {
                    string t = Mesh.Clean(line).Trim();
                    if (t.Length > 0) q.Add(t);
                }
                if (q.Count == 0) q.AddRange(AppSettings.DefaultQuick);
                Result.Quick = q;
            }
            catch (FormatException ex)
            {
                MessageBox.Show(ex.Message, "Settings");
                return;
            }
            DialogResult = DialogResult.OK;
        }

        void OnCancel(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }
    }

    // ---------------------------------------------------------------
    // The Search screen - looks through the whole saved history
    // ---------------------------------------------------------------
    public class SearchForm : Form
    {
        Mesh mesh;
        public string OpenKey = null;     // set when the user opens a result

        TextBox query = new TextBox();
        Label info = new Label();
        ListBox results = new ListBox();
        List<string> resultKeys = new List<string>();
        MainMenu menu = new MainMenu();
        MenuItem findKey = new MenuItem(), menuKey = new MenuItem();
        MenuItem openItem = new MenuItem(), backItem = new MenuItem();

        public SearchForm(Mesh mesh)
        {
            this.mesh = mesh;
            Text = "Search";
            MinimizeBox = false;
            int lineH = (int)(Fonts.Size * 2) + 4;

            query.Dock = DockStyle.Top;
            query.Font = Fonts.Regular();
            query.KeyPress += new KeyPressEventHandler(OnQueryKey);
            info.Dock = DockStyle.Top;
            info.Height = lineH;
            info.Font = Fonts.Regular();
            info.Text = "Type a word and press Enter or Find.";
            results.Dock = DockStyle.Fill;
            results.Font = Fonts.Regular();
            results.KeyPress += new KeyPressEventHandler(OnResultKey);

            Controls.Add(results);
            Controls.Add(info);
            Controls.Add(query);
            results.BringToFront();

            findKey.Text = "Find";
            findKey.Click += new EventHandler(OnFind);
            menuKey.Text = "Menu";
            openItem.Text = "Open conversation";
            openItem.Click += new EventHandler(OnOpen);
            backItem.Text = "Back";
            backItem.Click += new EventHandler(OnBack);
            menuKey.MenuItems.Add(openItem);
            menuKey.MenuItems.Add(backItem);
            menu.MenuItems.Add(findKey);
            menu.MenuItems.Add(menuKey);
            Menu = menu;
            query.Focus();
        }

        void OnQueryKey(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                e.Handled = true;
                OnFind(sender, EventArgs.Empty);
            }
        }

        void OnResultKey(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                e.Handled = true;
                OnOpen(sender, EventArgs.Empty);
            }
        }

        void OnFind(object sender, EventArgs e)
        {
            string q = query.Text.Trim().ToLower();
            results.Items.Clear();
            resultKeys.Clear();
            if (q.Length == 0) return;
            string[] lines;
            try
            {
                using (StreamReader r = new StreamReader(mesh.HistoryFile, Encoding.UTF8))
                {
                    lines = r.ReadToEnd().Split('\n');
                }
            }
            catch (Exception)
            {
                info.Text = "No history yet.";
                return;
            }
            int found = 0;
            {
                for (int i = lines.Length - 1; i >= 0; i--)        // newest first
                {
                    string[] p = Mesh.SplitTabs(lines[i].TrimEnd('\r'), 5);
                    if (p == null || p[4].ToLower().IndexOf(q) < 0) continue;
                    found++;
                    if (found > 60) continue;                       // count, don't list
                    try
                    {
                        DateTime t = Mesh.LocalTime(double.Parse(p[0]));
                        string who = p[2] == "1" ? "me" : mesh.NodeName(uint.Parse(p[3]));
                        results.Items.Add(t.ToString("MM/dd HH:mm") + " " +
                                          mesh.Title(p[1]) + " " + who + ": " + p[4]);
                        resultKeys.Add(p[1]);
                    }
                    catch (Exception) { found--; }
                }
            }
            info.Text = found == 0 ? "Nothing found." :
                found + " found" + (found > 60 ? ", showing newest 60" : "") +
                " - select one, Menu > Open";
            if (results.Items.Count > 0) results.SelectedIndex = 0;
        }

        void OnOpen(object sender, EventArgs e)
        {
            int i = results.SelectedIndex;
            if (i < 0 || i >= resultKeys.Count) return;
            OpenKey = resultKeys[i];
            DialogResult = DialogResult.OK;
        }

        void OnBack(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }
    }

    // ---------------------------------------------------------------
    // Rename my node (long + short name)
    // ---------------------------------------------------------------
    public class RenameForm : Form
    {
        public string LongName, ShortName;
        TextBox longBox = new TextBox(), shortBox = new TextBox();
        MainMenu menu = new MainMenu();
        MenuItem saveKey = new MenuItem(), cancelKey = new MenuItem();

        public RenameForm(string currentLong, string currentShort)
        {
            Text = "Rename my node";
            MinimizeBox = false;
            int lineH = (int)(Fonts.Size * 2) + 4;
            int w = Screen.PrimaryScreen.WorkingArea.Width - 16;
            if (w < 100) w = 200;
            int y = 4;

            Label l1 = new Label();
            l1.Text = "Long name (up to 39 letters)";
            l1.Font = Fonts.Regular();
            l1.Bounds = new Rectangle(4, y, w, lineH);
            Controls.Add(l1);
            y += lineH;
            longBox.Font = Fonts.Regular();
            longBox.Text = currentLong;
            longBox.Bounds = new Rectangle(4, y, w, lineH + 2);
            Controls.Add(longBox);
            y += lineH + 8;

            Label l2 = new Label();
            l2.Text = "Short name (up to 4 letters)";
            l2.Font = Fonts.Regular();
            l2.Bounds = new Rectangle(4, y, w, lineH);
            Controls.Add(l2);
            y += lineH;
            shortBox.Font = Fonts.Regular();
            shortBox.Text = currentShort;
            shortBox.MaxLength = 4;
            shortBox.Bounds = new Rectangle(4, y, w / 2, lineH + 2);
            Controls.Add(shortBox);
            y += lineH + 8;

            Label hint = new Label();
            hint.Text = "The short name shows in chats, the node list and the map. " +
                        "Your new name is sent to the mesh straight away.";
            hint.Font = Fonts.Small();
            hint.Bounds = new Rectangle(4, y, w, lineH * 3);
            Controls.Add(hint);

            saveKey.Text = "Save";
            saveKey.Click += new EventHandler(OnSave);
            cancelKey.Text = "Cancel";
            cancelKey.Click += new EventHandler(OnCancel);
            menu.MenuItems.Add(saveKey);
            menu.MenuItems.Add(cancelKey);
            Menu = menu;
            longBox.Focus();
        }

        void OnSave(object sender, EventArgs e)
        {
            string ln = Mesh.Clean(longBox.Text).Trim();
            string sn = Mesh.Clean(shortBox.Text).Trim();
            if (ln.Length == 0 || sn.Length == 0)
            {
                MessageBox.Show("Please fill in both names.", "Rename my node");
                return;
            }
            if (Encoding.UTF8.GetByteCount(ln) > 39)
            {
                MessageBox.Show("The long name is too long (39 letters max).", "Rename my node");
                return;
            }
            if (Encoding.UTF8.GetByteCount(sn) > 4)
            {
                MessageBox.Show("The short name is too long (4 letters max).", "Rename my node");
                return;
            }
            LongName = ln;
            ShortName = sn;
            DialogResult = DialogResult.OK;
        }

        void OnCancel(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }
    }
}
