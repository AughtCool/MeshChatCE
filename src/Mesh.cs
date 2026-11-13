// Mesh.cs - talks to the Meshtastic node (Heltec) over Wi-Fi.
// Part of MeshChatCE for Windows Mobile (.NET Compact Framework 3.5).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MeshChatCE
{
    // ---------------------------------------------------------------
    // Just enough Protobuf to read and write Meshtastic messages
    // ---------------------------------------------------------------
    public class Pb
    {
        // field number -> list of values (ulong, uint, or byte[])
        public Dictionary<int, List<object>> Fields =
            new Dictionary<int, List<object>>();

        public static Pb Parse(byte[] buf)
        {
            return Parse(buf, 0, buf.Length);
        }

        public static Pb Parse(byte[] buf, int start, int length)
        {
            Pb pb = new Pb();
            int pos = start;
            int end = start + length;
            while (pos < end)
            {
                ulong key = ReadVarint(buf, ref pos);
                int num = (int)(key >> 3);
                int wt = (int)(key & 7);
                object val;
                if (wt == 0)
                {
                    val = ReadVarint(buf, ref pos);
                }
                else if (wt == 1)
                {
                    byte[] b = new byte[8];
                    Array.Copy(buf, pos, b, 0, 8);
                    pos += 8;
                    val = b;
                }
                else if (wt == 2)
                {
                    int len = (int)ReadVarint(buf, ref pos);
                    byte[] b = new byte[len];
                    Array.Copy(buf, pos, b, 0, len);
                    pos += len;
                    val = b;
                }
                else if (wt == 5)
                {
                    val = BitConverter.ToUInt32(buf, pos);
                    pos += 4;
                }
                else
                {
                    throw new IOException("bad wire type " + wt);
                }
                List<object> list;
                if (!pb.Fields.TryGetValue(num, out list))
                {
                    list = new List<object>();
                    pb.Fields[num] = list;
                }
                list.Add(val);
            }
            return pb;
        }

        public static ulong ReadVarint(byte[] buf, ref int pos)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte c = buf[pos];
                pos++;
                result |= ((ulong)(c & 0x7F)) << shift;
                if ((c & 0x80) == 0)
                    return result;
                shift += 7;
            }
        }

        public bool Has(int num)
        {
            return Fields.ContainsKey(num);
        }

        public object First(int num)
        {
            List<object> list;
            if (Fields.TryGetValue(num, out list) && list.Count > 0)
                return list[0];
            return null;
        }

        public ulong Num(int num, ulong dflt)
        {
            object v = First(num);
            if (v is ulong) return (ulong)v;
            if (v is uint) return (uint)v;
            return dflt;
        }

        public byte[] Bytes(int num)
        {
            return First(num) as byte[];
        }

        public Pb Sub(int num)
        {
            byte[] b = Bytes(num);
            if (b == null) return null;
            return Parse(b);
        }

        public string Text(int num)
        {
            byte[] b = Bytes(num);
            if (b == null) return null;
            return Encoding.UTF8.GetString(b, 0, b.Length);
        }

        // 4-byte float field (like SNR)
        public float Float(int num, float dflt)
        {
            object v = First(num);
            if (v is uint)
                return BitConverter.ToSingle(BitConverter.GetBytes((uint)v), 0);
            return dflt;
        }

        // negative numbers (like signal strength) arrive as huge numbers
        public int Signed(int num, int dflt)
        {
            object v = First(num);
            if (v is ulong) return (int)(long)(ulong)v;
            if (v is uint) return (int)(uint)v;
            return dflt;
        }
    }

    // Builds Protobuf messages
    public class PbWriter
    {
        MemoryStream ms = new MemoryStream();

        void Varint(ulong n)
        {
            while (true)
            {
                byte b = (byte)(n & 0x7F);
                n >>= 7;
                if (n != 0)
                {
                    ms.WriteByte((byte)(b | 0x80));
                }
                else
                {
                    ms.WriteByte(b);
                    break;
                }
            }
        }

        public PbWriter Number(int num, ulong val)
        {
            Varint((ulong)(num << 3));
            Varint(val);
            return this;
        }

        public PbWriter Fixed32(int num, uint val)
        {
            Varint((ulong)((num << 3) | 5));
            byte[] b = BitConverter.GetBytes(val);
            ms.Write(b, 0, 4);
            return this;
        }

        public PbWriter Bytes(int num, byte[] data)
        {
            Varint((ulong)((num << 3) | 2));
            Varint((ulong)data.Length);
            ms.Write(data, 0, data.Length);
            return this;
        }

        public PbWriter Message(int num, PbWriter inner)
        {
            return Bytes(num, inner.ToArray());
        }

        public byte[] ToArray()
        {
            return ms.ToArray();
        }
    }

    // ---------------------------------------------------------------
    // A chat message
    // ---------------------------------------------------------------
    public class ChatMessage
    {
        public DateTime Time;
        public uint From;
        public bool Mine;
        public string Text;
        public string Key;        // "c0" = channel 0, "d1234" = DM
        public string Signal = "";
        public uint Id = 0;
        public uint DestDm = 0;   // for my DMs: who it was sent to
        public string Status = "";  // my messages: sending / heard by mesh ...
    }

    // ---------------------------------------------------------------
    // What we know about one node on the mesh
    // ---------------------------------------------------------------
    public class Node
    {
        public uint Num;
        public long Heard = 0;        // seconds since 1970 (UTC), 0 = never
        public float Snr = float.NaN;
        public int Hops = -1;         // -1 = unknown
        public int Battery = -1;      // percent, 101 = plugged in, -1 = unknown
        public float Voltage = float.NaN;
        public float ChannelUse = float.NaN;
        public float AirTx = float.NaN;
        public long Uptime = -1;
        public double Lat = double.NaN, Lon = double.NaN;
        public int Channel = 0;
    }

    // ---------------------------------------------------------------
    // A traceroute request and its answer
    // ---------------------------------------------------------------
    public class Trace
    {
        public uint Target;
        public uint Id;
        public DateTime Sent;
        public DateTime Answered;
        public string Status = "waiting";     // waiting / done / no response / failed: ...
        public List<uint> Route = new List<uint>();
        public List<int> SnrTo = new List<int>();     // dB x 4, -128 = unknown
        public List<uint> Back = new List<uint>();
        public List<int> SnrBack = new List<int>();
    }

    public delegate void MeshEvent();
    public delegate void MessageEvent(ChatMessage m);

    // ---------------------------------------------------------------
    // The connection to the Heltec
    // ---------------------------------------------------------------
    public class Mesh
    {
        // split "a\tb\tc..." into exactly n parts (the last keeps any tabs)
        public static string[] SplitTabs(string line, int n)
        {
            string[] parts = new string[n];
            int pos = 0;
            for (int i = 0; i < n - 1; i++)
            {
                int tab = line.IndexOf('\t', pos);
                if (tab < 0) return null;
                parts[i] = line.Substring(pos, tab - pos);
                pos = tab + 1;
            }
            parts[n - 1] = line.Substring(pos);
            return parts;
        }

        public const uint BROADCAST = 0xFFFFFFFF;
        const int PORT_TEXT = 1;

        public string Host = "192.168.4.250";
        public int Port = 4403;
        public int HopLimit = 3;
        public string HistoryFile = "";

        public volatile bool Connected = false;
        public string Status = "Starting...";
        public uint MyNum = 0;
        public Dictionary<uint, string> Names = new Dictionary<uint, string>();
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public int Drops = 0;

        // raised (on the background thread) when something changes
        public event MeshEvent Changed;

        public readonly object Lock = new object();

        Socket sock;
        readonly object sendLock = new object();
        Thread thread;
        volatile bool running = true;
        Random rnd = new Random();
        List<string> seen = new List<string>();
        List<ChatMessage> newMessages = new List<ChatMessage>();

        static readonly string[] Presets = new string[] {
            "LongFast", "LongSlow", "VLongSlow", "MediumSlow", "MediumFast",
            "ShortSlow", "ShortFast", "LongMod", "ShortTurbo", "LongTurbo" };
        public string Preset = null;
        public Dictionary<int, string> ChannelNames = new Dictionary<int, string>();
        public Dictionary<int, int> ChannelRoles = new Dictionary<int, int>();
        public Dictionary<uint, int> NodeChannel = new Dictionary<uint, int>();
        public Dictionary<string, int> Unread = new Dictionary<string, int>();
        Dictionary<uint, ChatMessage> pending = new Dictionary<uint, ChatMessage>();

        // the conversation the screen is showing (set by the screen)
        public string Current = "c0";

        static readonly Dictionary<int, string> RouteErrors = MakeRouteErrors();
        static Dictionary<int, string> MakeRouteErrors()
        {
            Dictionary<int, string> d = new Dictionary<int, string>();
            d[1] = "no route to them";
            d[2] = "refused by the mesh";
            d[3] = "timed out - try again";
            d[4] = "the radio isn't ready";
            d[5] = "no answer after several tries - out of range?";
            d[6] = "channel not found on the radio";
            d[7] = "message too large";
            d[8] = "no response";
            d[9] = "radio airtime limit reached - wait a bit";
            d[32] = "bad request";
            d[33] = "not authorized";
            d[34] = "encryption failed - try Send my info, then retry";
            d[35] = "they don't have your key - try Send my info, then retry";
            d[36] = "admin session expired";
            d[37] = "admin not authorized";
            d[38] = "sending too fast - wait a moment";
            d[39] = "missing their key - try again in a minute";
            return d;
        }

        // channels the radio has (index numbers), primary first
        public List<int> ChannelList()
        {
            List<int> list = new List<int>();
            lock (Lock)
            {
                foreach (KeyValuePair<int, int> kv in ChannelRoles)
                {
                    if (kv.Value != 0) list.Add(kv.Key);
                }
            }
            list.Sort();
            if (list.Count == 0) list.Add(0);
            return list;
        }

        // DM conversations that exist, as keys like "d1234"
        public List<string> DmKeys()
        {
            List<string> keys = new List<string>();
            lock (Lock)
            {
                for (int i = 0; i < Messages.Count; i++)
                {
                    string k = Messages[i].Key;
                    if (k.StartsWith("d") && !keys.Contains(k)) keys.Add(k);
                }
                foreach (string k in Unread.Keys)
                {
                    if (k.StartsWith("d") && !keys.Contains(k)) keys.Add(k);
                }
            }
            if (Current.StartsWith("d") && !keys.Contains(Current)) keys.Add(Current);
            keys.Sort();
            return keys;
        }

        public string Title(string key)
        {
            if (key.StartsWith("c"))
                return "#" + ChannelName(int.Parse(key.Substring(1)));
            return "DM " + NodeName(uint.Parse(key.Substring(1)));
        }

        public int UnreadCount(string key)
        {
            int n;
            lock (Lock)
            {
                if (Unread.TryGetValue(key, out n)) return n;
            }
            return 0;
        }

        public void MarkRead(string key)
        {
            lock (Lock)
            {
                Unread[key] = 0;
            }
        }

        // known nodes (not including this one), for the DM picker
        public List<uint> KnownNodes()
        {
            List<uint> list = new List<uint>();
            lock (Lock)
            {
                foreach (uint n in Names.Keys)
                {
                    if (n != MyNum && n != 0) list.Add(n);
                }
            }
            return list;
        }

        public string LongName(uint num)
        {
            string n;
            lock (Lock)
            {
                if (LongNames.TryGetValue(num, out n)) return n;
            }
            return "";
        }
        public Dictionary<uint, string> LongNames = new Dictionary<uint, string>();
        public byte[] MyUserRaw = null;     // our own name + public key, from the radio
        const int PORT_NODEINFO = 4;
        const int PORT_ADMIN = 6;

        public bool HaveMyInfo
        {
            get { return MyUserRaw != null && MyNum != 0; }
        }

        // a copy of a message with some fields replaced (used for our User)
        static byte[] Rebuild(Pb pb, Dictionary<int, byte[]> replace)
        {
            PbWriter w = new PbWriter();
            foreach (KeyValuePair<int, List<object>> kv in pb.Fields)
            {
                if (replace.ContainsKey(kv.Key)) continue;
                foreach (object v in kv.Value)
                {
                    if (v is ulong) w.Number(kv.Key, (ulong)v);
                    else if (v is uint) w.Fixed32(kv.Key, (uint)v);
                    else if (v is byte[]) w.Bytes(kv.Key, (byte[])v);
                }
            }
            foreach (KeyValuePair<int, byte[]> kv in replace) w.Bytes(kv.Key, kv.Value);
            return w.ToArray();
        }

        // -------- telling the mesh who we are --------
        // dest = BROADCAST: announce to everyone.
        // dest = a node + askBack: swap names and keys with that node.
        public string SendMyInfo(uint dest, bool askBack)
        {
            if (sock == null || !Connected) return "Not connected to the Heltec.";
            if (!HaveMyInfo) return "Still loading your node's details - try again shortly.";
            int channel = 0;
            if (dest != BROADCAST)
            {
                lock (Lock)
                {
                    if (!NodeChannel.TryGetValue(dest, out channel)) channel = 0;
                }
            }
            PbWriter data = new PbWriter()
                .Number(1, PORT_NODEINFO)
                .Bytes(2, MyUserRaw);
            if (askBack) data.Number(3, 1);                   // want_response
            PbWriter pkt = new PbWriter()
                .Fixed32(2, dest)
                .Number(3, (ulong)channel)
                .Message(4, data)
                .Fixed32(6, (uint)rnd.Next(1, 0x7FFFFFFF))
                .Number(9, (ulong)HopLimit);
            try
            {
                SendRaw(new PbWriter().Message(1, pkt).ToArray());
            }
            catch (Exception ex)
            {
                return "Not sent: " + ex.Message;
            }
            return "";
        }

        // -------- renaming our node --------
        public string MyLongName()
        {
            return LongName(MyNum);
        }

        public string RenameMyNode(string longName, string shortName)
        {
            if (sock == null || !Connected) return "Not connected to the Heltec.";
            if (!HaveMyInfo) return "Still loading your node's details - try again shortly.";
            Dictionary<int, byte[]> replace = new Dictionary<int, byte[]>();
            replace[2] = Encoding.UTF8.GetBytes(longName);
            replace[3] = Encoding.UTF8.GetBytes(shortName);
            byte[] newUser = Rebuild(Pb.Parse(MyUserRaw), replace);

            // AdminMessage.set_owner (field 32), sent to our own node
            PbWriter admin = new PbWriter().Bytes(32, newUser);
            PbWriter data = new PbWriter()
                .Number(1, PORT_ADMIN)
                .Message(2, admin);
            PbWriter pkt = new PbWriter()
                .Fixed32(2, MyNum)
                .Message(4, data)
                .Fixed32(6, (uint)rnd.Next(1, 0x7FFFFFFF));
            try
            {
                SendRaw(new PbWriter().Message(1, pkt).ToArray());
            }
            catch (Exception ex)
            {
                return "Not renamed: " + ex.Message;
            }
            lock (Lock)
            {
                MyUserRaw = newUser;
                Names[MyNum] = shortName;
                LongNames[MyNum] = longName;
            }
            Fire();
            // tell everyone the new name straight away
            Thread.Sleep(500);
            SendMyInfo(BROADCAST, false);
            return "";
        }
        public Dictionary<uint, Node> Nodes = new Dictionary<uint, Node>();
        public List<Trace> Traces = new List<Trace>();   // newest first
        public MeshStats Stats = new MeshStats();
        public RangeLog Range = null;
        // raised (on the background thread) when a new message arrives
        public event MessageEvent Incoming;
        public const int TraceGapSeconds = 30;
        public const int TraceTimeoutSeconds = 90;
        const int PORT_POSITION = 3;
        const int PORT_TELEMETRY = 67;
        const int PORT_TRACEROUTE = 70;

        public static DateTime LocalTime(double epochSeconds)
        {
            return Epoch.AddSeconds(epochSeconds).ToLocalTime();
        }

        // close the connection; it reconnects by itself (e.g. new address)
        public void Reconnect()
        {
            Close();
        }

        public static long NowEpoch()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }

        // the record for a node, created if new (call inside Lock)
        Node GetNode(uint num)
        {
            Node n;
            if (!Nodes.TryGetValue(num, out n))
            {
                n = new Node();
                n.Num = num;
                Nodes[num] = n;
            }
            return n;
        }

        // a copy-safe snapshot for the screens
        public List<Node> NodeList()
        {
            List<Node> list = new List<Node>();
            lock (Lock)
            {
                foreach (Node n in Nodes.Values)
                {
                    if (n.Num != MyNum && n.Num != 0) list.Add(n);
                }
            }
            return list;
        }

        public Node MyNode()
        {
            lock (Lock)
            {
                Node n;
                if (Nodes.TryGetValue(MyNum, out n)) return n;
            }
            return null;
        }

        void ReadMetrics(Node n, Pb dm)
        {
            if (dm == null) return;
            if (dm.Has(1)) n.Battery = (int)dm.Num(1, 0);
            if (dm.Has(2)) n.Voltage = dm.Float(2, float.NaN);
            if (dm.Has(3)) n.ChannelUse = dm.Float(3, float.NaN);
            if (dm.Has(4)) n.AirTx = dm.Float(4, float.NaN);
            if (dm.Has(5)) n.Uptime = (long)dm.Num(5, 0);
        }

        void ReadPosition(Node n, Pb pos)
        {
            if (pos == null) return;
            int lat = pos.Signed(1, 0);
            int lon = pos.Signed(2, 0);
            if (lat != 0 && lon != 0)
            {
                n.Lat = lat / 1e7;
                n.Lon = lon / 1e7;
            }
        }

        // repeated 4-byte numbers, packed or not
        static List<uint> RepFixed32(Pb pb, int num)
        {
            List<uint> result = new List<uint>();
            List<object> vals;
            if (!pb.Fields.TryGetValue(num, out vals)) return result;
            foreach (object v in vals)
            {
                if (v is uint) result.Add((uint)v);
                else if (v is byte[])
                {
                    byte[] b = (byte[])v;
                    for (int i = 0; i + 3 < b.Length; i += 4)
                        result.Add(BitConverter.ToUInt32(b, i));
                }
            }
            return result;
        }

        // repeated small signed numbers, packed or not
        static List<int> RepInt(Pb pb, int num)
        {
            List<int> result = new List<int>();
            List<object> vals;
            if (!pb.Fields.TryGetValue(num, out vals)) return result;
            foreach (object v in vals)
            {
                if (v is ulong) result.Add((int)(long)(ulong)v);
                else if (v is byte[])
                {
                    byte[] b = (byte[])v;
                    int pos = 0;
                    while (pos < b.Length)
                        result.Add((int)(long)Pb.ReadVarint(b, ref pos));
                }
            }
            return result;
        }

        // -------- our GPS position, given to the Heltec to share --------
        public void SendPosition(double lat, double lon, double alt, long utc, int sats)
        {
            if (sock == null || !Connected || MyNum == 0) return;
            PbWriter pos = new PbWriter()
                .Fixed32(1, (uint)(int)Math.Round(lat * 1e7))
                .Fixed32(2, (uint)(int)Math.Round(lon * 1e7));
            if (!double.IsNaN(alt)) pos.Number(3, (ulong)(long)Math.Round(alt));
            if (utc > 0) pos.Fixed32(4, (uint)utc);
            pos.Number(5, 3);                                   // source: external GPS
            if (sats > 0) pos.Number(19, (ulong)sats);
            PbWriter data = new PbWriter()
                .Number(1, PORT_POSITION)
                .Message(2, pos);
            PbWriter pkt = new PbWriter()
                .Fixed32(2, MyNum)
                .Message(4, data)
                .Fixed32(6, (uint)rnd.Next(1, 0x7FFFFFFF));
            SendRaw(new PbWriter().Message(1, pkt).ToArray());
            lock (Lock)
            {
                Node me = GetNode(MyNum);
                me.Lat = lat;
                me.Lon = lon;
            }
        }

        // -------- traceroute --------
        // returns "" if sent, otherwise a reason it was not
        public string SendTrace(uint target)
        {
            if (sock == null || !Connected) return "Not connected to the Heltec.";
            int channel = 0;
            lock (Lock)
            {
                if (Traces.Count > 0)
                {
                    double ago = (DateTime.Now - Traces[0].Sent).TotalSeconds;
                    if (ago < TraceGapSeconds)
                        return "Please wait " + (int)(TraceGapSeconds - ago) +
                               " s between traceroutes.";
                }
                if (!NodeChannel.TryGetValue(target, out channel)) channel = 0;
            }
            uint id = (uint)rnd.Next(1, 0x7FFFFFFF);
            PbWriter data = new PbWriter()
                .Number(1, PORT_TRACEROUTE)
                .Bytes(2, new byte[0])
                .Number(3, 1);                      // want_response
            PbWriter pkt = new PbWriter()
                .Fixed32(2, target)
                .Number(3, (ulong)channel)
                .Message(4, data)
                .Fixed32(6, id)
                .Number(9, (ulong)HopLimit);
            Trace t = new Trace();
            t.Target = target;
            t.Id = id;
            t.Sent = DateTime.Now;
            lock (Lock)
            {
                Traces.Insert(0, t);
                if (Traces.Count > 10) Traces.RemoveAt(Traces.Count - 1);
            }
            try
            {
                SendRaw(new PbWriter().Message(1, pkt).ToArray());
            }
            catch (Exception ex)
            {
                t.Status = "not sent: " + ex.Message;
            }
            Fire();
            return "";
        }

        Trace FindTrace(uint requestId, uint from)
        {
            foreach (Trace t in Traces)
                if (requestId != 0 && t.Id == requestId) return t;
            foreach (Trace t in Traces)
                if (t.Status == "waiting" && t.Target == from) return t;
            return null;
        }

        // marks traces that waited too long (call inside Lock)
        public void ExpireTraces()
        {
            foreach (Trace t in Traces)
            {
                if (t.Status == "waiting" &&
                    (DateTime.Now - t.Sent).TotalSeconds > TraceTimeoutSeconds)
                    t.Status = "no response";
            }
        }

        public void Start()
        {
            thread = new Thread(new ThreadStart(Run));
            thread.IsBackground = true;
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            Close();
        }

        void Fire()
        {
            MeshEvent e = Changed;
            if (e != null)
            {
                try { e(); }
                catch (Exception) { }
            }
        }

        void SetStatus(string s)
        {
            Status = s;
            Fire();
        }

        public string NodeName(uint num)
        {
            string n;
            lock (Lock)
            {
                if (Names.TryGetValue(num, out n) && n != null && n.Length > 0)
                    return n;
            }
            return "!" + num.ToString("x8");
        }

        public string ChannelName(int idx)
        {
            string n;
            lock (Lock)
            {
                if (ChannelNames.TryGetValue(idx, out n) && n != null && n.Length > 0)
                    return n;
            }
            if (idx == 0)
                return Preset != null ? Preset : "Primary";
            return "Ch " + idx;
        }

        // -------- background loop: connect, read, keep alive --------
        // true once the phone has a real network address (Wi-Fi connected).
        // 127.x is the phone itself, 169.254.x means "no network yet" or USB.
        public static bool HaveNetwork()
        {
            try
            {
                IPAddress[] list = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
                foreach (IPAddress a in list)
                {
                    if (a.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] b = a.GetAddressBytes();
                    if (b[0] == 127) continue;
                    if (b[0] == 169 && b[1] == 254) continue;
                    if (b[0] == 0) continue;
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return true;      // can't tell - just try to connect
            }
        }

        // Don't touch the network until Wi-Fi is up. Trying to connect too
        // early makes Windows Mobile look for other connections and can stop
        // the phone joining its Wi-Fi network by itself (seen at power-on).
        void WaitForWifi()
        {
            if (HaveNetwork()) return;
            DateTime since = DateTime.Now;
            while (running && !HaveNetwork())
            {
                int secs = (int)(DateTime.Now - since).TotalSeconds;
                SetStatus("Waiting for Wi-Fi..." + (secs >= 60
                    ? " (connect to your network in Settings > Wi-Fi)" : ""));
                for (int i = 0; i < 6 && running; i++) Thread.Sleep(500);
            }
        }

        void Run()
        {
            int quickDrops = 0;
            while (running)
            {
                DateTime connectedAt = DateTime.MinValue;
                try
                {
                    WaitForWifi();
                    if (!running) break;
                    SetStatus("Connecting to " + Host + "...");
                    Connect();
                    connectedAt = DateTime.Now;
                    ReadLoop();
                }
                catch (Exception ex)
                {
                    if (!running) break;
                    Drops++;
                    Close();
                    Connected = false;
                    if (connectedAt != DateTime.MinValue &&
                        (DateTime.Now - connectedAt).TotalSeconds < 15)
                        quickDrops++;
                    else
                        quickDrops = 0;
                    int wait = 2;
                    for (int i = 0; i < quickDrops && wait < 30; i++)
                        wait *= 2;
                    if (wait > 30) wait = 30;
                    if (quickDrops >= 3)
                        SetStatus("Keeps getting cut off - another app on the Heltec? Retry in " + wait + " s");
                    else
                        SetStatus("Problem: " + ex.Message + " - retry in " + wait + " s");
                    for (int i = 0; i < wait * 2 && running; i++)
                        Thread.Sleep(500);
                }
            }
        }

        void Connect()
        {
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream,
                                  ProtocolType.Tcp);
            s.Connect(new IPEndPoint(IPAddress.Parse(Host), Port));
            sock = s;
            lock (Lock)
            {
                ChannelNames.Clear();
                ChannelRoles.Clear();
            }
            SetStatus("Loading radio info...");
            // "hello": ToRadio.want_config_id (field 3)
            SendRaw(new PbWriter().Number(3, (ulong)rnd.Next(1, 0x7FFFFFFF)).ToArray());
        }

        void Close()
        {
            Socket s = sock;
            sock = null;
            if (s != null)
            {
                try { s.Close(); }
                catch (Exception) { }
            }
        }

        void ReadLoop()
        {
            byte[] chunk = new byte[1024];
            List<byte> buf = new List<byte>();
            DateTime lastBeat = DateTime.Now;
            while (running)
            {
                Socket s = sock;
                if (s == null) throw new IOException("closed");
                if (s.Poll(500000, SelectMode.SelectRead))
                {
                    int n = s.Receive(chunk);
                    if (n <= 0) throw new IOException("connection closed");
                    for (int i = 0; i < n; i++) buf.Add(chunk[i]);
                    ExtractFrames(buf);
                }
                if ((DateTime.Now - lastBeat).TotalSeconds > 60)
                {
                    // heartbeat: ToRadio field 7 (empty message)
                    SendRaw(new PbWriter().Bytes(7, new byte[0]).ToArray());
                    lastBeat = DateTime.Now;
                }
            }
        }

        // Envelopes: 0x94 0xC3, 2-byte length, then the message
        void ExtractFrames(List<byte> buf)
        {
            while (true)
            {
                int idx = -1;
                for (int i = 0; i + 1 < buf.Count; i++)
                {
                    if (buf[i] == 0x94 && buf[i + 1] == 0xC3) { idx = i; break; }
                }
                if (idx < 0)
                {
                    bool keepLast = buf.Count > 0 && buf[buf.Count - 1] == 0x94;
                    buf.Clear();
                    if (keepLast) buf.Add(0x94);
                    return;
                }
                if (idx > 0) buf.RemoveRange(0, idx);
                if (buf.Count < 4) return;
                int len = (buf[2] << 8) | buf[3];
                if (len > 512)
                {
                    buf.RemoveAt(0);
                    continue;
                }
                if (buf.Count < 4 + len) return;
                byte[] frame = new byte[len];
                for (int i = 0; i < len; i++) frame[i] = buf[4 + i];
                buf.RemoveRange(0, 4 + len);
                try { Handle(frame); }
                catch (Exception) { }   // skip one bad message
            }
        }

        void SendRaw(byte[] payload)
        {
            byte[] frame = new byte[payload.Length + 4];
            frame[0] = 0x94;
            frame[1] = 0xC3;
            frame[2] = (byte)((payload.Length >> 8) & 0xFF);
            frame[3] = (byte)(payload.Length & 0xFF);
            Array.Copy(payload, 0, frame, 4, payload.Length);
            lock (sendLock)
            {
                Socket s = sock;
                if (s == null) throw new IOException("not connected");
                int sent = 0;
                while (sent < frame.Length)
                {
                    int n = s.Send(frame, sent, frame.Length - sent, SocketFlags.None);
                    if (n <= 0) throw new IOException("send failed");
                    sent += n;
                }
            }
        }

        // -------- understanding what the Heltec sends (FromRadio) --------
        void Handle(byte[] frame)
        {
            Pb msg = Pb.Parse(frame);
            bool changed = false;
            lock (Lock)
            {
                if (msg.Has(3))                              // my_info
                {
                    MyNum = (uint)msg.Sub(3).Num(1, 0);
                }
                if (msg.Has(4))                              // node_info
                {
                    Pb ni = msg.Sub(4);
                    uint num = (uint)ni.Num(1, 0);
                    Pb user = ni.Sub(2);
                    if (user != null)
                        SaveName(num, user);
                    if (num == MyNum && MyNum != 0 && ni.Bytes(2) != null)
                        MyUserRaw = ni.Bytes(2);
                    if (ni.Has(7))
                        NodeChannel[num] = (int)ni.Num(7, 0);
                    Node node = GetNode(num);
                    ReadPosition(node, ni.Sub(3));
                    if (ni.Has(4)) node.Snr = ni.Float(4, float.NaN);
                    if (ni.Has(5)) node.Heard = (long)ni.Num(5, 0);
                    ReadMetrics(node, ni.Sub(6));
                    if (ni.Has(7)) node.Channel = (int)ni.Num(7, 0);
                    if (ni.Has(9)) node.Hops = (int)ni.Num(9, 0);
                }
                if (msg.Has(5))                              // config
                {
                    Pb cfg = msg.Sub(5);
                    Pb lora = cfg.Sub(6);
                    if (lora != null)
                    {
                        if (lora.Num(1, 0) != 0)
                        {
                            int p = (int)lora.Num(2, 0);
                            Preset = (p >= 0 && p < Presets.Length) ? Presets[p] : "Preset " + p;
                        }
                        else
                        {
                            Preset = "Custom";
                        }
                    }
                }
                if (msg.Has(10))                             // channel
                {
                    Pb ch = msg.Sub(10);
                    int idx = (int)ch.Num(1, 0);
                    ChannelRoles[idx] = (int)ch.Num(3, 0);
                    if (ch.Num(3, 0) != 0)
                    {
                        Pb settings = ch.Sub(2);
                        string name = settings != null ? settings.Text(3) : null;
                        ChannelNames[idx] = name != null ? name : "";
                    }
                }
                if (msg.Has(2))                              // a packet
                {
                    if (HandlePacket(msg.Sub(2)))
                        changed = true;
                }
            }
            if (msg.Has(7))                                  // config complete
            {
                Connected = true;
                SetStatus("Connected");
                return;
            }
            if (changed) Fire();
            List<ChatMessage> arrived = null;
            lock (Lock)
            {
                if (newMessages.Count > 0)
                {
                    arrived = new List<ChatMessage>(newMessages);
                    newMessages.Clear();
                }
            }
            if (arrived != null)
            {
                MessageEvent ev = Incoming;
                if (ev != null)
                {
                    foreach (ChatMessage cm in arrived)
                    {
                        try { ev(cm); }
                        catch (Exception) { }
                    }
                }
            }
        }

        void SaveName(uint num, Pb user)
        {
            string shortName = user.Text(3);
            string longName = user.Text(2);
            if (longName != null && longName.Length > 0)
                LongNames[num] = longName;
            GetNode(num);
            if (shortName != null && shortName.Length > 0)
                Names[num] = shortName;
            else if (longName != null && longName.Length > 0)
                Names[num] = longName;
        }

        // returns true if a new chat message arrived
        bool HandlePacket(Pb pkt)
        {
            Pb data = pkt.Sub(4);
            if (data == null) return false;          // encrypted, not for us
            int port = (int)data.Num(1, 0);
            uint from = (uint)pkt.Num(1, 0);
            uint to = (uint)pkt.Num(2, BROADCAST);
            uint id = (uint)pkt.Num(6, 0);

            if (port == 5)                           // delivery report
            {
                uint requestId = (uint)data.Num(6, 0);
                byte[] p = data.Bytes(2);
                Pb routing = p != null ? Pb.Parse(p) : new Pb();
                return HandleAck(from, requestId, (int)routing.Num(3, 0));
            }
            // every packet from another node tells us it is alive
            float pktSnr = pkt.Float(8, float.NaN);
            int pktHopStart = (int)pkt.Num(15, 0);
            int pktHops = pktHopStart > 0 ? pktHopStart - (int)pkt.Num(9, 0) : -1;
            if (from != MyNum && from != 0)
            {
                Node node = GetNode(from);
                node.Heard = NowEpoch();
                if (!float.IsNaN(pktSnr)) node.Snr = pktSnr;
                if (pktHops >= 0) node.Hops = pktHops;
                bool viaInternet = pkt.Num(14, 0) != 0;
                if (port != 5)
                {
                    Stats.Packet(from, port == PORT_TEXT);
                    if (Range != null && Range.Active && !viaInternet)
                        Range.Add(node, NodeName(from), pktSnr, pkt.Signed(12, 0),
                                  pktHops, port);
                }
            }

            byte[] payload = data.Bytes(2);
            if (port == 4)                           // node info (names)
            {
                if (payload != null) SaveName(from, Pb.Parse(payload));
                return true;
            }
            if (port == PORT_POSITION)
            {
                if (payload != null) ReadPosition(GetNode(from), Pb.Parse(payload));
                return true;
            }
            if (port == PORT_TELEMETRY)
            {
                if (payload != null)
                {
                    Node tn = GetNode(from);
                    ReadMetrics(tn, Pb.Parse(payload).Sub(2));
                    if (from == MyNum) Stats.ChannelUse(tn.ChannelUse);
                }
                return true;
            }
            if (port == PORT_TRACEROUTE)
            {
                if (to == MyNum && from != MyNum)
                {
                    Trace t = FindTrace((uint)data.Num(6, 0), from);
                    if (t != null)
                    {
                        Pb rd = payload != null ? Pb.Parse(payload) : new Pb();
                        t.Route = RepFixed32(rd, 1);
                        t.SnrTo = RepInt(rd, 2);
                        t.Back = RepFixed32(rd, 3);
                        t.SnrBack = RepInt(rd, 4);
                        t.Status = "done";
                        t.Answered = DateTime.Now;
                    }
                }
                return true;
            }
            if (port != PORT_TEXT || from == MyNum) return false;

            string tag = from.ToString("x") + ":" + id.ToString("x");
            if (id != 0)
            {
                if (seen.Contains(tag)) return false;
                seen.Add(tag);
                if (seen.Count > 200) seen.RemoveAt(0);
            }
            if (to != BROADCAST && to != MyNum) return false;

            ChatMessage m = new ChatMessage();
            m.Time = DateTime.Now;
            m.From = from;
            m.Mine = false;
            byte[] text = data.Bytes(2);
            m.Text = text != null ? Clean(Encoding.UTF8.GetString(text, 0, text.Length)) : "";
            m.Key = (to == BROADCAST) ? "c" + pkt.Num(3, 0) : "d" + from;

            List<string> sig = new List<string>();
            float snr = pkt.Float(8, float.NaN);
            if (!float.IsNaN(snr)) sig.Add("SNR " + snr.ToString("0.0"));
            int rssi = pkt.Signed(12, 0);
            if (rssi != 0) sig.Add(rssi + " dBm");
            int hopStart = (int)pkt.Num(15, 0);
            if (hopStart > 0)
            {
                int hops = hopStart - (int)pkt.Num(9, 0);
                sig.Add(hops == 0 ? "direct" : (hops == 1 ? "1 hop" : hops + " hops"));
            }
            m.Signal = string.Join(", ", sig.ToArray());

            AddMessage(m, true);
            newMessages.Add(m);
            return true;
        }

        // returns true if a message's status changed
        bool HandleAck(uint from, uint requestId, int error)
        {
            if (error != 0)
            {
                foreach (Trace t in Traces)
                {
                    if (t.Id == requestId && t.Status == "waiting")
                    {
                        string reason;
                        if (!RouteErrors.TryGetValue(error, out reason))
                            reason = "error " + error;
                        t.Status = "failed: " + reason;
                        return true;
                    }
                }
            }
            ChatMessage m;
            if (!pending.TryGetValue(requestId, out m)) return false;
            if (error != 0)
            {
                string why;
                if (!RouteErrors.TryGetValue(error, out why)) why = "error " + error;
                m.Status = "failed: " + why;
                pending.Remove(requestId);
            }
            else if (m.DestDm != 0 && from == m.DestDm)
            {
                m.Status = "delivered";
                pending.Remove(requestId);
            }
            else if (m.DestDm != 0)
            {
                if (m.Status == "sending...") m.Status = "relayed, waiting...";
            }
            else
            {
                m.Status = "heard by mesh";
                pending.Remove(requestId);
            }
            return true;
        }

        public static string Clean(string s)
        {
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        void AddMessage(ChatMessage m, bool save)
        {
            Messages.Add(m);
            if (Messages.Count > 300) Messages.RemoveAt(0);
            if (save)
            {
                SaveHistory(m);
                if (!m.Mine && m.Key != Current)
                {
                    int n;
                    Unread.TryGetValue(m.Key, out n);
                    Unread[m.Key] = n + 1;
                }
            }
        }

        // -------- sending --------
        public void SendText(string text, string key)
        {
            if (sock == null || !Connected)
                throw new IOException("not connected");
            uint dest = BROADCAST;
            int channel = 0;
            if (key.StartsWith("c"))
            {
                channel = int.Parse(key.Substring(1));
            }
            else
            {
                dest = uint.Parse(key.Substring(1));
                lock (Lock)
                {
                    if (!NodeChannel.TryGetValue(dest, out channel)) channel = 0;
                }
            }
            uint id = (uint)rnd.Next(1, 0x7FFFFFFF);
            PbWriter data = new PbWriter()
                .Number(1, PORT_TEXT)
                .Bytes(2, Encoding.UTF8.GetBytes(text));
            PbWriter pkt = new PbWriter()
                .Fixed32(2, dest)
                .Number(3, (ulong)channel)
                .Message(4, data)
                .Fixed32(6, id)
                .Number(9, (ulong)HopLimit)
                .Number(10, 1);                     // want_ack

            ChatMessage m = new ChatMessage();
            m.Time = DateTime.Now;
            m.From = MyNum;
            m.Mine = true;
            m.Text = text;
            m.Key = key;
            m.Id = id;
            m.Status = "sending...";
            if (dest != BROADCAST) m.DestDm = dest;
            lock (Lock)
            {
                pending[id] = m;          // ready before any reply arrives
            }
            try
            {
                SendRaw(new PbWriter().Message(1, pkt).ToArray());
            }
            catch (Exception)
            {
                lock (Lock) { pending.Remove(id); }
                throw;
            }
            lock (Lock)
            {
                AddMessage(m, true);
            }
            Fire();
        }

        // -------- history (same file and format as the Python version) --------
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0);

        void SaveHistory(ChatMessage m)
        {
            if (HistoryFile.Length == 0) return;
            try
            {
                long t = (long)(m.Time.ToUniversalTime() - Epoch).TotalSeconds;
                string line = t + "\t" + m.Key + "\t" + (m.Mine ? "1" : "0") + "\t" +
                              m.From + "\t" + Clean(m.Text) + "\n";
                using (FileStream f = new FileStream(HistoryFile, FileMode.Append,
                                                     FileAccess.Write))
                {
                    byte[] b = Encoding.UTF8.GetBytes(line);
                    f.Write(b, 0, b.Length);
                }
            }
            catch (Exception) { }
        }

        public void LoadHistory()
        {
            if (HistoryFile.Length == 0 || !File.Exists(HistoryFile)) return;
            try
            {
                string all;
                using (StreamReader r = new StreamReader(HistoryFile, Encoding.UTF8))
                {
                    all = r.ReadToEnd();
                }
                string[] lines = all.Split('\n');
                int startAt = lines.Length > 300 ? lines.Length - 300 : 0;
                lock (Lock)
                {
                    for (int i = startAt; i < lines.Length; i++)
                    {
                        string[] p = SplitTabs(lines[i], 5);
                        if (p == null) continue;
                        try
                        {
                            ChatMessage m = new ChatMessage();
                            m.Time = Epoch.AddSeconds(double.Parse(p[0])).ToLocalTime();
                            m.Key = p[1];
                            m.Mine = p[2] == "1";
                            m.From = uint.Parse(p[3]);
                            m.Text = p[4].TrimEnd('\r');
                            AddMessage(m, false);
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
