// MainForm.cs - the MeshChatCE main screen.
// Part of MeshChatCE for Windows Mobile (.NET Compact Framework 3.5).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.WindowsCE.Forms;

namespace MeshChatCE
{
    public class MainForm : Form
    {
        public const string Version = "0.7.1";

        Mesh mesh = new Mesh();
        Gps gps = new Gps();
        RangeLog range = new RangeLog();
        Notifier notifier = new Notifier();
        AppSettings settings = new AppSettings();
        Prefs prefs;
        string iniFile;

        Label status = new Label();
        ComboBox convo = new ComboBox();
        TextBox messages = new TextBox();       // "Classic" layout
        ChatView chat;                          // "Bubbles" layout
        Panel bottom = new Panel();
        TextBox input = new TextBox();
        Button send = new Button();
        Timer tick = new Timer();
        Timer awakeTick = new Timer();

        Notification bubble = null;
        Icon appIcon = null;
        bool mainActive = true;
        int dialogDepth = 0;
        int unseen = 0;                  // messages that arrived while away
        string lastFrom = "";

        MainMenu menu = new MainMenu();
        MenuItem sendKey = new MenuItem();
        MenuItem menuKey = new MenuItem();
        MenuItem quickMenu = new MenuItem();
        MenuItem nodesItem = new MenuItem();
        MenuItem mapItem = new MenuItem();
        MenuItem dmItem = new MenuItem();
        MenuItem searchItem = new MenuItem();
        MenuItem gpsItem = new MenuItem();
        MenuItem toolsMenu = new MenuItem();
        MenuItem tracesItem = new MenuItem();
        MenuItem rangeItem = new MenuItem();
        MenuItem statsItem = new MenuItem();
        MenuItem optionsMenu = new MenuItem();
        MenuItem settingsItem = new MenuItem();
        MenuItem sizeMenu = new MenuItem();
        MenuItem sizeSmall = new MenuItem();
        MenuItem sizeNormal = new MenuItem();
        MenuItem sizeLarge = new MenuItem();
        MenuItem soundItem = new MenuItem();
        MenuItem vibrateItem = new MenuItem();
        MenuItem bubbleItem = new MenuItem();
        MenuItem ledItem = new MenuItem();
        MenuItem awakeItem = new MenuItem();
        MenuItem testItem = new MenuItem();
        MenuItem renameItem = new MenuItem();
        MenuItem sendInfoItem = new MenuItem();
        MenuItem shotItem = new MenuItem();
        Timer shotTimer = new Timer();
        MenuItem layoutMenu = new MenuItem();
        MenuItem layoutBubbles = new MenuItem();
        MenuItem layoutClassic = new MenuItem();
        MenuItem themeMenu = new MenuItem();
        MenuItem themeLight = new MenuItem();
        MenuItem themeDark = new MenuItem();
        MenuItem themeNight = new MenuItem();
        MenuItem fontMenu = new MenuItem();
        MenuItem fontTahoma = new MenuItem();
        MenuItem fontCourier = new MenuItem();
        MenuItem infoItem = new MenuItem();
        MenuItem exitItem = new MenuItem();

        bool refreshPending = false;
        readonly object refreshLock = new object();
        string lastShown = null;
        string lastLabels = null;
        List<string> convKeys = new List<string>();
        bool fillingList = false;

        public MainForm()
        {
            Text = "MeshChatCE";
            MinimizeBox = false;          // the OK button closes the app

            LoadSettings();               // before building the screen (text size)
            LoadIcon();

            // status line (top)
            status.Dock = DockStyle.Top;
            status.ForeColor = Color.DarkRed;
            status.Text = "Starting...";

            // conversation picker (under the status)
            convo.Dock = DockStyle.Top;
            convo.DropDownStyle = ComboBoxStyle.DropDownList;
            convo.SelectedIndexChanged += new EventHandler(OnConvoChanged);

            // message list (fills the middle)
            messages.Multiline = true;
            messages.ReadOnly = true;
            messages.WordWrap = true;
            messages.ScrollBars = ScrollBars.Vertical;
            messages.BackColor = Color.White;
            messages.Dock = DockStyle.Fill;

            chat = new ChatView(mesh);
            chat.Dock = DockStyle.Fill;
            chat.FooterForMine = new MessageInfo(MineFooter);
            chat.Tapped += new MessageTap(OnMessageTapped);

            // typing box + Send button (bottom)
            bottom.Dock = DockStyle.Bottom;
            send.Text = "Send";
            send.Dock = DockStyle.Right;
            send.Width = 48;
            send.Click += new EventHandler(OnSend);
            input.Dock = DockStyle.Fill;
            input.MaxLength = 200;
            input.KeyPress += new KeyPressEventHandler(OnInputKey);
            bottom.Controls.Add(input);
            bottom.Controls.Add(send);
            input.BringToFront();

            Controls.Add(messages);
            Controls.Add(chat);
            Controls.Add(convo);
            Controls.Add(status);
            Controls.Add(bottom);
            ApplyLayout();                // shows bubbles or classic
            ApplyTextSize();

            BuildMenu();

            // refresh every 15 s, so "sending..." can time out on screen
            tick.Interval = 15000;
            tick.Tick += new EventHandler(RefreshScreen);
            tick.Enabled = true;

            // keep-awake heartbeat
            awakeTick.Interval = 20000;
            awakeTick.Tick += new EventHandler(OnAwakeTick);
            ApplyKeepAwake();

            gps.Mesh = mesh;
            range.Gps = gps;
            mesh.Range = range;
            mesh.LoadHistory();
            mesh.Changed += new MeshEvent(OnMeshChanged);
            gps.Changed += new MeshEvent(OnMeshChanged);
            mesh.Incoming += new MessageEvent(OnIncoming);
            Activated += new EventHandler(OnActivated);
            Deactivate += new EventHandler(OnDeactivate);
            Closed += new EventHandler(OnClosed);
            RefreshScreen(this, EventArgs.Empty);
            mesh.Start();
            input.Focus();
        }

        // -------- menus --------
        void BuildMenu()
        {
            sendKey.Text = "Send";
            sendKey.Click += new EventHandler(OnSend);
            menuKey.Text = "Menu";

            quickMenu.Text = "Quick reply";
            FillQuickMenu();

            nodesItem.Text = "Nodes";
            nodesItem.Click += new EventHandler(OnNodes);
            mapItem.Text = "Map";
            mapItem.Click += new EventHandler(OnMap);
            dmItem.Text = "Direct message...";
            dmItem.Click += new EventHandler(OnNewDm);
            searchItem.Text = "Search messages";
            searchItem.Click += new EventHandler(OnSearch);
            gpsItem.Text = "GPS";
            gpsItem.Click += new EventHandler(OnGps);

            toolsMenu.Text = "Tools";
            tracesItem.Text = "Traceroutes";
            tracesItem.Click += new EventHandler(OnTraces);
            rangeItem.Text = "Range log";
            rangeItem.Click += new EventHandler(OnRange);
            statsItem.Text = "Mesh stats";
            statsItem.Click += new EventHandler(OnStats);
            sendInfoItem.Text = "Send my info to the mesh";
            sendInfoItem.Click += new EventHandler(OnSendInfo);
            toolsMenu.MenuItems.Add(sendInfoItem);
            toolsMenu.MenuItems.Add(tracesItem);
            toolsMenu.MenuItems.Add(rangeItem);
            toolsMenu.MenuItems.Add(statsItem);
            shotItem.Text = "Screenshot (in 5 seconds)";
            shotItem.Click += new EventHandler(OnScreenshot);
            toolsMenu.MenuItems.Add(shotItem);
            shotTimer.Interval = 5000;
            shotTimer.Enabled = false;
            shotTimer.Tick += new EventHandler(OnShotTimer);

            optionsMenu.Text = "Options";
            settingsItem.Text = "Settings...";
            settingsItem.Click += new EventHandler(OnSettings);
            sizeMenu.Text = "Text size";
            sizeSmall.Text = "Small";
            sizeNormal.Text = "Normal";
            sizeLarge.Text = "Large";
            sizeSmall.Click += new EventHandler(OnSize);
            sizeNormal.Click += new EventHandler(OnSize);
            sizeLarge.Click += new EventHandler(OnSize);
            sizeMenu.MenuItems.Add(sizeSmall);
            sizeMenu.MenuItems.Add(sizeNormal);
            sizeMenu.MenuItems.Add(sizeLarge);
            soundItem.Text = "Sound";
            soundItem.Click += new EventHandler(OnSound);
            vibrateItem.Text = "Vibrate";
            vibrateItem.Click += new EventHandler(OnVibrate);
            bubbleItem.Text = "Pop-up when in background";
            bubbleItem.Click += new EventHandler(OnBubbleOption);
            ledItem.Text = "Blink light for unread";
            ledItem.Click += new EventHandler(OnLedOption);
            awakeItem.Text = "Keep awake while open";
            awakeItem.Click += new EventHandler(OnAwakeOption);
            testItem.Text = "Test alert";
            testItem.Click += new EventHandler(OnTestAlert);
            layoutMenu.Text = "Message layout";
            layoutBubbles.Text = "Bubbles";
            layoutClassic.Text = "Classic list";
            layoutBubbles.Click += new EventHandler(OnLayout);
            layoutClassic.Click += new EventHandler(OnLayout);
            layoutMenu.MenuItems.Add(layoutBubbles);
            layoutMenu.MenuItems.Add(layoutClassic);
            themeMenu.Text = "Colors";
            themeLight.Text = "Light";
            themeDark.Text = "Dark";
            themeNight.Text = "Night (red)";
            themeLight.Click += new EventHandler(OnTheme);
            themeDark.Click += new EventHandler(OnTheme);
            themeNight.Click += new EventHandler(OnTheme);
            themeMenu.MenuItems.Add(themeLight);
            themeMenu.MenuItems.Add(themeDark);
            themeMenu.MenuItems.Add(themeNight);
            fontMenu.Text = "Font";
            fontTahoma.Text = "Tahoma";
            fontCourier.Text = "Courier New";
            fontTahoma.Click += new EventHandler(OnFont);
            fontCourier.Click += new EventHandler(OnFont);
            fontMenu.MenuItems.Add(fontTahoma);
            fontMenu.MenuItems.Add(fontCourier);
            renameItem.Text = "Rename my node...";
            renameItem.Click += new EventHandler(OnRename);
            optionsMenu.MenuItems.Add(settingsItem);
            optionsMenu.MenuItems.Add(renameItem);
            optionsMenu.MenuItems.Add(layoutMenu);
            optionsMenu.MenuItems.Add(themeMenu);
            optionsMenu.MenuItems.Add(fontMenu);
            optionsMenu.MenuItems.Add(sizeMenu);
            optionsMenu.MenuItems.Add(soundItem);
            optionsMenu.MenuItems.Add(vibrateItem);
            optionsMenu.MenuItems.Add(bubbleItem);
            optionsMenu.MenuItems.Add(ledItem);
            optionsMenu.MenuItems.Add(awakeItem);
            optionsMenu.MenuItems.Add(testItem);

            infoItem.Text = "Connection info";
            infoItem.Click += new EventHandler(OnInfo);
            exitItem.Text = "Exit";
            exitItem.Click += new EventHandler(OnExit);

            menuKey.MenuItems.Add(quickMenu);
            menuKey.MenuItems.Add(nodesItem);
            menuKey.MenuItems.Add(mapItem);
            menuKey.MenuItems.Add(dmItem);
            menuKey.MenuItems.Add(searchItem);
            menuKey.MenuItems.Add(gpsItem);
            menuKey.MenuItems.Add(toolsMenu);
            menuKey.MenuItems.Add(optionsMenu);
            menuKey.MenuItems.Add(infoItem);
            menuKey.MenuItems.Add(exitItem);
            menu.MenuItems.Add(sendKey);
            menu.MenuItems.Add(menuKey);
            Menu = menu;
            UpdateChecks();
        }

        void FillQuickMenu()
        {
            quickMenu.MenuItems.Clear();
            foreach (string q in settings.Quick)
            {
                MenuItem qi = new MenuItem();
                qi.Text = q;
                qi.Click += new EventHandler(OnQuick);
                quickMenu.MenuItems.Add(qi);
            }
        }

        bool Pref(string key, string dflt)
        {
            return prefs.Get(key, dflt) == "on";
        }

        void UpdateChecks()
        {
            string size = prefs.Get("text", "small");
            sizeSmall.Checked = size == "small";
            sizeNormal.Checked = size == "normal";
            sizeLarge.Checked = size == "large";
            soundItem.Checked = notifier.Sound;
            vibrateItem.Checked = notifier.Vibrate;
            bubbleItem.Checked = Pref("bubble", "on");
            ledItem.Checked = Pref("led", "on");
            awakeItem.Checked = Pref("awake", "off");
            bool bubbles = prefs.Get("layout", "bubbles") != "classic";
            layoutBubbles.Checked = bubbles;
            layoutClassic.Checked = !bubbles;
            themeLight.Checked = Theme.Name == "light";
            themeDark.Checked = Theme.Name == "dark";
            themeNight.Checked = Theme.Name == "night";
            fontTahoma.Checked = Fonts.Family == "Tahoma";
            fontCourier.Checked = Fonts.Family == "Courier New";
            gpsItem.Checked = gps.Wanted;
        }

        void ApplyTextSize()
        {
            int lineHeight = (int)(Fonts.Size * 2) + 2;
            status.Font = Fonts.Regular();
            status.Height = lineHeight;
            convo.Font = Fonts.Bold();
            messages.Font = Fonts.Regular();
            input.Font = Fonts.Regular();
            bottom.Height = lineHeight + 8;
            lastShown = null;
            if (chat != null) chat.FontsChanged();
        }

        // shows another screen; while it is open we count as "in front"
        DialogResult ShowChild(Form f)
        {
            dialogDepth++;
            try { return f.ShowDialog(); }
            finally { dialogDepth--; }
        }

        // -------- settings --------
        // MeshChatCE.ini (next to MeshChatCE.exe): connection, GPS, alerts, quick replies.
        // meshchat_settings.txt (shared with the Python version): text size,
        // sound, vibrate, pop-up, light, keep awake, favorites.
        static string AppFolder()
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly()
                         .GetName().CodeBase;
            if (exe.StartsWith("file:///")) exe = exe.Substring(8);
            return Path.GetDirectoryName(exe);
        }

        void LoadSettings()
        {
            string folder = AppFolder();
            iniFile = Path.Combine(folder, "MeshChatCE.ini");
            string oldIni = Path.Combine(folder, "MeshChat.ini");
            mesh.HistoryFile = Path.Combine(folder, "meshchat_history.txt");
            range.File = Path.Combine(folder, "meshchat_range.csv");
            prefs = new Prefs(Path.Combine(folder, "meshchat_settings.txt"));

            bool haveIni = File.Exists(iniFile);
            settings.Load(haveIni ? iniFile : oldIni);
            if (!haveIni) settings.Save(iniFile);
            ApplySettings();

            Fonts.FromSetting(prefs.Get("text", "small"));
            Fonts.Family = prefs.Get("font", "Tahoma");
            Theme.Set(prefs.Get("theme", "light"));
            notifier.Sound = Pref("sound", "on");
            notifier.Vibrate = Pref("vibrate", "off");
        }

        // copy settings into the parts that use them
        void ApplySettings()
        {
            mesh.Host = settings.Host;
            mesh.Port = settings.Port;
            mesh.HopLimit = settings.Hops;
            gps.Port = settings.GpsPort;
            gps.Baud = settings.GpsBaud;
            gps.Share = settings.GpsShare;
            gps.ShareEverySeconds = settings.ShareEvery;
            Geo.Units = settings.Units;
            notifier.For = settings.AlertFor;
            notifier.VibrateLed = settings.VibrateLed;
            notifier.NotifyLed = settings.NotifyLed;
        }

        void OnSettings(object sender, EventArgs e)
        {
            SettingsForm f = new SettingsForm(settings);
            if (ShowChild(f) == DialogResult.OK)
            {
                bool moved = f.Result.Host != settings.Host || f.Result.Port != settings.Port;
                bool gpsMoved = f.Result.GpsPort != settings.GpsPort;
                settings = f.Result;
                settings.Save(iniFile);
                ApplySettings();
                FillQuickMenu();
                if (moved) mesh.Reconnect();
                if (gpsMoved && gps.Wanted)
                {
                    gps.TurnOff("off");
                    MessageBox.Show("GPS port changed - turn the GPS on again (Menu > GPS).",
                                    "Settings");
                }
                lastShown = null;
                UpdateChecks();
                RefreshScreen(this, EventArgs.Empty);
            }
            input.Focus();
        }

        // -------- the app's icon (for pop-ups) --------
        void LoadIcon()
        {
            try
            {
                Stream s = System.Reflection.Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("MeshChatCE.ico");
                if (s != null) appIcon = new Icon(s, 16, 16);
            }
            catch (Exception) { appIcon = null; }
        }

        // -------- in front / in the background --------
        bool InFront()
        {
            return mainActive || dialogDepth > 0;
        }

        void OnActivated(object sender, EventArgs e)
        {
            mainActive = true;
            unseen = 0;
            notifier.Blink(false);
            HideBubble();
        }

        void OnDeactivate(object sender, EventArgs e)
        {
            mainActive = false;
        }

        List<ChatMessage> awayQueue = new List<ChatMessage>();

        // a message arrived (background thread)
        void OnIncoming(ChatMessage m)
        {
            notifier.Message(m);
            if (!InFront())
            {
                lock (awayQueue) { awayQueue.Add(m); }
                try
                {
                    Invoke(new EventHandler(OnAwayQueued), new object[] { this, EventArgs.Empty });
                }
                catch (Exception) { }
            }
        }

        // runs on the screen's thread
        void OnAwayQueued(object sender, EventArgs e)
        {
            List<ChatMessage> list;
            lock (awayQueue)
            {
                list = new List<ChatMessage>(awayQueue);
                awayQueue.Clear();
            }
            foreach (ChatMessage m in list) ShowAway(m);
        }

        void ShowAway(ChatMessage m)
        {
            if (InFront()) return;
            unseen++;
            lastFrom = mesh.Title(m.Key) + " - " + (m.Mine ? "me" : mesh.NodeName(m.From));
            if (Pref("led", "on")) notifier.Blink(true);
            if (Pref("bubble", "on")) ShowBubble(m);
        }

        static string Html(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        void ShowBubble(ChatMessage m)
        {
            try
            {
                if (bubble == null)
                {
                    bubble = new Notification();
                    bubble.ResponseSubmitted += new ResponseSubmittedEventHandler(OnBubbleTap);
                }
                bubble.Caption = "MeshChatCE";
                if (appIcon != null) bubble.Icon = appIcon;
                string more = unseen > 1 ? "<br>(" + unseen + " new messages)" : "";
                bubble.Text = "<html><body><b>" + Html(lastFrom) + "</b><br>" +
                              Html(m.Text) + more +
                              "<br><br><a href=\"open\">Open MeshChatCE</a>" +
                              "</body></html>";
                bubble.InitialDuration = 10;
                bubble.Visible = true;
            }
            catch (Exception) { }
        }

        void HideBubble()
        {
            try { if (bubble != null) bubble.Visible = false; }
            catch (Exception) { }
        }

        void OnBubbleTap(object sender, ResponseSubmittedEventArgs e)
        {
            HideBubble();
            Show();
            BringToFront();
            Notifier.BringToFront(Handle);
        }

        // -------- keep awake --------
        void ApplyKeepAwake()
        {
            bool on = Pref("awake", "off");
            awakeTick.Enabled = on;
            Power.Unattended(on);
            if (on) Power.Poke();
        }

        void OnAwakeTick(object sender, EventArgs e)
        {
            Power.Poke();
        }

        // -------- updates from the background connection --------
        void OnMeshChanged()
        {
            // only queue one screen refresh at a time
            lock (refreshLock)
            {
                if (refreshPending) return;
                refreshPending = true;
            }
            try
            {
                Invoke(new EventHandler(RefreshScreen),
                       new object[] { this, EventArgs.Empty });
            }
            catch (Exception)
            {
                lock (refreshLock) { refreshPending = false; }
            }
        }

        void RefreshScreen(object sender, EventArgs e)
        {
            lock (refreshLock) { refreshPending = false; }

            string gpsText = gps.Wanted || gps.Status != "off" ? " | " + gps.Summary() : "";
            if (mesh.Connected)
            {
                status.ForeColor = Color.DarkGreen;
                status.Text = "Connected - " + mesh.NodeName(mesh.MyNum) + gpsText;
            }
            else
            {
                status.ForeColor = Color.DarkRed;
                status.Text = mesh.Status + gpsText;
            }
            gpsItem.Checked = gps.Wanted;

            mesh.MarkRead(mesh.Current);
            FillConversationList();
            ShowMessages();
        }

        // the drop-down list of channels and DMs, with unread counts
        void FillConversationList()
        {
            List<string> keys = new List<string>();
            foreach (int ch in mesh.ChannelList()) keys.Add("c" + ch);
            foreach (string dm in mesh.DmKeys()) keys.Add(dm);
            if (!keys.Contains(mesh.Current)) keys.Insert(0, mesh.Current);

            List<string> labels = new List<string>();
            StringBuilder all = new StringBuilder();
            foreach (string k in keys)
            {
                string label = mesh.Title(k);
                int n = mesh.UnreadCount(k);
                if (n > 0) label += " (" + n + ")";
                labels.Add(label);
                all.Append(label);
                all.Append("|");
            }
            string signature = all.ToString() + mesh.Current;
            if (signature == lastLabels) return;      // nothing changed
            lastLabels = signature;

            fillingList = true;
            try
            {
                convo.Items.Clear();
                foreach (string label in labels) convo.Items.Add(label);
                convKeys = keys;
                convo.SelectedIndex = keys.IndexOf(mesh.Current);
            }
            finally
            {
                fillingList = false;
            }
        }

        void ShowMessages()
        {
            string key = mesh.Current;
            StringBuilder sb = new StringBuilder();
            List<ChatMessage> shown = new List<ChatMessage>();
            lock (mesh.Lock)
            {
                for (int i = 0; i < mesh.Messages.Count; i++)
                {
                    if (mesh.Messages[i].Key == key) shown.Add(mesh.Messages[i]);
                }
            }
            int start = shown.Count > 60 ? shown.Count - 60 : 0;
            for (int i = start; i < shown.Count; i++)
            {
                ChatMessage m = shown[i];
                string who = m.Mine ? "me" : mesh.NodeName(m.From);
                sb.Append(m.Time.ToString("HH:mm"));
                sb.Append(" ");
                sb.Append(who);
                sb.Append(": ");
                sb.Append(m.Text);
                string extra = m.Mine ? StatusText(m) : m.Signal;
                if (extra.Length > 0)
                {
                    sb.Append("  [");
                    sb.Append(extra);
                    sb.Append("]");
                }
                sb.Append("\r\n");
            }
            if (shown.Count == 0) sb.Append("No messages yet.\r\n");

            string text = sb.ToString();
            if (text != lastShown)
            {
                lastShown = text;
                if (UseBubbles())
                {
                    chat.SetMessages(shown.GetRange(start, shown.Count - start));
                }
                else
                {
                    messages.Text = text;
                    messages.SelectionStart = text.Length;   // jump to newest
                    messages.ScrollToCaret();
                }
            }
        }

        // -------- message layout, colors, font --------
        bool UseBubbles()
        {
            return prefs.Get("layout", "bubbles") != "classic";
        }

        void ApplyLayout()
        {
            bool bubbles = UseBubbles();
            chat.OpenAtBottom();
            chat.Visible = bubbles;
            messages.Visible = !bubbles;
            if (bubbles) chat.BringToFront(); else messages.BringToFront();
            lastShown = null;
        }

        string MineFooter(ChatMessage m)
        {
            string st = StatusText(m);
            return m.Time.ToString("HH:mm") + (st.Length > 0 ? "  " + st : "");
        }

        void OnMessageTapped(ChatMessage m)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(m.Mine ? "Sent by me" : "From " + mesh.NodeName(m.From) +
                      " (!" + m.From.ToString("x8") + ")");
            sb.Append("\r\nIn: " + mesh.Title(m.Key));
            sb.Append("\r\nTime: " + m.Time.ToString("ddd MMM d, HH:mm:ss"));
            if (m.Mine)
            {
                string st = StatusText(m);
                if (st.Length > 0) sb.Append("\r\nStatus: " + st);
            }
            else if (m.Signal.Length > 0)
            {
                sb.Append("\r\nSignal: " + m.Signal);
            }
            sb.Append("\r\n\r\n" + m.Text);
            MessageBox.Show(sb.ToString(), "Message");
        }

        void OnLayout(object sender, EventArgs e)
        {
            prefs.Set("layout", sender == layoutClassic ? "classic" : "bubbles");
            ApplyLayout();
            UpdateChecks();
            RefreshScreen(this, EventArgs.Empty);
        }

        void OnTheme(object sender, EventArgs e)
        {
            string t = sender == themeDark ? "dark" : (sender == themeNight ? "night" : "light");
            prefs.Set("theme", t);
            Theme.Set(t);
            UpdateChecks();
            chat.Invalidate();
        }

        void OnFont(object sender, EventArgs e)
        {
            string f = sender == fontCourier ? "Courier New" : "Tahoma";
            prefs.Set("font", f);
            Fonts.Family = f;
            ApplyTextSize();
            lastLabels = null;
            UpdateChecks();
            RefreshScreen(this, EventArgs.Empty);
        }

        static string StatusText(ChatMessage m)
        {
            if (m.Status == "sending..." &&
                (DateTime.Now - m.Time).TotalSeconds > 120)
                return "sent, no confirmation";
            return m.Status;
        }

        void OnConvoChanged(object sender, EventArgs e)
        {
            if (fillingList) return;
            int i = convo.SelectedIndex;
            if (i < 0 || i >= convKeys.Count) return;
            OpenConversation(convKeys[i]);
        }

        void OpenConversation(string key)
        {
            mesh.Current = key;
            chat.OpenAtBottom();          // new conversation: show the newest
            lastShown = null;
            lastLabels = null;
            RefreshScreen(this, EventArgs.Empty);
            input.Focus();
        }

        // -------- nodes, map, DMs, search, traceroutes --------
        void OnNodes(object sender, EventArgs e)
        {
            NodesForm f = new NodesForm(mesh, prefs, gps);
            if (ShowChild(f) == DialogResult.OK && f.DmTarget != 0)
                OpenConversation("d" + f.DmTarget);
            RefreshScreen(this, EventArgs.Empty);
            input.Focus();
        }

        void OnMap(object sender, EventArgs e)
        {
            ShowChild(new MapForm(mesh, prefs, gps));
            input.Focus();
        }

        void OnNewDm(object sender, EventArgs e)
        {
            List<uint> nodes = mesh.KnownNodes();
            if (nodes.Count == 0)
            {
                MessageBox.Show("No other nodes known yet.", "Direct message");
                return;
            }
            DmPicker picker = new DmPicker(mesh, nodes);
            if (ShowChild(picker) == DialogResult.OK && picker.Chosen != 0)
                OpenConversation("d" + picker.Chosen);
            input.Focus();
        }

        void OnSearch(object sender, EventArgs e)
        {
            SearchForm f = new SearchForm(mesh);
            if (ShowChild(f) == DialogResult.OK && f.OpenKey != null)
                OpenConversation(f.OpenKey);
            input.Focus();
        }

        void OnTraces(object sender, EventArgs e)
        {
            ShowChild(new TraceForm(mesh));
            input.Focus();
        }

        // -------- naming and introducing ourselves --------
        void OnRename(object sender, EventArgs e)
        {
            if (!mesh.HaveMyInfo)
            {
                MessageBox.Show("Connect to the Heltec first.", "Rename my node");
                return;
            }
            RenameForm f = new RenameForm(mesh.MyLongName(), mesh.NodeName(mesh.MyNum));
            if (ShowChild(f) == DialogResult.OK)
            {
                Cursor.Current = Cursors.WaitCursor;
                string why = mesh.RenameMyNode(f.LongName, f.ShortName);
                Cursor.Current = Cursors.Default;
                MessageBox.Show(why.Length > 0 ? why :
                    "Renamed to " + f.ShortName + " (" + f.LongName + ").\r\n" +
                    "Other nodes will show the new name as soon as they hear it.",
                    "Rename my node");
                lastLabels = null;
                lastShown = null;
                RefreshScreen(this, EventArgs.Empty);
            }
            input.Focus();
        }

        void OnSendInfo(object sender, EventArgs e)
        {
            string why = mesh.SendMyInfo(Mesh.BROADCAST, false);
            MessageBox.Show(why.Length > 0 ? why :
                "Sent your name and key to the mesh. This helps when direct " +
                "messages fail with a key error.", "Send my info");
            input.Focus();
        }

        // -------- screenshots --------
        // Waits 5 seconds so the menu closes and you can switch to another
        // screen (Nodes, Map...), then saves the whole screen as a picture.
        void OnScreenshot(object sender, EventArgs e)
        {
            shotTimer.Enabled = false;
            shotTimer.Enabled = true;
            status.Text = "Screenshot in 5 seconds...";
            input.Focus();
        }

        void OnShotTimer(object sender, EventArgs e)
        {
            shotTimer.Enabled = false;
            string msg;
            try
            {
                string path = Screenshot.Take(AppFolder());
                notifier.Beep();
                msg = "Saved " + Path.GetFileName(path) + "\r\nin the Screenshots folder next to MeshChatCE.exe.";
            }
            catch (Exception ex)
            {
                msg = "Couldn't save the screenshot: " + ex.Message;
            }
            MessageBox.Show(msg, "Screenshot");
        }

        // -------- GPS, range log, stats --------
        void OnGps(object sender, EventArgs e)
        {
            if (gps.Wanted) gps.TurnOff("off");
            else gps.TurnOn();
            UpdateChecks();
            RefreshScreen(this, EventArgs.Empty);
        }

        void OnRange(object sender, EventArgs e)
        {
            ShowChild(new TextForm("Range log", new TextSource(RangeText), 3000,
                                   "Start/Stop", new EventHandler(OnRangeToggle)));
            input.Focus();
        }

        void OnRangeToggle(object sender, EventArgs e)
        {
            if (range.Active)
            {
                range.Stop();
            }
            else
            {
                string why = range.Start();
                if (why.Length > 0) MessageBox.Show(why, "Range log");
            }
        }

        string RangeText()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Records every node the Heltec hears, with your GPS position and " +
                      "the signal, so you can map coverage as you move.\r\n\r\n");
            if (range.Active)
            {
                TimeSpan t = DateTime.Now - range.Started;
                sb.Append("LOGGING for " + (int)t.TotalMinutes + " min, " +
                          range.Lines + " lines\r\n");
                sb.Append("Tap Start/Stop to stop.\r\n");
            }
            else
            {
                sb.Append("Stopped. Tap Start/Stop to start.\r\n");
            }
            sb.Append("\r\n");
            if (!gps.Wanted) sb.Append("GPS is off - lines will have no position. " +
                                       "Turn it on in the Menu.\r\n");
            else sb.Append(gps.Summary() + "\r\n");
            sb.Append("\r\nFile: " + range.File + "\r\n");
            sb.Append("Open it in Excel or Google Sheets, or a map site that reads CSV.\r\n");
            return sb.ToString();
        }

        void OnStats(object sender, EventArgs e)
        {
            ShowChild(new TextForm("Mesh stats", new TextSource(StatsText), 10000, null, null));
            input.Focus();
        }

        string StatsText()
        {
            return mesh.Stats.Report(mesh);
        }

        // -------- options --------
        void OnSize(object sender, EventArgs e)
        {
            string size = sender == sizeLarge ? "large" : (sender == sizeNormal ? "normal" : "small");
            prefs.Set("text", size);
            Fonts.FromSetting(size);
            ApplyTextSize();
            lastLabels = null;
            UpdateChecks();
            RefreshScreen(this, EventArgs.Empty);
        }

        void Toggle(string key, string dflt)
        {
            prefs.Set(key, Pref(key, dflt) ? "off" : "on");
            UpdateChecks();
        }

        void OnSound(object sender, EventArgs e)
        {
            notifier.Sound = !notifier.Sound;
            prefs.Set("sound", notifier.Sound ? "on" : "off");
            UpdateChecks();
        }

        void OnVibrate(object sender, EventArgs e)
        {
            notifier.Vibrate = !notifier.Vibrate;
            prefs.Set("vibrate", notifier.Vibrate ? "on" : "off");
            UpdateChecks();
        }

        void OnBubbleOption(object sender, EventArgs e) { Toggle("bubble", "on"); }

        void OnLedOption(object sender, EventArgs e)
        {
            Toggle("led", "on");
            if (!Pref("led", "on")) notifier.Blink(false);
        }

        void OnAwakeOption(object sender, EventArgs e)
        {
            Toggle("awake", "off");
            ApplyKeepAwake();
            if (Pref("awake", "off"))
                MessageBox.Show("MeshChatCE will keep the phone (and Wi-Fi) running while " +
                                "it is open, even with the screen off. This uses more " +
                                "battery - use the charger for long sessions.", "Keep awake");
        }

        void OnTestAlert(object sender, EventArgs e)
        {
            if (notifier.Sound) notifier.Beep();
            if (notifier.Vibrate) notifier.Buzz();
            if (!notifier.Sound && !notifier.Vibrate)
                MessageBox.Show("Sound and Vibrate are both off.", "Test alert");
        }

        // -------- sending --------
        void OnInputKey(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                e.Handled = true;
                OnSend(sender, EventArgs.Empty);
            }
        }

        void OnSend(object sender, EventArgs e)
        {
            string text = Mesh.Clean(input.Text).Trim();
            if (SendNow(text)) input.Text = "";
            input.Focus();
        }

        void OnQuick(object sender, EventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi != null) SendNow(mi.Text);
            input.Focus();
        }

        bool SendNow(string text)
        {
            if (text.Length == 0) return false;
            if (Encoding.UTF8.GetByteCount(text) > 200)
            {
                status.Text = "Too long (200 max)";
                return false;
            }
            try
            {
                mesh.SendText(text, mesh.Current);
                return true;
            }
            catch (Exception ex)
            {
                status.ForeColor = Color.DarkRed;
                status.Text = "Not sent: " + ex.Message;
                return false;
            }
        }

        // -------- menu --------
        void OnInfo(object sender, EventArgs e)
        {
            MessageBox.Show(
                "MeshChatCE " + Version + "\r\n" +
                "Heltec: " + mesh.Host + ":" + mesh.Port + "\r\n" +
                "State: " + (mesh.Connected ? "connected" : mesh.Status) + "\r\n" +
                "Drops: " + mesh.Drops + "\r\n" +
                "Preset: " + (mesh.Preset != null ? mesh.Preset : "?") + "\r\n" +
                "Channels: " + mesh.ChannelList().Count + "\r\n" +
                "Nodes known: " + mesh.KnownNodes().Count + "\r\n" +
                gps.Summary() + " (" + gps.Port + ")\r\n" +
                "Range log: " + (range.Active ? "on" : "off") + "\r\n" +
                "Keep awake: " + (Pref("awake", "off") ? "on" : "off") + "\r\n" +
                "History: " + mesh.HistoryFile,
                "MeshChatCE");
        }

        void OnExit(object sender, EventArgs e)
        {
            Close();
        }

        void OnClosed(object sender, EventArgs e)
        {
            tick.Enabled = false;
            awakeTick.Enabled = false;
            Power.Unattended(false);
            notifier.Blink(false);
            HideBubble();
            gps.TurnOff("off");
            range.Stop();
            mesh.Stop();
        }
    }

    // ---------------------------------------------------------------
    // A small screen for choosing who to send a DM to
    // ---------------------------------------------------------------
    public class DmPicker : Form
    {
        public uint Chosen = 0;
        ListBox list = new ListBox();
        List<uint> nums = new List<uint>();
        MainMenu menu = new MainMenu();
        MenuItem okKey = new MenuItem();
        MenuItem cancelKey = new MenuItem();

        public DmPicker(Mesh mesh, List<uint> nodes)
        {
            Text = "Direct message to...";
            MinimizeBox = false;
            list.Dock = DockStyle.Fill;
            list.Font = Fonts.Regular();

            // sort by name
            List<string> labels = new List<string>();
            foreach (uint n in nodes)
            {
                string label = mesh.NodeName(n);
                string longName = mesh.LongName(n);
                if (longName.Length > 0 && longName != label)
                    label += "  (" + longName + ")";
                int at = 0;
                while (at < labels.Count &&
                       string.Compare(labels[at], label, true) < 0) at++;
                labels.Insert(at, label);
                nums.Insert(at, n);
            }
            foreach (string label in labels) list.Items.Add(label);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            Controls.Add(list);

            okKey.Text = "OK";
            okKey.Click += new EventHandler(OnOk);
            cancelKey.Text = "Cancel";
            cancelKey.Click += new EventHandler(OnCancel);
            menu.MenuItems.Add(okKey);
            menu.MenuItems.Add(cancelKey);
            Menu = menu;
            list.KeyPress += new KeyPressEventHandler(OnKey);
        }

        void OnKey(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                e.Handled = true;
                OnOk(sender, EventArgs.Empty);
            }
        }

        void OnOk(object sender, EventArgs e)
        {
            int i = list.SelectedIndex;
            if (i >= 0 && i < nums.Count) Chosen = nums[i];
            DialogResult = DialogResult.OK;
        }

        void OnCancel(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }
    }

    public class Program
    {
        [MTAThread]
        static void Main()
        {
            Application.Run(new MainForm());
        }
    }
}
