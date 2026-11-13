// MapForm.cs - the radar-style map: you in the middle, nodes by distance
// and direction. Works with no internet - positions come from your GPS
// and from what nodes share over the mesh.
// Part of MeshChatCE for Windows Mobile (.NET Compact Framework 3.5).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MeshChatCE
{
    public delegate void NodeTap(uint num);

    // ---------------------------------------------------------------
    // The drawing itself
    // ---------------------------------------------------------------
    public class MapView : Control
    {
        Mesh mesh;
        Prefs prefs;
        Gps gps;

        // ring sizes to choose from, in miles or km
        static readonly double[] Steps = new double[] {
            0.1, 0.25, 0.5, 1, 2, 5, 10, 25, 50, 100, 250 };
        public int StepIndex = -1;          // -1 = automatic
        int autoIndex = 3;

        // where the dots were drawn, for tapping
        List<uint> dotNums = new List<uint>();
        List<Point> dotPoints = new List<Point>();

        public event NodeTap Tapped;

        public MapView(Mesh mesh, Prefs prefs, Gps gps)
        {
            this.mesh = mesh;
            this.prefs = prefs;
            this.gps = gps;
        }

        static double UnitMeters()
        {
            return Geo.Units == "km" ? 1000.0 : 1609.344;
        }

        public int CurrentIndex()
        {
            return StepIndex >= 0 ? StepIndex : autoIndex;
        }

        public void ZoomIn()
        {
            int i = CurrentIndex();
            if (i > 0) i--;
            StepIndex = i;
            Invalidate();
        }

        public void ZoomOut()
        {
            int i = CurrentIndex();
            if (i < Steps.Length - 1) i++;
            StepIndex = i;
            Invalidate();
        }

        public void ZoomAuto()
        {
            StepIndex = -1;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // everything is drawn in OnPaint (avoids flicker)
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 10 || h < 10) return;
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
            Font font = Fonts.Regular();
            Font bold = Fonts.Bold();
            g.Clear(Color.White);
            dotNums.Clear();
            dotPoints.Clear();

            int lineH = (int)(Fonts.Size * 2) + 2;
            int cx = w / 2;
            int cy = (h - lineH) / 2;
            int radius = Math.Min(w, h - lineH) / 2 - (int)(Fonts.Size * 2);
            if (radius < 20) radius = 20;

            using (SolidBrush black = new SolidBrush(Color.Black))
            using (SolidBrush grey = new SolidBrush(Color.Gray))
            {
                double myLat, myLon;
                if (!Geo.MyPosition(mesh, gps, out myLat, out myLon))
                {
                    g.DrawString("Your position is unknown.", bold, black, 4, 4);
                    g.DrawString("Turn on the GPS (Menu > GPS)", font, black, 4, 4 + lineH);
                    g.DrawString("or give the Heltec a fixed", font, black, 4, 4 + 2 * lineH);
                    g.DrawString("position in its settings.", font, black, 4, 4 + 3 * lineH);
                    return;
                }

                // nodes with a position: distance and bearing from us
                List<Node> nodes = mesh.NodeList();
                List<Node> placed = new List<Node>();
                List<double> dist = new List<double>();
                List<double> bear = new List<double>();
                int noPos = 0;
                double far = 0;
                foreach (Node n in nodes)
                {
                    if (double.IsNaN(n.Lat)) { noPos++; continue; }
                    string dir;
                    double d = Geo.Distance(myLat, myLon, n.Lat, n.Lon, out dir);
                    placed.Add(n);
                    dist.Add(d);
                    bear.Add(Geo.Bearing(myLat, myLon, n.Lat, n.Lon));
                    if (d > far) far = d;
                }

                // automatic zoom: smallest ring that fits everyone
                autoIndex = Steps.Length - 1;
                for (int i = 0; i < Steps.Length; i++)
                {
                    if (Steps[i] * UnitMeters() >= far * 1.1) { autoIndex = i; break; }
                }
                double range = Steps[CurrentIndex()] * UnitMeters();

                // rings and cross
                using (Pen ring = new Pen(Color.FromArgb(200, 200, 200)))
                using (Pen cross = new Pen(Color.FromArgb(225, 225, 225)))
                {
                    g.DrawLine(cross, cx - radius, cy, cx + radius, cy);
                    g.DrawLine(cross, cx, cy - radius, cx, cy + radius);
                    for (int k = 1; k <= 3; k++)
                    {
                        int r = radius * k / 3;
                        g.DrawEllipse(ring, cx - r, cy - r, 2 * r, 2 * r);
                        string label = Geo.Format(range * k / 3);
                        g.DrawString(label, font, grey, cx + 2, cy - r);
                    }
                }
                g.DrawString("N", bold, black, cx + 3, cy - radius - lineH + 2);

                // you
                using (SolidBrush blue = new SolidBrush(Color.Blue))
                {
                    g.FillEllipse(blue, cx - 4, cy - 4, 9, 9);
                }

                // the nodes
                List<uint> favs = prefs.Favorites();
                long now = Mesh.NowEpoch();
                using (SolidBrush fresh = new SolidBrush(Color.Green))
                using (SolidBrush fav = new SolidBrush(Color.FromArgb(230, 120, 0)))
                using (Pen oldPen = new Pen(Color.Gray))
                {
                    for (int i = 0; i < placed.Count; i++)
                    {
                        Node n = placed[i];
                        double frac = dist[i] / range;
                        bool beyond = frac > 1.0;
                        if (beyond) frac = 1.0;
                        double rad = bear[i] * Math.PI / 180.0;
                        int x = cx + (int)(Math.Sin(rad) * frac * radius);
                        int y = cy - (int)(Math.Cos(rad) * frac * radius);

                        bool recent = n.Heard > 0 && now - n.Heard < 15 * 60;
                        bool isFav = favs.Contains(n.Num);
                        if (isFav) g.FillEllipse(fav, x - 4, y - 4, 9, 9);
                        else if (recent) g.FillEllipse(fresh, x - 4, y - 4, 9, 9);
                        else g.DrawEllipse(oldPen, x - 4, y - 4, 8, 8);

                        string name = mesh.NodeName(n.Num) + (beyond ? "+" : "");
                        g.DrawString(name, font, black, x + 6, y - (int)Fonts.Size);
                        dotNums.Add(n.Num);
                        dotPoints.Add(new Point(x, y));
                    }
                }

                // bottom line
                string info = placed.Count + " on map";
                if (noPos > 0) info += ", " + noPos + " without position";
                info += StepIndex < 0 ? "  (auto zoom)" : "";
                g.DrawString(info, font, grey, 2, h - lineH);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int best = -1;
            int bestD = 18 * 18;                  // tap within ~18 pixels
            for (int i = 0; i < dotPoints.Count; i++)
            {
                int dx = dotPoints[i].X - e.X, dy = dotPoints[i].Y - e.Y;
                int d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0)
            {
                NodeTap t = Tapped;
                if (t != null) t(dotNums[best]);
            }
        }
    }

    // ---------------------------------------------------------------
    // The map screen
    // ---------------------------------------------------------------
    public class MapForm : Form
    {
        Mesh mesh;
        Prefs prefs;
        Gps gps;
        MapView view;
        Timer tick = new Timer();
        MainMenu menu = new MainMenu();
        MenuItem backKey = new MenuItem();
        MenuItem zoomKey = new MenuItem();
        MenuItem zoomIn = new MenuItem();
        MenuItem zoomOut = new MenuItem();
        MenuItem zoomAuto = new MenuItem();

        public MapForm(Mesh mesh, Prefs prefs, Gps gps)
        {
            this.mesh = mesh;
            this.prefs = prefs;
            this.gps = gps;
            Text = "Map";
            MinimizeBox = false;

            view = new MapView(mesh, prefs, gps);
            view.Dock = DockStyle.Fill;
            view.Tapped += new NodeTap(OnTapped);
            Controls.Add(view);

            backKey.Text = "Back";
            backKey.Click += new EventHandler(OnBack);
            zoomKey.Text = "Zoom";
            zoomIn.Text = "Zoom in";
            zoomIn.Click += new EventHandler(OnZoomIn);
            zoomOut.Text = "Zoom out";
            zoomOut.Click += new EventHandler(OnZoomOut);
            zoomAuto.Text = "Fit everyone";
            zoomAuto.Click += new EventHandler(OnZoomAuto);
            zoomKey.MenuItems.Add(zoomIn);
            zoomKey.MenuItems.Add(zoomOut);
            zoomKey.MenuItems.Add(zoomAuto);
            menu.MenuItems.Add(backKey);
            menu.MenuItems.Add(zoomKey);
            Menu = menu;

            tick.Interval = 5000;
            tick.Tick += new EventHandler(OnTick);
            tick.Enabled = true;
            Closed += new EventHandler(OnClosed);
        }

        void OnTick(object sender, EventArgs e) { view.Invalidate(); }
        void OnZoomIn(object sender, EventArgs e) { view.ZoomIn(); }
        void OnZoomOut(object sender, EventArgs e) { view.ZoomOut(); }
        void OnZoomAuto(object sender, EventArgs e) { view.ZoomAuto(); }

        void OnTapped(uint num)
        {
            foreach (Node n in mesh.NodeList())
            {
                if (n.Num == num)
                {
                    MessageBox.Show(NodesForm.DetailsText(mesh, prefs, gps, n), "Node details");
                    return;
                }
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
