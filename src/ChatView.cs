// ChatView.cs - texting-style message list: your messages on the right,
// received on the left, in colored bubbles. Drawn by hand (the phone has
// no built-in control for this). Scroll with the scrollbar or the
// up/down keys; tap a bubble for its details.
// Part of MeshChatCE for Windows Mobile (.NET Compact Framework 3.5).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MeshChatCE
{
    public delegate string MessageInfo(ChatMessage m);
    public delegate void MessageTap(ChatMessage m);

    public class ChatView : Control
    {
        Mesh mesh;
        VScrollBar bar = new VScrollBar();
        List<ChatMessage> items = new List<ChatMessage>();
        List<Entry> layout = new List<Entry>();
        int totalHeight = 0;
        int layoutWidth = -1;
        bool stickToBottom = true;
        bool forceBottom = true;                // next update: go to the newest
        int newCount = 0;                       // arrived while scrolled up
        ChatMessage lastItem = null;
        int bannerHeight = 0;

        public MessageInfo FooterForMine;      // e.g. "08:15  delivered"
        public event MessageTap Tapped;

        const int Pad = 4;                      // inside a bubble
        const int Margin = 4;                   // around a bubble
        const int Gap = 6;                      // between messages
        const int Corner = 5;

        class Entry
        {
            public ChatMessage Msg;
            public string DayLabel;             // date separator above, or null
            public string Header;               // name + time above (received)
            public string Footer;               // status/signal below
            public List<string> Lines = new List<string>();
            public int Top, Height;
            public Rectangle Bubble;            // relative to Top
            public int HeaderY, FooterY, DayY;
        }

        public ChatView(Mesh mesh)
        {
            this.mesh = mesh;
            bar.Dock = DockStyle.Right;
            bar.Width = 13;
            bar.Minimum = 0;
            bar.SmallChange = 20;
            bar.ValueChanged += new EventHandler(OnScroll);
            Controls.Add(bar);
        }

        // ---------- data ----------
        public void SetMessages(List<ChatMessage> list)
        {
            bool wasAtBottom = AtBottom() || stickToBottom;
            ChatMessage newest = list.Count > 0 ? list[list.Count - 1] : null;

            // count messages that are new since last time
            int added = 0;
            if (lastItem != null && newest != null && newest != lastItem)
            {
                int i = list.IndexOf(lastItem);
                added = i >= 0 ? list.Count - 1 - i : 1;
            }
            bool mineArrived = newest != null && newest != lastItem && newest.Mine;
            lastItem = newest;

            items = list;
            layoutWidth = -1;                   // lay out again
            Relayout();

            if (forceBottom || wasAtBottom || mineArrived)
            {
                // just opened, already at the bottom, or I sent something
                forceBottom = false;
                newCount = 0;
                ScrollToBottom();
            }
            else if (added > 0)
            {
                newCount += added;              // keep the place, show the bar
            }
            Invalidate();
        }

        // call when switching conversations: open at the newest message
        public void OpenAtBottom()
        {
            forceBottom = true;
            newCount = 0;
            lastItem = null;
        }

        public void FontsChanged()
        {
            layoutWidth = -1;
            Relayout();
            ScrollToBottom();
            Invalidate();
        }

        // ---------- layout ----------
        int ViewWidth()
        {
            int w = ClientSize.Width - (bar.Visible ? bar.Width : 0);
            return w > 40 ? w : 40;
        }

        void Relayout()
        {
            int width = ViewWidth();
            if (width == layoutWidth) return;
            layoutWidth = width;
            layout.Clear();
            using (Graphics g = CreateGraphics())
            using (Font body = Fonts.Regular())
            using (Font small = Fonts.Small())
            {
                int lineH = (int)g.MeasureString("Ag", body).Height;
                int smallH = (int)g.MeasureString("Ag", small).Height;
                int maxText = (int)(width * 0.75) - 2 * Pad;
                if (maxText < 40) maxText = 40;
                int y = Margin;
                DateTime lastDay = DateTime.MinValue;

                foreach (ChatMessage m in items)
                {
                    Entry e = new Entry();
                    e.Msg = m;
                    e.Top = y;
                    int at = 0;

                    if (m.Time.Date != lastDay)
                    {
                        lastDay = m.Time.Date;
                        e.DayLabel = m.Time.Date == DateTime.Now.Date ? "Today" :
                                     m.Time.ToString("ddd MMM d");
                        e.DayY = at;
                        at += smallH + Gap;
                    }
                    if (!m.Mine)
                    {
                        e.Header = mesh.NodeName(m.From) + "  " + m.Time.ToString("HH:mm");
                        e.HeaderY = at;
                        at += smallH;
                    }

                    e.Lines = Wrap(g, body, m.Text, maxText);
                    int textW = 0;
                    foreach (string line in e.Lines)
                    {
                        int lw = (int)g.MeasureString(line, body).Width;
                        if (lw > textW) textW = lw;
                    }
                    int bw = textW + 2 * Pad;
                    int bh = e.Lines.Count * lineH + 2 * Pad;
                    int bx = m.Mine ? width - Margin - bw : Margin;
                    e.Bubble = new Rectangle(bx, at, bw, bh);
                    at += bh;

                    e.Footer = m.Mine ? (FooterForMine != null ? FooterForMine(m) : "")
                                      : m.Signal;
                    if (e.Footer != null && e.Footer.Length > 0)
                    {
                        e.FooterY = at + 1;
                        at += smallH + 1;
                    }
                    e.Height = at;
                    layout.Add(e);
                    y += at + Gap;
                }
                totalHeight = y;
            }
            UpdateBar();
        }

        // break text into lines that fit (long words are split too)
        static List<string> Wrap(Graphics g, Font f, string text, int maxW)
        {
            List<string> lines = new List<string>();
            if (text == null || text.Length == 0) { lines.Add(" "); return lines; }
            string[] words = text.Split(' ');
            string cur = "";
            foreach (string word0 in words)
            {
                string word = word0;
                // a single word too wide for a line: cut it up
                while (g.MeasureString(word, f).Width > maxW && word.Length > 1)
                {
                    if (cur.Length > 0) { lines.Add(cur); cur = ""; }
                    int n = word.Length - 1;
                    while (n > 1 && g.MeasureString(word.Substring(0, n), f).Width > maxW) n--;
                    lines.Add(word.Substring(0, n));
                    word = word.Substring(n);
                }
                string trial = cur.Length == 0 ? word : cur + " " + word;
                if (g.MeasureString(trial, f).Width <= maxW)
                {
                    cur = trial;
                }
                else
                {
                    if (cur.Length > 0) lines.Add(cur);
                    cur = word;
                }
            }
            if (cur.Length > 0) lines.Add(cur);
            if (lines.Count == 0) lines.Add(" ");
            return lines;
        }

        // ---------- scrolling ----------
        int ScrollMax()
        {
            int m = totalHeight - ClientSize.Height;
            return m > 0 ? m : 0;
        }

        void UpdateBar()
        {
            int view = ClientSize.Height;
            if (totalHeight <= view || view <= 0)
            {
                bar.Enabled = false;
                bar.Maximum = 0;
                bar.Value = 0;
                return;
            }
            bar.Enabled = true;
            bar.LargeChange = view;
            bar.Maximum = totalHeight - 1;
            if (bar.Value > ScrollMax()) bar.Value = ScrollMax();
        }

        bool AtBottom()
        {
            return !bar.Enabled || bar.Value >= ScrollMax() - 4;
        }

        public void ScrollToBottom()
        {
            newCount = 0;
            stickToBottom = true;
            if (bar.Enabled) bar.Value = ScrollMax();
            Invalidate();
        }

        void ScrollBy(int dy)
        {
            if (!bar.Enabled) return;
            int v = bar.Value + dy;
            if (v < 0) v = 0;
            if (v > ScrollMax()) v = ScrollMax();
            bar.Value = v;
        }

        void OnScroll(object sender, EventArgs e)
        {
            stickToBottom = AtBottom();
            if (stickToBottom) newCount = 0;    // reached the new messages
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { ScrollBy(-20); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { ScrollBy(20); e.Handled = true; }
            else base.OnKeyDown(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            layoutWidth = -1;
            Relayout();
            if (stickToBottom) ScrollToBottom();
            Invalidate();
        }

        // ---------- drawing ----------
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // everything is drawn in OnPaint (no flicker)
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int w = ViewWidth(), h = ClientSize.Height;
            if (w < 10 || h < 10) return;
            if (layoutWidth != w) Relayout();
            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Draw(g, w, h);
                }
                e.Graphics.DrawImage(bmp, 0, 0);
            }
        }

        void Draw(Graphics g, int w, int h)
        {
            g.Clear(Theme.Back);
            int scroll = bar.Enabled ? bar.Value : 0;
            using (Font body = Fonts.Regular())
            using (Font small = Fonts.Small())
            using (Font smallBold = Fonts.SmallBold())
            using (SolidBrush mineBrush = new SolidBrush(Theme.Mine))
            using (SolidBrush theirsBrush = new SolidBrush(Theme.Theirs))
            using (SolidBrush mineText = new SolidBrush(Theme.MineText))
            using (SolidBrush theirsText = new SolidBrush(Theme.TheirsText))
            using (SolidBrush meta = new SolidBrush(Theme.Meta))
            using (SolidBrush name = new SolidBrush(Theme.Name1))
            using (SolidBrush day = new SolidBrush(Theme.Day))
            {
                int lineH = (int)g.MeasureString("Ag", body).Height;
                if (layout.Count == 0)
                {
                    g.DrawString("No messages yet.", body, meta, Margin, Margin);
                    return;
                }
                foreach (Entry e in layout)
                {
                    int top = e.Top - scroll;
                    if (top + e.Height < 0 || top > h) continue;      // off screen

                    if (e.DayLabel != null)
                    {
                        int dw = (int)g.MeasureString(e.DayLabel, small).Width;
                        g.DrawString(e.DayLabel, small, day, (w - dw) / 2, top + e.DayY);
                    }
                    if (e.Header != null)
                    {
                        g.DrawString(e.Header, smallBold, name, e.Bubble.X + 1, top + e.HeaderY);
                    }

                    Rectangle r = new Rectangle(e.Bubble.X, top + e.Bubble.Y,
                                                e.Bubble.Width, e.Bubble.Height);
                    RoundRect(g, e.Msg.Mine ? mineBrush : theirsBrush, r);
                    SolidBrush textBrush = e.Msg.Mine ? mineText : theirsText;
                    int ly = r.Y + Pad;
                    foreach (string line in e.Lines)
                    {
                        g.DrawString(line, body, textBrush, r.X + Pad, ly);
                        ly += lineH;
                    }

                    if (e.Footer != null && e.Footer.Length > 0)
                    {
                        int fx = r.X + 1;
                        if (e.Msg.Mine)
                        {
                            int fw = (int)g.MeasureString(e.Footer, small).Width;
                            fx = r.Right - fw - 1;
                            if (fx < Margin) fx = Margin;
                        }
                        g.DrawString(e.Footer, small, meta, fx, top + e.FooterY);
                    }
                }

                // "v New message" bar at the bottom while scrolled up
                bannerHeight = 0;
                if (newCount > 0)
                {
                    string label = newCount == 1 ? "New message" : newCount + " new messages";
                    int th = (int)g.MeasureString(label, smallBold).Height;
                    bannerHeight = th + 8;
                    int by = h - bannerHeight;
                    g.FillRectangle(mineBrush, 0, by, w, bannerHeight);
                    int tw = (int)g.MeasureString(label, smallBold).Width;
                    int tx = (w - tw) / 2 + 7;
                    // a small down-pointing triangle
                    int ax = tx - 12, ay = by + bannerHeight / 2 - 3;
                    g.FillPolygon(mineText, new Point[] {
                        new Point(ax, ay), new Point(ax + 9, ay), new Point(ax + 4, ay + 5) });
                    g.DrawString(label, smallBold, mineText, tx, by + 4);
                }
            }
        }

        // a rectangle with rounded corners (no curves API on the phone,
        // so: two overlapping rectangles plus a circle in each corner)
        static void RoundRect(Graphics g, Brush b, Rectangle r)
        {
            int c = Corner;
            if (r.Width < 2 * c || r.Height < 2 * c)
            {
                g.FillRectangle(b, r);
                return;
            }
            g.FillRectangle(b, r.X + c, r.Y, r.Width - 2 * c, r.Height);
            g.FillRectangle(b, r.X, r.Y + c, r.Width, r.Height - 2 * c);
            g.FillEllipse(b, r.X, r.Y, 2 * c, 2 * c);
            g.FillEllipse(b, r.Right - 2 * c - 1, r.Y, 2 * c, 2 * c);
            g.FillEllipse(b, r.X, r.Bottom - 2 * c - 1, 2 * c, 2 * c);
            g.FillEllipse(b, r.Right - 2 * c - 1, r.Bottom - 2 * c - 1, 2 * c, 2 * c);
        }

        // ---------- tapping ----------
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Focus();                                   // so the arrow keys scroll
            if (newCount > 0 && bannerHeight > 0 && e.Y >= ClientSize.Height - bannerHeight)
            {
                ScrollToBottom();
                return;
            }
            int scroll = bar.Enabled ? bar.Value : 0;
            int y = e.Y + scroll;
            foreach (Entry en in layout)
            {
                Rectangle r = new Rectangle(en.Bubble.X, en.Top + en.Bubble.Y,
                                            en.Bubble.Width, en.Bubble.Height);
                if (r.Contains(e.X, y))
                {
                    MessageTap t = Tapped;
                    if (t != null) t(en.Msg);
                    return;
                }
            }
        }
    }
}
