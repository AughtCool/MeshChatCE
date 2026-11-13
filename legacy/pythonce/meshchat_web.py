# meshchat_web.py - version 4
# Meshtastic chat for the AT&T 8525, shown in the phone's web browser.
#
# Runs on:
#   - the 8525 with PythonCE (Python 2.5) - no extra add-ons needed
#   - a desktop computer with Python 3 (for testing)
#
# How to use on the phone:
#   1. Tap this file to start it (a Python window shows its status).
#   2. Open Internet Explorer and go to   http://localhost:8080
#
# Features: channels (with real names like LongFast), direct messages,
# node list, delivery status, signal info per message, your radio's
# status, message history, GPS, quick replies, traceroute, an Info page,
# favorites, history search, mesh stats, a range log, text size and a
# sound when messages arrive.
#
# Tip: start it with MeshChat_start.py instead - it launches faster.

import socket, struct, sys, time, random, threading, os, math, calendar

# ---------------- SETTINGS: change these to match your setup ----------------
HOST = "192.168.4.250"   # your Heltec's IP address (shown on its screen)
PORT = 4403              # Meshtastic's network port (leave as is)
WEB_PORT = 8080          # the chat page:  http://localhost:8080
HOP_LIMIT = 3            # how many times the mesh may relay your message
HEARTBEAT_SECS = 60      # keep-alive so the node doesn't drop us
LIVE_UPDATES = True      # chat redraws only when something changes
                         # (set to False to go back to timed reloads)
REFRESH_SECS = 15        # timed reload, used when LIVE_UPDATES = False
WAIT_SECS = 55           # live updates: redraw at least this often
LIST_REFRESH_SECS = 30   # how often the Nodes and Channels tabs reload
SHOW_MESSAGES = 40       # messages shown per conversation
HISTORY_MAX = 500        # messages kept in the history file
ACK_WAIT_SECS = 120      # after this, "sending..." becomes "sent"

# --- GPS (GlobalSat BT-359 over Bluetooth) ---
GPS_ENABLED = True       # False = hide all GPS features
GPS_START_ON = False     # True = switch the GPS on when MeshChat starts
                         # (otherwise tap "Turn GPS on" in the Info tab)
GPS_GIVE_UP_TRIES = 3    # stop looking after this many failed attempts
GPS_PORT = "COM5:"       # the GPS's Bluetooth COM port - keep the colon!
SHARE_POSITION = True    # send your position to the mesh through the Heltec
SHARE_EVERY_SECS = 300   # how often to update your shared position
UNITS = "mi"             # "mi" for miles/feet, "km" for kilometers/meters

# --- Quick replies (tap to send to the conversation you have open) ---
QUICK_REPLIES = [
    "OK",
    "Yes",
    "No",
    "On my way",
    "Where are you?",
    "Call me",
    "Testing - can you hear me?",
    "Copy that, thanks",
]

# --- Notification sound ---
SOUND_DEFAULT_ON = True  # beep when a message arrives (toggle in Info tab)
SOUND_FOR = "all"        # "all" = every message, "dm" = direct messages only
SOUND_FILE = ""          # optional .wav, e.g. "\\Windows\\Alarm1.wav"
                         # (empty = the phone's normal notification beep)

# --- Range log ---
RANGE_MIN_GAP_SECS = 10  # log at most one line per node this often

# --- Traceroute ---
TRACE_TIMEOUT_SECS = 90  # give up waiting for a traceroute answer after this
TRACE_MIN_GAP_SECS = 30  # the Heltec limits how often traces can be sent
# ---------------------------------------------------------------------------

if len(sys.argv) > 1:
    HOST = sys.argv[1]

PY3 = sys.version_info[0] >= 3
if PY3:
    long_type = int
else:
    long_type = long
BROADCAST = 0xFFFFFFFF   # "send to everyone"
if PY3:
    from http.server import BaseHTTPRequestHandler, HTTPServer
    from socketserver import ThreadingMixIn
    from urllib.parse import parse_qs, urlparse
else:
    from BaseHTTPServer import BaseHTTPRequestHandler, HTTPServer
    from SocketServer import ThreadingMixIn
    from cgi import parse_qs
    from urlparse import urlparse

# --- "something changed" signal, so the chat only redraws when needed ---
changes = threading.Condition()
change_count = [0]

def bump():
    changes.acquire()
    try:
        change_count[0] += 1
        changes.notifyAll()
    finally:
        changes.release()

def wait_for_change(seen, secs):
    changes.acquire()
    try:
        end = time.time() + secs
        while change_count[0] == seen:
            left = end - time.time()
            if left <= 0:
                break
            changes.wait(left)
    finally:
        changes.release()

# History file lives next to this script
try:
    BASE_DIR = os.path.dirname(__file__)
except Exception:
    BASE_DIR = ''
if not BASE_DIR:
    BASE_DIR = '.'
HISTORY_FILE = os.path.join(BASE_DIR, 'meshchat_history.txt')
SETTINGS_FILE = os.path.join(BASE_DIR, 'meshchat_settings.txt')
RANGE_FILE = os.path.join(BASE_DIR, 'meshchat_range.csv')

# --- Settings you change from the pages (saved on the card) ---
prefs = {'text': 'small', 'sound': SOUND_DEFAULT_ON and 'on' or 'off',
         'favs': ''}

def load_prefs():
    try:
        f = open(SETTINGS_FILE, 'r')
        try:
            for line in f.readlines():
                if '=' in line:
                    k, v = line.strip().split('=', 1)
                    if k in prefs:
                        prefs[k] = v
        finally:
            f.close()
    except Exception:
        pass

def save_prefs():
    try:
        f = open(SETTINGS_FILE, 'w')
        try:
            for k in prefs:
                f.write("%s=%s\n" % (k, prefs[k]))
        finally:
            f.close()
    except Exception:
        pass

def favorites():
    result = []
    for x in prefs['favs'].split(','):
        x = x.strip()
        if x:
            try:
                result.append(int(x, 16))
            except ValueError:
                pass
    return result

def toggle_favorite(num):
    favs = favorites()
    if num in favs:
        favs.remove(num)
    else:
        favs.append(num)
    prefs['favs'] = ','.join(["%x" % n for n in favs])
    save_prefs()

# --- Notification sound ---
_beeper = [None]

def find_beeper():
    """Find the phone's sound functions (Windows Mobile coredll)."""
    try:
        import ctypes
        lib = None
        for name in ('windll', 'cdll'):
            try:
                lib = getattr(ctypes, name).coredll
                break
            except Exception:
                pass
        return lib
    except Exception:
        return None

def notify_sound():
    if prefs['sound'] != 'on':
        return
    try:
        if _beeper[0] is None:
            _beeper[0] = find_beeper() or False
        lib = _beeper[0]
        if not lib:
            return
        if SOUND_FILE:
            lib.PlaySoundW(unicode_text(SOUND_FILE), None, 0x20001)
        else:
            lib.MessageBeep(0x40)          # the "notification" sound
    except Exception:
        pass

def unicode_text(s):
    if PY3:
        return s
    try:
        return s.decode('utf-8')
    except Exception:
        return unicode(s)

# --- Range log ---
class RangeLog:
    def __init__(self):
        self.active = False
        self.lines = 0
        self.started = 0
        self.last = {}            # node -> time of last line

    def start(self):
        new = not os.path.exists(RANGE_FILE)
        try:
            f = open(RANGE_FILE, 'a')
            try:
                if new:
                    f.write("time,my_lat,my_lon,gps_fix,from_id,from_name,"
                            "snr_db,rssi_dbm,hops,distance_m,their_lat,"
                            "their_lon,port\n")
            finally:
                f.close()
        except Exception:
            return "Could not open %s" % RANGE_FILE
        self.active = True
        self.lines = 0
        self.started = time.time()
        self.last = {}
        return ''

    def stop(self):
        self.active = False

    def add(self, sender, name, snr, rssi, hops, port, node):
        if not self.active:
            return
        now = time.time()
        if now - self.last.get(sender, 0) < RANGE_MIN_GAP_SECS:
            return
        self.last[sender] = now
        lat = lon = ''
        fix = 'no'
        dist = ''
        if gps.has_fix():
            lat = "%.6f" % gps.lat
            lon = "%.6f" % gps.lon
            fix = 'yes'
        tlat = tlon = ''
        if node.get('lat') is not None:
            tlat = "%.6f" % node['lat']
            tlon = "%.6f" % node['lon']
            if fix == 'yes':
                dist = "%d" % distance_and_direction(gps.lat, gps.lon,
                                                     node['lat'],
                                                     node['lon'])[0]
        def num(v, fmt):
            if v is None:
                return ''
            return fmt % v
        row = [time.strftime("%Y-%m-%d %H:%M:%S"), lat, lon, fix,
               "!%08x" % sender, name.replace(',', ' '),
               num(snr, "%.2f"), num(rssi, "%d"), num(hops, "%d"),
               dist, tlat, tlon, str(port)]
        try:
            f = open(RANGE_FILE, 'a')
            try:
                f.write(",".join(row) + "\n")
            finally:
                f.close()
            self.lines += 1
        except Exception:
            pass

rangelog = RangeLog()

# --- Mesh statistics (kept for the last 24 hours, not saved) ---
class MeshStats:
    def __init__(self):
        self.hours = {}           # hour number -> dict
        self.by_node = {}         # node -> packets heard
        self.started = time.time()

    def hour(self):
        h = int(time.time() // 3600)
        rec = self.hours.get(h)
        if rec is None:
            rec = {'pkts': 0, 'msgs': 0, 'nodes': {}, 'ch': []}
            self.hours[h] = rec
            for old in list(self.hours.keys()):
                if old < h - 23:
                    del self.hours[old]
        return rec

    def packet(self, sender, is_text):
        rec = self.hour()
        rec['pkts'] += 1
        if is_text:
            rec['msgs'] += 1
        rec['nodes'][sender] = 1
        self.by_node[sender] = self.by_node.get(sender, 0) + 1

    def channel_use(self, value):
        if value is not None:
            self.hour()['ch'].append(value)

meshstats = MeshStats()

# Meshtastic numbers we use
PORT_TEXT = 1
PORT_POSITION = 3
PORT_NODEINFO = 4
PORT_ROUTING = 5
PORT_TELEMETRY = 67
PORT_TRACEROUTE = 70

VERSION = "v4"

PRESETS = {0: "LongFast", 1: "LongSlow", 2: "VLongSlow", 3: "MediumSlow",
           4: "MediumFast", 5: "ShortSlow", 6: "ShortFast", 7: "LongMod",
           8: "ShortTurbo", 9: "LongTurbo"}

ROUTE_ERRORS = {1: "no route", 2: "refused", 3: "timed out",
                4: "no radio interface", 5: "not heard by anyone",
                6: "channel not found", 7: "too large", 8: "no response",
                9: "duty-cycle limit", 32: "bad request",
                33: "not authorized", 34: "encryption failed",
                35: "unknown key"}

# --- small helpers so the same code works on Python 2.5 and Python 3 ---
# Text is kept as "native" strings: str on Python 3, UTF-8 bytes on Python 2.
if PY3:
    def b(ints):
        return bytes(ints)
    def ordat(buf, i):
        return buf[i]
    EMPTY = bytes()
    def to_bytes(text):
        return text.encode('utf-8')
    def to_text(raw):
        return raw.decode('utf-8', 'replace')
else:
    def b(ints):
        return ''.join([chr(x) for x in ints])
    def ordat(buf, i):
        return ord(buf[i])
    EMPTY = ''
    def to_bytes(text):
        return text
    def to_text(raw):
        return raw

def out(s):
    try:
        sys.stdout.write(s + "\n")
        sys.stdout.flush()
    except Exception:
        pass

# --- Protobuf: just enough to read and write Meshtastic's "forms" ---
def enc_varint(n):
    parts = []
    while True:
        byte = n & 0x7F
        n >>= 7
        if n:
            parts.append(byte | 0x80)
        else:
            parts.append(byte)
            break
    return b(parts)

def dec_varint(buf, pos):
    result = 0
    shift = 0
    while True:
        c = ordat(buf, pos)
        pos += 1
        result |= (c & 0x7F) << shift
        if not (c & 0x80):
            return result, pos
        shift += 7

def f_varint(num, val):
    return enc_varint((num << 3) | 0) + enc_varint(val)

def f_bytes(num, data):
    return enc_varint((num << 3) | 2) + enc_varint(len(data)) + data

def f_fixed32(num, val):
    return enc_varint((num << 3) | 5) + struct.pack('<I', val)

def parse(buf):
    """Turn a form into {field number: [values]}."""
    fields = {}
    pos = 0
    end = len(buf)
    while pos < end:
        key, pos = dec_varint(buf, pos)
        num = key >> 3
        wt = key & 7
        if wt == 0:
            val, pos = dec_varint(buf, pos)
        elif wt == 1:
            val = buf[pos:pos + 8]
            pos += 8
        elif wt == 2:
            ln, pos = dec_varint(buf, pos)
            val = buf[pos:pos + ln]
            pos += ln
        elif wt == 5:
            val = struct.unpack('<I', buf[pos:pos + 4])[0]
            pos += 4
        else:
            raise ValueError("bad wire type %d" % wt)
        fields.setdefault(num, []).append(val)
    return fields

def first(fields, num, default=None):
    v = fields.get(num)
    if v:
        return v[0]
    return default

def rep_fixed32(fields, num):
    """A list of 4-byte numbers (node IDs), packed or not."""
    result = []
    for v in fields.get(num, []):
        if isinstance(v, (int, long_type)):
            result.append(v)
        else:
            for i in range(0, len(v) - 3, 4):
                result.append(struct.unpack('<I', v[i:i + 4])[0])
    return result

def rep_int(fields, num):
    """A list of small signed numbers (SNR x 4), packed or not."""
    result = []
    for v in fields.get(num, []):
        if isinstance(v, (int, long_type)):
            result.append(as_signed(v))
        else:
            pos = 0
            while pos < len(v):
                n, pos = dec_varint(v, pos)
                result.append(as_signed(n))
    return result

def as_float(v):
    """A 4-byte float field arrives as a number; turn it back into a float."""
    if v is None:
        return None
    return struct.unpack('<f', struct.pack('<I', v & 0xFFFFFFFF))[0]

def as_signed(v):
    """Negative numbers (like signal strength) arrive as huge numbers."""
    if v is None:
        return None
    if v >= 2 ** 63:
        v -= 2 ** 64
    elif v >= 2 ** 31 and v < 2 ** 32:
        v -= 2 ** 32
    return v

# --- Envelopes: 0x94 0xC3, 2-byte length, then the form ---
MAGIC = b([0x94, 0xC3])

def frame(payload):
    n = len(payload)
    return b([0x94, 0xC3, (n >> 8) & 0xFF, n & 0xFF]) + payload

def extract_frames(buf):
    frames = []
    while True:
        idx = buf.find(MAGIC)
        if idx < 0:
            if len(buf) and ordat(buf, len(buf) - 1) == 0x94:
                buf = buf[-1:]
            else:
                buf = EMPTY
            return frames, buf
        buf = buf[idx:]
        if len(buf) < 4:
            return frames, buf
        n = (ordat(buf, 2) << 8) | ordat(buf, 3)
        if n > 512:
            buf = buf[1:]
            continue
        if len(buf) < 4 + n:
            return frames, buf
        frames.append(buf[4:4 + n])
        buf = buf[4 + n:]

# --- Conversations ---
# A conversation key is "c<channel number>" or "d<node number>".
def conv_channel(key):
    return int(key[1:])

def clean(text):
    return text.replace('\t', ' ').replace('\r', ' ').replace('\n', ' ')

# --- The Meshtastic connection and everything it knows ---
class Mesh:
    def __init__(self):
        self.sock = None
        self.sock_lock = threading.Lock()
        self.lock = threading.Lock()      # protects everything below
        self.connected = False
        self.status = "Starting ..."
        self.my_num = None
        self.nodes = {}       # num -> dict(short, long, heard, snr, hops, bat...)
        self.channels = {}    # index -> dict(name, role)
        self.preset = None    # LoRa preset name, e.g. "LongFast"
        self.messages = {}    # conversation key -> list of message dicts
        self.unread = {}      # conversation key -> count
        self.pending = {}     # packet id -> message dict (waiting for ack)
        self.seen = []        # recent (from, id) pairs, to skip duplicates
        self.current = "c0"   # conversation shown in the chat tab
        self.running = True
        self.traces = []      # recent traceroutes, newest first
        self.stats = {'started': time.time(), 'connects': 0, 'drops': 0,
                      'last_error': '', 'last_error_t': 0,
                      'connected_since': 0, 'frames_in': 0, 'frames_out': 0,
                      'last_rx': 0, 'msgs_in': 0, 'msgs_out': 0}

    # ---------- names ----------
    def node_name(self, num):
        n = self.nodes.get(num)
        if n and n.get('short'):
            return n['short']
        if n and n.get('long'):
            return n['long']
        return "!%08x" % num

    def channel_name(self, idx):
        ch = self.channels.get(idx)
        if ch and ch.get('name'):
            return ch['name']
        if idx == 0:
            return self.preset or "Primary"
        return "Ch %d" % idx

    def conv_title(self, key):
        if key[0] == 'c':
            return "#" + self.channel_name(conv_channel(key))
        return "DM " + self.node_name(int(key[1:]))

    # ---------- messages ----------
    def add_message(self, key, msg, save=True, count_unread=True):
        lst = self.messages.setdefault(key, [])
        lst.append(msg)
        if len(lst) > SHOW_MESSAGES * 3:
            del lst[0]
        if count_unread and not msg['mine'] and key != self.current:
            self.unread[key] = self.unread.get(key, 0) + 1
        if save:
            save_history(key, msg)
            if msg['mine']:
                self.stats['msgs_out'] += 1
            else:
                self.stats['msgs_in'] += 1
                if SOUND_FOR != "dm" or key[0] == 'd':
                    notify_sound()
        bump()

    # ---------- connection ----------
    def connect(self):
        self.status = "Connecting to %s ..." % HOST
        bump()
        out("-- " + self.status)
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.settimeout(10)
        s.connect((HOST, PORT))
        s.settimeout(5)
        self.sock = s
        self.lock.acquire()
        try:
            self.channels = {}
            self.status = "Loading radio info ..."
            bump()
        finally:
            self.lock.release()
        self.send_raw(f_varint(3, random.randint(1, 0x7FFFFFFF)))

    def close(self):
        self.sock_lock.acquire()
        try:
            if self.sock is not None:
                try:
                    self.sock.close()
                except Exception:
                    pass
            self.sock = None
        finally:
            self.sock_lock.release()
        self.connected = False

    def send_raw(self, payload):
        self.sock_lock.acquire()
        try:
            if self.sock is None:
                raise IOError("not connected")
            self.sock.sendall(frame(payload))
            self.stats['frames_out'] += 1
        finally:
            self.sock_lock.release()

    def send_text(self, key, text):
        """Send text to a conversation. Returns the message dict."""
        if key[0] == 'c':
            dest = BROADCAST
            chan = conv_channel(key)
        else:
            dest = int(key[1:])
            node = self.nodes.get(dest, {})
            chan = node.get('channel', 0)
        pid = random.randint(1, 0x7FFFFFFF)
        data = f_varint(1, PORT_TEXT) + f_bytes(2, to_bytes(text))
        pkt = (f_fixed32(2, dest) + f_varint(3, chan) + f_bytes(4, data) +
               f_fixed32(6, pid) + f_varint(9, HOP_LIMIT) + f_varint(10, 1))
        msg = {'t': time.time(), 'frm': self.my_num or 0, 'mine': True,
               'text': text, 'id': pid, 'status': 'sending', 'sig': ''}
        if dest != BROADCAST:
            msg['dest_dm'] = dest
        self.lock.acquire()
        try:
            self.pending[pid] = msg       # ready before any reply arrives
        finally:
            self.lock.release()
        try:
            self.send_raw(f_bytes(1, pkt))
        except Exception:
            self.lock.acquire()
            try:
                del self.pending[pid]
            finally:
                self.lock.release()
            raise
        self.lock.acquire()
        try:
            self.add_message(key, msg)
        finally:
            self.lock.release()
        return msg

    def send_position(self, lat, lon, alt, utc, sats):
        """Give the Heltec our GPS position, like the phone apps do.
        The Heltec then shares it on the mesh on its own schedule."""
        pos = (enc_varint((1 << 3) | 5) +
               struct.pack('<i', int(round(lat * 1e7))) +
               enc_varint((2 << 3) | 5) +
               struct.pack('<i', int(round(lon * 1e7))))
        if alt is not None:
            pos += f_varint(3, int(round(alt)) & 0xFFFFFFFFFFFFFFFF)
        if utc:
            pos += f_fixed32(4, int(utc))
        pos += f_varint(5, 3)                 # source: external GPS
        if sats:
            pos += f_varint(19, sats)
        data = f_varint(1, PORT_POSITION) + f_bytes(2, pos)
        pkt = (f_fixed32(2, self.my_num) + f_bytes(4, data) +
               f_fixed32(6, random.randint(1, 0x7FFFFFFF)))
        self.send_raw(f_bytes(1, pkt))
        self.lock.acquire()
        try:
            me = self.node(self.my_num)
            me['lat'] = lat
            me['lon'] = lon
        finally:
            self.lock.release()

    def send_trace(self, num):
        """Ask the mesh for the route to a node. Returns a note for the user."""
        now = time.time()
        self.lock.acquire()
        try:
            if self.traces and now - self.traces[0]['t'] < TRACE_MIN_GAP_SECS:
                return ("Please wait %d s between traceroutes."
                        % int(TRACE_MIN_GAP_SECS - (now - self.traces[0]['t'])))
            chan = self.nodes.get(num, {}).get('channel', 0)
        finally:
            self.lock.release()
        pid = random.randint(1, 0x7FFFFFFF)
        data = (f_varint(1, PORT_TRACEROUTE) + f_bytes(2, EMPTY) +
                f_varint(3, 1))                       # want_response
        pkt = (f_fixed32(2, num) + f_varint(3, chan) + f_bytes(4, data) +
               f_fixed32(6, pid) + f_varint(9, HOP_LIMIT))
        trace = {'num': num, 't': now, 'id': pid, 'status': 'waiting',
                 'route': [], 'snr_to': [], 'back': [], 'snr_back': []}
        self.lock.acquire()
        try:
            self.traces.insert(0, trace)
            del self.traces[10:]
        finally:
            self.lock.release()
        try:
            self.send_raw(f_bytes(1, pkt))
        except Exception:
            trace['status'] = 'not sent: %s' % sys.exc_info()[1]
        bump()
        return ''

    def find_trace(self, request_id, sender):
        for tr in self.traces:
            if request_id and tr['id'] == request_id:
                return tr
        for tr in self.traces:                # fallback: newest waiting one
            if tr['status'] == 'waiting' and tr['num'] == sender:
                return tr
        return None

    def handle_trace_reply(self, sender, request_id, payload):
        tr = self.find_trace(request_id, sender)
        if tr is None:
            return
        rd = parse(payload)
        tr['route'] = rep_fixed32(rd, 1)
        tr['snr_to'] = rep_int(rd, 2)
        tr['back'] = rep_fixed32(rd, 3)
        tr['snr_back'] = rep_int(rd, 4)
        tr['status'] = 'done'
        tr['done_t'] = time.time()
        bump()

    # ---------- understanding what the radio sends ----------
    def handle(self, payload):
        msg = parse(payload)
        self.lock.acquire()
        try:
            if 3 in msg:                                  # my_info
                self.my_num = first(parse(msg[3][0]), 1)
            if 4 in msg:                                  # node_info
                self.handle_nodeinfo(parse(msg[4][0]))
            if 5 in msg:                                  # config
                cfg = parse(msg[5][0])
                if 6 in cfg:                              # lora config
                    lora = parse(cfg[6][0])
                    if first(lora, 1, 0):
                        self.preset = PRESETS.get(first(lora, 2, 0),
                                                  "Preset %d" % first(lora, 2, 0))
                    else:
                        self.preset = "Custom"
            if 10 in msg:                                 # channel
                ch = parse(msg[10][0])
                idx = first(ch, 1, 0)
                role = first(ch, 3, 0)
                if role:
                    settings = parse(first(ch, 2, EMPTY))
                    name = to_text(first(settings, 3, EMPTY))
                    self.channels[idx] = {'name': name, 'role': role}
                elif idx in self.channels:
                    del self.channels[idx]
            if 7 in msg:                                  # config complete
                self.connected = True
                self.status = "Connected"
                self.stats['connects'] += 1
                self.stats['connected_since'] = time.time()
                bump()
                out("-- Connected - %d nodes, %d channels"
                    % (len(self.nodes), len(self.channels)))
            if 2 in msg:                                  # packet
                self.handle_packet(parse(msg[2][0]))
        finally:
            self.lock.release()

    def node(self, num):
        n = self.nodes.get(num)
        if n is None:
            n = {}
            self.nodes[num] = n
        return n

    def handle_user(self, n, user):
        long_name = first(user, 2)
        short_name = first(user, 3)
        if long_name is not None:
            n['long'] = to_text(long_name)
        if short_name is not None:
            n['short'] = to_text(short_name)

    def handle_metrics(self, n, dm):
        if 1 in dm:
            n['bat'] = first(dm, 1)
        if 2 in dm:
            n['volt'] = as_float(first(dm, 2))
        if 3 in dm:
            n['chutil'] = as_float(first(dm, 3))
        if 4 in dm:
            n['airtx'] = as_float(first(dm, 4))
        if 5 in dm:
            n['uptime'] = first(dm, 5)

    def handle_nodeinfo(self, ni):
        num = first(ni, 1)
        if num is None:
            return
        n = self.node(num)
        if 2 in ni:
            self.handle_user(n, parse(ni[2][0]))
        if 4 in ni:
            n['snr'] = as_float(first(ni, 4))
        if 5 in ni:
            n['heard'] = first(ni, 5)
        if 6 in ni:
            self.handle_metrics(n, parse(ni[6][0]))
        if 7 in ni:
            n['channel'] = first(ni, 7)
        if 9 in ni:
            n['hops'] = first(ni, 9)
        if 3 in ni:
            self.handle_position(n, parse(ni[3][0]))

    def handle_position(self, n, pos):
        lat = as_signed(first(pos, 1))
        lon = as_signed(first(pos, 2))
        if lat and lon:
            n['lat'] = lat / 1e7
            n['lon'] = lon / 1e7

    def handle_packet(self, pkt):
        data = first(pkt, 4)
        if data is None:                  # encrypted, not for us
            return
        d = parse(data)
        port = first(d, 1, 0)
        sender = first(pkt, 1, 0)
        dest = first(pkt, 2, BROADCAST)
        pid = first(pkt, 6, 0)
        payload = first(d, 2, EMPTY)

        if port == PORT_ROUTING:          # delivery reports
            self.handle_ack(sender, first(d, 6), parse(payload))
            return

        # skip duplicates
        if pid:
            tag = (sender, pid)
            if tag in self.seen:
                return
            self.seen.append(tag)
            if len(self.seen) > 200:
                del self.seen[0]

        # signal details
        snr = as_float(first(pkt, 8))
        rssi = as_signed(first(pkt, 12))
        hop_start = first(pkt, 15, 0)
        hop_limit = first(pkt, 9, 0)
        hops = None
        if hop_start:
            hops = hop_start - hop_limit
        via_mqtt = first(pkt, 14, 0)

        if sender != self.my_num:
            n = self.node(sender)
            n['heard'] = int(time.time())
            if snr is not None:
                n['snr'] = snr
            if hops is not None:
                n['hops'] = hops
            meshstats.packet(sender, port == PORT_TEXT)
            if rangelog.active and not via_mqtt:
                rangelog.add(sender, self.node_name(sender), snr, rssi,
                             hops, port, n)

        if port == PORT_TRACEROUTE:
            if dest == self.my_num and sender != self.my_num:
                self.handle_trace_reply(sender, first(d, 6), payload)
            return

        if port == PORT_NODEINFO:
            self.handle_user(self.node(sender), parse(payload))
        elif port == PORT_POSITION:
            self.handle_position(self.node(sender), parse(payload))
        elif port == PORT_TELEMETRY:
            tel = parse(payload)
            if 2 in tel:
                self.handle_metrics(self.node(sender), parse(tel[2][0]))
                if sender == self.my_num:
                    meshstats.channel_use(self.node(sender).get('chutil'))
        elif port == PORT_TEXT:
            if sender == self.my_num:
                return
            if dest == BROADCAST:
                key = "c%d" % first(pkt, 3, 0)
            else:
                key = "d%d" % sender
            sig = []
            if via_mqtt:
                sig.append("via internet")
            if snr is not None and not via_mqtt:
                sig.append("SNR %.1f" % snr)
            if rssi:
                sig.append("%d dBm" % rssi)
            if hops is not None:
                if hops == 0:
                    sig.append("direct")
                elif hops == 1:
                    sig.append("1 hop")
                else:
                    sig.append("%d hops" % hops)
            text = to_text(payload)
            if first(d, 8, 0):            # a reaction (tapback)
                text = "(reacted) " + text
            msg = {'t': time.time(), 'frm': sender, 'mine': False,
                   'text': clean(text), 'id': pid, 'status': '',
                   'sig': ", ".join(sig)}
            self.add_message(key, msg)

    def handle_ack(self, sender, request_id, routing):
        self.update_ack(sender, request_id, routing)
        bump()

    def update_ack(self, sender, request_id, routing):
        err = first(routing, 3, 0)
        for tr in self.traces:                # a traceroute that failed
            if tr['id'] == request_id and tr['status'] == 'waiting' and err:
                tr['status'] = "failed: " + ROUTE_ERRORS.get(
                    err, "error %d" % err)
                return
        msg = self.pending.get(request_id)
        if msg is None:
            return
        err = first(routing, 3, 0)
        if err:
            msg['status'] = "failed: " + ROUTE_ERRORS.get(err, "error %d" % err)
            del self.pending[request_id]
            return
        if msg.get('dest_dm') and sender == msg['dest_dm']:
            msg['status'] = 'delivered'
            del self.pending[request_id]
        elif msg.get('dest_dm'):
            if msg['status'] == 'sending':
                msg['status'] = 'relayed, waiting...'
        else:
            msg['status'] = 'heard by mesh'
            del self.pending[request_id]

    # ---------- background loop ----------
    def reader(self):
        buf = EMPTY
        last_beat = time.time()
        connected_at = 0
        quick_drops = 0       # connections cut right after connecting
        while self.running:
            try:
                if self.sock is None:
                    self.connect()
                    connected_at = time.time()
                    buf = EMPTY
                    last_beat = time.time()
                try:
                    chunk = self.sock.recv(1024)
                except socket.timeout:
                    chunk = None
                if chunk is not None:
                    if not chunk:
                        raise IOError("connection closed")
                    frames, buf = extract_frames(buf + chunk)
                    if frames:
                        self.stats['frames_in'] += len(frames)
                        self.stats['last_rx'] = time.time()
                    for f in frames:
                        try:
                            self.handle(f)
                        except Exception:
                            pass
                if time.time() - last_beat > HEARTBEAT_SECS:
                    self.send_raw(f_bytes(7, EMPTY))
                    last_beat = time.time()
            except Exception:
                if not self.running:
                    break
                problem = sys.exc_info()[1]
                self.stats['drops'] += 1
                self.stats['last_error'] = str(problem)
                self.stats['last_error_t'] = time.time()
                if connected_at and time.time() - connected_at < 15:
                    quick_drops += 1
                else:
                    quick_drops = 0
                wait = min(2 * (2 ** quick_drops), 30)
                if quick_drops >= 3:
                    self.status = ("Keeps getting cut off - is another app "
                                   "connected to the Heltec? Retry in %ds"
                                   % wait)
                else:
                    self.status = "Problem: %s - retry in %ds" % (problem, wait)
                bump()
                out("-- " + self.status)
                self.close()
                time.sleep(wait)

# --- GPS reader (runs in the background) ---
def nmea_ok(line):
    if '*' not in line:
        return False
    body, given = line[1:].split('*', 1)
    calc = 0
    for ch in body:
        calc ^= ord(ch)
    try:
        return calc == int(given[:2], 16)
    except ValueError:
        return False

def nmea_degrees(value, hemi):
    if not value:
        return None
    dot = value.find('.')
    if dot < 0:
        dot = len(value)
    result = float(value[:dot - 2]) + float(value[dot - 2:]) / 60.0
    if hemi in ('S', 'W'):
        result = -result
    return result

class GPS:
    def __init__(self):
        self.status = "off"
        self.fix = False
        self.sats = 0
        self.lat = None
        self.lon = None
        self.alt = None
        self.utc = None           # GPS time, seconds since 1970
        self.last_fix = 0         # when (phone clock) we last had a fix
        self.last_shared = 0
        self.running = True
        self.wanted = False       # the user switched the GPS on
        self.active = False       # the reader thread is running
        self.ctl = threading.Lock()

    def turn_on(self):
        self.ctl.acquire()
        try:
            self.wanted = True
            self.status = "starting"
            if not self.active:
                self.active = True
                t = threading.Thread(target=self.reader)
                if PY3:
                    t.daemon = True
                else:
                    t.setDaemon(True)
                t.start()
        finally:
            self.ctl.release()
        bump()

    def turn_off(self, why="off"):
        self.wanted = False       # the reader notices and closes the port
        self.fix = False
        self.status = why
        bump()

    def is_on(self):
        return self.wanted

    def has_fix(self):
        return self.fix and time.time() - self.last_fix < 10

    def handle(self, line):
        parts = line.split('*')[0].split(',')
        kind = parts[0][3:]
        try:
            if kind == 'GGA' and len(parts) >= 10:
                self.sats = int(parts[7] or 0)
                ok = parts[6] not in ('', '0')
                if ok and parts[2] and parts[4]:
                    self.lat = nmea_degrees(parts[2], parts[3])
                    self.lon = nmea_degrees(parts[4], parts[5])
                    if parts[9]:
                        self.alt = float(parts[9])
                    self.last_fix = time.time()
                self.fix = ok
            elif kind == 'RMC' and len(parts) >= 10:
                hms = parts[1]
                dmy = parts[9]
                if parts[2] == 'A' and len(hms) >= 6 and len(dmy) == 6:
                    self.utc = calendar.timegm((
                        2000 + int(dmy[4:6]), int(dmy[2:4]), int(dmy[0:2]),
                        int(hms[0:2]), int(hms[2:4]), int(hms[4:6]),
                        0, 0, 0))
        except ValueError:
            pass

    def reader(self):
        try:
            self.read_loop()
        finally:
            self.ctl.acquire()
            try:
                self.active = False
            finally:
                self.ctl.release()

    def read_loop(self):
        fails = 0
        port = None
        while self.running and self.wanted:
            try:
                self.status = "connecting"
                bump()
                port = open(GPS_PORT, 'rb')
                self.status = "waiting for data"
                quiet_since = time.time()
                while self.running and self.wanted:
                    raw = port.readline()
                    if not raw:
                        if time.time() - quiet_since > 30:
                            raise IOError("no data from GPS")
                        time.sleep(0.2)
                        continue
                    quiet_since = time.time()
                    fails = 0                     # GPS is talking again
                    if not isinstance(raw, str):
                        raw = raw.decode('ascii', 'replace')
                    line = raw.strip()
                    if line.startswith('$') and nmea_ok(line):
                        self.handle(line)
                        if self.has_fix():
                            self.status = "fix"
                        else:
                            self.status = "searching"
                        self.maybe_share()
                port.close()                      # switched off by the user
                return
            except Exception:
                self.fix = False
                try:
                    port.close()
                except Exception:
                    pass
                if not self.wanted:
                    return
                fails += 1
                if fails >= GPS_GIVE_UP_TRIES:
                    # stop searching - it upsets the phone's Wi-Fi
                    self.turn_off("not found - switched off")
                    out("-- GPS not found, stopped looking for it")
                    return
                self.status = "not found, retry %d of %d" % (
                    fails + 1, GPS_GIVE_UP_TRIES)
                bump()
                end = time.time() + 15
                while time.time() < end and self.wanted:
                    time.sleep(0.5)

    def maybe_share(self):
        """Send our position to the Heltec, which shares it on the mesh."""
        if not SHARE_POSITION or not self.has_fix() or self.utc is None:
            return
        if time.time() - self.last_shared < SHARE_EVERY_SECS:
            return
        if mesh.my_num is None or mesh.sock is None or not mesh.connected:
            return
        try:
            mesh.send_position(self.lat, self.lon, self.alt, self.utc,
                               self.sats)
            self.last_shared = time.time()
        except Exception:
            pass

gps = GPS()

mesh = Mesh()

# --- Saved history ---
def save_history(key, msg):
    try:
        line = "%d\t%s\t%d\t%d\t%s\n" % (int(msg['t']), key,
                                         msg['mine'] and 1 or 0,
                                         msg['frm'] or 0, clean(msg['text']))
        f = open(HISTORY_FILE, 'ab')
        try:
            f.write(to_bytes(line))
        finally:
            f.close()
    except Exception:
        pass

def load_history():
    try:
        f = open(HISTORY_FILE, 'rb')
        try:
            raw = f.read()
        finally:
            f.close()
    except Exception:
        return
    if PY3:
        raw = raw.decode('utf-8', 'replace')
    lines = [x for x in raw.split('\n') if x.strip()]
    if len(lines) > HISTORY_MAX:          # trim the file
        lines = lines[-HISTORY_MAX:]
        try:
            f = open(HISTORY_FILE, 'wb')
            try:
                f.write(to_bytes('\n'.join(lines) + '\n'))
            finally:
                f.close()
        except Exception:
            pass
    for line in lines:
        parts = line.split('\t', 4)
        if len(parts) != 5:
            continue
        try:
            msg = {'t': int(parts[0]), 'frm': int(parts[3]),
                   'mine': parts[2] == '1', 'text': parts[4], 'id': 0,
                   'status': '', 'sig': ''}
            mesh.add_message(parts[1], msg, save=False, count_unread=False)
        except Exception:
            pass
    out("-- loaded %d saved messages" % len(lines))

# --- The pages ---
def esc(s):
    return (s.replace("&", "&amp;").replace("<", "&lt;")
             .replace(">", "&gt;").replace('"', "&quot;"))

TEXT_SIZES = {'small': 'small', 'normal': 'medium', 'large': 'large'}

def page_head(refresh=''):
    r = ''
    if refresh:
        r = '<meta http-equiv="refresh" content="%s">' % refresh
    size = TEXT_SIZES.get(prefs['text'], 'small')
    return ('<html><head><meta http-equiv="Content-Type" '
            'content="text/html; charset=utf-8">%s<title>MeshChat</title>'
            '</head><body style="font-family:Tahoma,sans-serif;'
            'font-size:%s;margin:2px">' % (r, size))

def ago(t):
    if not t:
        return "?"
    d = int(time.time() - t)
    if d < 0 or d > 365 * 86400:
        return "?"
    if d < 60:
        return "now"
    if d < 3600:
        return "%dm ago" % (d // 60)
    if d < 86400:
        return "%dh ago" % (d // 3600)
    return "%dd ago" % (d // 86400)

def when(t):
    if time.strftime("%Y%m%d", time.localtime(t)) == time.strftime("%Y%m%d"):
        return time.strftime("%H:%M", time.localtime(t))
    return time.strftime("%m/%d %H:%M", time.localtime(t))

def grey(s):
    return '<font color="#777777" size="1">%s</font>' % s

def header():
    m = mesh
    parts = []
    if m.connected:
        parts.append('<font color="green"><b>Connected</b></font>')
    else:
        parts.append('<font color="#cc0000"><b>%s</b></font>' % esc(m.status))
    if m.my_num is not None:
        me = m.nodes.get(m.my_num, {})
        bits = [esc(m.node_name(m.my_num))]
        if me.get('bat') is not None:
            if me['bat'] > 100:
                bits.append("plugged in")
            else:
                bits.append("bat %d%%" % me['bat'])
        if me.get('chutil') is not None:
            bits.append("ch %.0f%%" % me['chutil'])
        if me.get('airtx') is not None:
            bits.append("tx %.1f%%" % me['airtx'])
        if me.get('uptime') is not None:
            bits.append("up %s" % uptime(me['uptime']))
        if GPS_ENABLED:
            bits.append(gps_summary())
        parts.append(grey(" | ".join(bits)))
    return " ".join(parts) + "<br>"

def my_position():
    """Our position: the GPS if it has a fix, else the Heltec's own."""
    if gps.has_fix():
        return gps.lat, gps.lon
    me = mesh.nodes.get(mesh.my_num, {})
    if me.get('lat') is not None:
        return me['lat'], me['lon']
    return None

def distance_and_direction(lat1, lon1, lat2, lon2):
    r = 6371000.0                                  # earth radius, meters
    p1 = math.radians(lat1)
    p2 = math.radians(lat2)
    dp = p2 - p1
    dl = math.radians(lon2 - lon1)
    a = (math.sin(dp / 2) ** 2 +
         math.cos(p1) * math.cos(p2) * math.sin(dl / 2) ** 2)
    meters = 2 * r * math.atan2(math.sqrt(a), math.sqrt(1 - a))
    y = math.sin(dl) * math.cos(p2)
    x = (math.cos(p1) * math.sin(p2) -
         math.sin(p1) * math.cos(p2) * math.cos(dl))
    bearing = (math.degrees(math.atan2(y, x)) + 360) % 360
    names = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
    return meters, names[int((bearing + 22.5) // 45) % 8]

def fmt_distance(meters):
    if UNITS == "km":
        if meters < 1000:
            return "%d m" % meters
        return "%.1f km" % (meters / 1000.0)
    feet = meters * 3.28084
    if feet < 1000:
        return "%d ft" % feet
    return "%.1f mi" % (meters / 1609.344)

def gps_summary():
    if not GPS_ENABLED:
        return ""
    if not gps.is_on():
        return "GPS " + gps.status
    if gps.has_fix():
        return "GPS %d sats" % gps.sats
    if gps.status == "searching":
        return "GPS searching (%d sats)" % gps.sats
    return "GPS " + gps.status

def gps_button():
    if gps.is_on():
        return '<a href="/gps?on=0">Turn GPS off</a>'
    return '<a href="/gps?on=1">Turn GPS on</a>'

def uptime(s):
    if s < 3600:
        return "%dm" % (s // 60)
    if s < 86400:
        return "%dh" % (s // 3600)
    return "%dd" % (s // 86400)

def tabs(view):
    total = 0
    for k in mesh.unread:
        total += mesh.unread[k]
    chat = "Chat"
    if total:
        chat = "Chat (%d)" % total
    items = [('chat', chat),
             ('nodes', 'Nodes (%d)' % max(0, len(mesh.nodes) - 1)),
             ('chans', 'Chans'), ('quick', 'Quick'), ('info', 'Info')]
    out_parts = []
    for v, label in items:
        if v == view:
            out_parts.append('<b>[%s]</b>' % label)
        else:
            out_parts.append('<a href="/top?v=%s">%s</a>' % (v, label))
    return " ".join(out_parts) + "<hr size=1>"

def conv_link(key, label):
    n = mesh.unread.get(key, 0)
    if n:
        label = "%s (%d)" % (label, n)
    link = '<a href="/?c=%s" target="_top">%s</a>' % (key, esc(label))
    if key == mesh.current:
        return '<b>&#9656;%s</b>' % link    # the selected conversation
    return link

def view_chat():
    m = mesh
    key = m.current
    m.unread[key] = 0
    # conversation picker: channels, then DMs that exist
    links = []
    idxs = list(m.channels.keys())
    idxs.sort()
    if not idxs:
        idxs = [0]
    for i in idxs:
        links.append(conv_link("c%d" % i, "#" + m.channel_name(i)))
    dms = [k for k in m.messages if k[0] == 'd']
    dms.sort()
    for k in dms:
        links.append(conv_link(k, m.conv_title(k)))
    parts = [" ".join(links), "<br>"]
    parts.append('<b>%s</b> <a href="/top?v=find">Find</a><br>'
                 % esc(m.conv_title(key)))
    lst = list(m.messages.get(key, []))[-SHOW_MESSAGES:]
    lst.reverse()
    if not lst:
        parts.append('<i>No messages yet.</i>')
    now = time.time()
    for msg in lst:
        if msg['mine']:
            who = 'me'
        else:
            who = m.node_name(msg['frm'])
        parts.append('<b>%s %s</b>: %s<br>' % (when(msg['t']), esc(who),
                                               esc(msg['text'])))
        meta = msg['sig']
        if msg['mine'] and msg['status']:
            st = msg['status']
            if st == 'sending' and now - msg['t'] > ACK_WAIT_SECS:
                st = 'sent (no confirmation)'
            if st.startswith('failed'):
                meta = '<font color="#cc0000" size="1">%s</font>' % esc(st)
            else:
                meta = esc(st)
            if not st.startswith('failed'):
                meta = grey(meta)
            parts.append(meta + '<br>')
        elif meta:
            parts.append(grey(esc(meta)) + '<br>')
    return "".join(parts)

def view_nodes():
    m = mesh
    favs = favorites()
    nums = [n for n in m.nodes if n != m.my_num]
    # favorites first, then most recently heard
    nums.sort(key=lambda n: (n not in favs, -(m.nodes[n].get('heard') or 0)))
    parts = []
    here = my_position()
    if GPS_ENABLED or here:
        if gps.has_fix():
            mine = "You: %.5f, %.5f (GPS, %d sats)" % (gps.lat, gps.lon,
                                                      gps.sats)
            if SHARE_POSITION and gps.last_shared:
                mine += ", shared " + ago(gps.last_shared)
            elif not SHARE_POSITION:
                mine += ", not shared"
        elif here:
            mine = "You: %.5f, %.5f (Heltec's position)" % here
        else:
            mine = "You: no position yet (%s)" % gps_summary()
        line = grey(esc(mine))
        if GPS_ENABLED:
            line += " " + gps_button()
        parts.append(line + "<br>")
    if not nums:
        parts.append('<i>No other nodes heard yet.</i>')
    for num in nums:
        n = m.nodes[num]
        name = n.get('short') or ("!%08x" % num)
        star = ''
        if num in favs:
            star = '<font color="#cc8800">*</font>'
        line = '%s<b>%s</b>' % (star, esc(name))
        if n.get('long'):
            line += ' %s' % esc(n['long'])
        if num in favs:
            fav = '-Fav'
        else:
            fav = '+Fav'
        line += (' <a href="/?c=d%d" target="_top">DM</a>'
                 ' <a href="/trace?n=%d">Trace</a>'
                 ' <a href="/fav?n=%d">%s</a><br>' % (num, num, num, fav))
        bits = ["heard " + ago(n.get('heard'))]
        if here and n.get('lat') is not None:
            meters, direction = distance_and_direction(here[0], here[1],
                                                       n['lat'], n['lon'])
            bits.insert(0, "<b>%s %s</b>" % (fmt_distance(meters), direction))
        if n.get('snr') is not None:
            bits.append("SNR %.1f" % n['snr'])
        if n.get('hops') is not None:
            if n['hops'] == 0:
                bits.append("direct")
            elif n['hops'] == 1:
                bits.append("1 hop")
            else:
                bits.append("%d hops" % n['hops'])
        if n.get('bat') is not None:
            if n['bat'] > 100:
                bits.append("plugged in")
            else:
                bits.append("bat %d%%" % n['bat'])
        parts.append(line + grey(" | ".join(bits)) + "<br>")
    return "".join(parts)

def view_chans():
    m = mesh
    parts = []
    if m.preset:
        parts.append(grey("Radio preset: %s" % esc(m.preset)) + "<br>")
    idxs = list(m.channels.keys())
    idxs.sort()
    if not idxs:
        parts.append('<i>No channel info yet.</i>')
    for i in idxs:
        ch = m.channels[i]
        role = "primary"
        if ch['role'] == 2:
            role = "secondary"
        key = "c%d" % i
        line = '%d: %s' % (i, conv_link(key, "#" + m.channel_name(i)))
        parts.append(line + ' ' + grey(role) + '<br>')
    parts.append('<br>' + grey("Change channels with the Meshtastic app "
                               "or web client.") )
    return "".join(parts)

def view_quick():
    parts = ['<b>Quick reply to %s</b><br>' % esc(mesh.conv_title(mesh.current)),
             grey("Tap one to send it.") + "<br>"]
    for i in range(len(QUICK_REPLIES)):
        parts.append('&#8226; <a href="/quick?i=%d">%s</a><br>'
                     % (i, esc(QUICK_REPLIES[i])))
    parts.append("<br>" + grey("Edit the list near the top of "
                               "meshchat_web.py (QUICK_REPLIES)."))
    return "".join(parts)

def snr_text(values, i):
    if i < len(values) and values[i] != -128:
        return "%.1f dB" % (values[i] / 4.0)
    return "? dB"

def route_line(start, hops, end, snrs):
    """Me > A (5.0 dB) > B (3.2 dB) > Target (4.0 dB)"""
    parts = [esc(start)]
    names = [mesh.node_name(h) for h in hops] + [end]
    for i in range(len(names)):
        label = esc(names[i])
        if snrs:
            label += ' ' + grey(snr_text(snrs, i))
        parts.append(label)
    return " &gt; ".join(parts)

def view_trace(note=''):
    m = mesh
    parts = ['<b>Traceroutes</b> ' + grey("newest first") + '<br>']
    if note:
        parts.append('<font color="#cc0000">%s</font><br>' % esc(note))
    if not m.traces:
        parts.append('<i>None yet. Tap "Trace" next to a node in the '
                     'Nodes tab.</i>')
    now = time.time()
    for tr in m.traces:
        target = m.node_name(tr['num'])
        st = tr['status']
        if st == 'waiting' and now - tr['t'] > TRACE_TIMEOUT_SECS:
            st = tr['status'] = 'no response'
        parts.append('<b>%s</b> %s<br>' % (esc(target),
                                           grey(when(tr['t']))))
        if st == 'waiting':
            parts.append(grey("waiting for answer... (%d s)"
                              % int(now - tr['t'])) + "<br>")
        elif st == 'done':
            me = m.node_name(m.my_num or 0)
            if not tr['route']:
                parts.append(grey("direct, no relays") + "<br>")
            parts.append("Out: " + route_line(me, tr['route'], target,
                                              tr['snr_to']) + "<br>")
            if tr['back'] or tr['snr_back']:
                parts.append("Back: " + route_line(target, tr['back'], me,
                                                   tr['snr_back']) + "<br>")
            hops = len(tr['route'])
            parts.append(grey("%d relay%s, answered in %d s"
                              % (hops, hops != 1 and "s" or "",
                                 int(tr.get('done_t', now) - tr['t'])))
                         + "<br>")
        else:
            parts.append('<font color="#cc0000" size="1">%s</font><br>'
                         % esc(st))
    return "".join(parts)

def view_info():
    m = mesh
    s = m.stats
    now = time.time()
    rows = []
    def row(label, value):
        rows.append('%s: <b>%s</b><br>' % (label, esc(str(value))))
    rows.append('<b>MeshChat %s</b><br>' % VERSION)
    row("Running for", ago_plain(now - s['started']))
    rows.append('<br><b>Heltec link</b><br>')
    row("Address", "%s:%d" % (HOST, PORT))
    row("State", m.connected and "connected" or m.status)
    if m.connected and s['connected_since']:
        row("Connected for", ago_plain(now - s['connected_since']))
    row("Successful connects", s['connects'])
    row("Drops / failed tries", s['drops'])
    if s['last_error']:
        row("Last problem", "%s (%s)" % (s['last_error'],
                                         ago(s['last_error_t'])))
    if s['last_rx']:
        row("Last data from Heltec", ago(s['last_rx']))
    row("Packets in / out", "%d / %d" % (s['frames_in'], s['frames_out']))
    row("Messages in / out", "%d / %d" % (s['msgs_in'], s['msgs_out']))
    rows.append('<br><b>Radio</b><br>')
    if m.my_num is not None:
        row("This node", "%s (!%08x)" % (m.node_name(m.my_num), m.my_num))
    row("Preset", m.preset or "?")
    row("Nodes known", max(0, len(m.nodes) - 1))
    row("Channels", len(m.channels))
    rows.append('<br><b>GPS</b><br>')
    if not GPS_ENABLED:
        row("GPS", "turned off in settings")
    else:
        rows.append(gps_button() + "<br>")
        row("Port", GPS_PORT)
        row("State", gps.status)
        row("Satellites", gps.sats)
        if gps.last_fix:
            row("Last fix", ago(gps.last_fix))
        if SHARE_POSITION:
            row("Last shared", gps.last_shared and ago(gps.last_shared)
                or "not yet")
        else:
            row("Sharing", "off")
    rows.append('<br><b>Options</b><br>')
    sizes = []
    for k in ('small', 'normal', 'large'):
        if prefs['text'] == k:
            sizes.append('<b>[%s]</b>' % k)
        else:
            sizes.append('<a href="/set?text=%s">%s</a>' % (k, k))
    rows.append('Text size: ' + ' '.join(sizes) + '<br>')
    if prefs['sound'] == 'on':
        rows.append('Sound: <b>on</b> <a href="/set?sound=off">turn off</a>'
                    ' <a href="/set?test=1">test</a><br>')
    else:
        rows.append('Sound: <b>off</b> <a href="/set?sound=on">turn on</a>'
                    '<br>')
    rows.append('<br><b>More</b><br>'
                '<a href="/top?v=trace">Traceroutes</a> | '
                '<a href="/top?v=stats">Mesh stats</a> | '
                '<a href="/top?v=range">Range log</a> | '
                '<a href="/top?v=find">Find messages</a>')
    return "".join(rows)

def view_find(q=''):
    parts = ['<b>Find messages</b><br>'
             '<form action="/top" method="get" accept-charset="utf-8" '
             'style="margin:0"><input type="hidden" name="v" value="find">'
             '<input type="text" name="q" size="16" value="%s">'
             '<input type="submit" value="Find"></form>' % esc(q)]
    q = q.strip()
    if not q:
        parts.append(grey("Searches everything in the saved history."))
        return "".join(parts)
    try:
        f = open(HISTORY_FILE, 'rb')
        try:
            raw = f.read()
        finally:
            f.close()
    except Exception:
        parts.append('<i>No history yet.</i>')
        return "".join(parts)
    if PY3:
        raw = raw.decode('utf-8', 'replace')
    needle = q.lower()
    hits = []
    for line in raw.split('\n'):
        p = line.split('\t', 4)
        if len(p) == 5 and needle in p[4].lower():
            hits.append(p)
    hits.reverse()                       # newest first
    parts.append(grey("%d found%s" % (len(hits),
                                      len(hits) > 40 and ", showing 40" or ""))
                 + "<br>")
    for p in hits[:40]:
        try:
            t = int(p[0])
            frm = int(p[3])
        except ValueError:
            continue
        if p[2] == '1':
            who = 'me'
        else:
            who = mesh.node_name(frm)
        parts.append('%s <a href="/?c=%s" target="_top">%s</a><br>'
                     '<b>%s</b>: %s<br>'
                     % (grey(when(t)), p[1], esc(mesh.conv_title(p[1])),
                        esc(who), esc(p[4])))
    return "".join(parts)

def view_stats():
    st = meshstats
    parts = ['<b>Mesh stats</b> ' +
             grey("since MeshChat started, last 24 h") + '<br>']
    hours = list(st.hours.keys())
    hours.sort()
    hours.reverse()
    if not hours:
        parts.append('<i>Nothing heard yet.</i>')
        return "".join(parts)
    parts.append('<table border="0" cellspacing="0" cellpadding="1">'
                 '<tr><td><b>Hour</b></td><td><b>Pkts</b></td>'
                 '<td><b>Msgs</b></td><td><b>Nodes</b></td>'
                 '<td><b>Ch%</b></td></tr>')
    tp = tm = 0
    for h in hours:
        rec = st.hours[h]
        tp += rec['pkts']
        tm += rec['msgs']
        if rec['ch']:
            ch = "%.0f" % (sum(rec['ch']) / len(rec['ch']))
        else:
            ch = "-"
        label = time.strftime("%H:00", time.localtime(h * 3600))
        parts.append('<tr><td>%s</td><td>%d</td><td>%d</td><td>%d</td>'
                     '<td>%s</td></tr>' % (label, rec['pkts'], rec['msgs'],
                                           len(rec['nodes']), ch))
    parts.append('</table>')
    parts.append(grey("Total: %d packets, %d messages" % (tp, tm)) + "<br>")
    top = list(st.by_node.items())
    top.sort(key=lambda x: -x[1])
    if top:
        parts.append('<br><b>Most active nodes</b><br>')
        for num, count in top[:5]:
            parts.append('%s: %d packet%s<br>' % (esc(mesh.node_name(num)),
                                                 count, count != 1 and "s" or ""))
    return "".join(parts)

def view_range(note=''):
    r = rangelog
    parts = ['<b>Range log</b><br>']
    if note:
        parts.append('<font color="#cc0000">%s</font><br>' % esc(note))
    parts.append(grey("Records every node the Heltec hears, with your GPS "
                      "position and the signal, so you can map coverage "
                      "as you move.") + "<br>")
    if r.active:
        parts.append('<b>Logging</b> for %s, %d lines '
                     '<a href="/range?on=0">Stop</a><br>'
                     % (ago_plain(time.time() - r.started), r.lines))
    else:
        parts.append('Stopped. <a href="/range?on=1">Start logging</a><br>')
    if not gps.is_on():
        parts.append(grey("GPS is off - lines will have no position. ") +
                     gps_button() + "<br>")
    parts.append(grey("File: %s" % esc(RANGE_FILE)) + "<br>")
    parts.append(grey("Open it in Excel or Google Sheets, or load it into "
                      "a map site that reads CSV (lat/lon columns).") +
                 "<br>")
    return "".join(parts)

def ago_plain(secs):
    secs = int(secs)
    if secs < 60:
        return "%d s" % secs
    if secs < 3600:
        return "%d min" % (secs // 60)
    if secs < 86400:
        return "%dh %02dm" % (secs // 3600, (secs % 3600) // 60)
    return "%dd %dh" % (secs // 86400, (secs % 86400) // 3600)

def page_top(view, seen=None, note='', q=''):
    if view not in ('chat', 'nodes', 'chans', 'quick', 'info', 'trace',
                    'find', 'stats', 'range'):
        view = 'chat'
    if view == 'chat' and LIVE_UPDATES and seen is not None:
        wait_for_change(seen, WAIT_SECS)     # hold until something changes
    now_count = change_count[0]
    if view in ('quick', 'find'):
        refresh = ''
    elif view == 'trace':
        refresh = str(LIST_REFRESH_SECS)
        for tr in mesh.traces:
            if tr['status'] == 'waiting':
                refresh = "4"                 # update often while waiting
    elif view != 'chat':
        refresh = str(LIST_REFRESH_SECS)
    elif LIVE_UPDATES:
        # come straight back and wait for the next change
        refresh = "0;url=/top?v=chat&amp;w=%d" % now_count
    else:
        refresh = str(REFRESH_SECS)
    mesh.lock.acquire()
    try:
        body = header() + tabs(view)
        if view == 'chat':
            body += view_chat()
        elif view == 'nodes':
            body += view_nodes()
        elif view == 'quick':
            body += view_quick()
        elif view == 'info':
            body += view_info()
        elif view == 'trace':
            body += view_trace(note)
        elif view == 'find':
            body += view_find(q)
        elif view == 'stats':
            body += view_stats()
        elif view == 'range':
            body += view_range(note)
        else:
            body += view_chans()
    finally:
        mesh.lock.release()
    return page_head(refresh) + body + '</body></html>'

def page_frames():
    return ('<html><head><title>MeshChat</title></head>'
            '<frameset rows="*,64" border="1">'
            '<frame name="top" src="/top?v=chat">'
            '<frame name="form" src="/form" scrolling="no">'
            '</frameset></html>')

def page_form(note=''):
    mesh.lock.acquire()
    try:
        title = mesh.conv_title(mesh.current)
    finally:
        mesh.lock.release()
    return (page_head() +
            '<form action="/send" method="get" accept-charset="utf-8" '
            'style="margin:0">' +
            grey("To: %s" % esc(title)) + ' ' + grey(esc(note)) + '<br>'
            '<input type="text" name="m" size="22" maxlength="200">'
            '<input type="submit" value="Send"></form></body></html>')

def do_send(text):
    text = clean(text).strip()
    if not text:
        return ''
    if len(to_bytes(text)) > 200:
        return 'Too long (200 max)'
    key = mesh.current
    try:
        mesh.send_text(key, text)
        return 'Sent.'
    except Exception:
        return 'Not sent: %s' % sys.exc_info()[1]

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        url = urlparse(self.path)
        path = url[2]
        query = parse_qs(url[4])
        if path == '/':
            c = query.get('c', [''])[0]
            if c and c[0] in 'cd' and c[1:].isdigit():
                mesh.lock.acquire()
                try:
                    mesh.current = c
                    mesh.unread[c] = 0
                finally:
                    mesh.lock.release()
                bump()
            body = page_frames()
        elif path == '/top':
            seen = query.get('w', [''])[0]
            if seen.isdigit():
                seen = int(seen)
            else:
                seen = None
            q = query.get('q', [''])[0]
            body = page_top(query.get('v', ['chat'])[0], seen, '', q)
        elif path == '/form':
            body = page_form()
        elif path == '/send':
            body = page_form(do_send(query.get('m', [''])[0]))
        elif path == '/fav':
            n = query.get('n', [''])[0]
            if n.isdigit():
                toggle_favorite(int(n))
            self.redirect('/top?v=nodes')
            return
        elif path == '/set':
            t = query.get('text', [''])[0]
            if t in TEXT_SIZES:
                prefs['text'] = t
            s = query.get('sound', [''])[0]
            if s in ('on', 'off'):
                prefs['sound'] = s
            save_prefs()
            if query.get('test'):
                notify_sound()
            self.redirect('/top?v=info')
            return
        elif path == '/range':
            note = ''
            if query.get('on', [''])[0] == '1':
                note = rangelog.start()
            else:
                rangelog.stop()
            if note:
                body = page_top('range', None, note)
            else:
                self.redirect('/top?v=range')
                return
        elif path == '/gps':
            if GPS_ENABLED:
                if query.get('on', [''])[0] == '1':
                    gps.turn_on()
                else:
                    gps.turn_off()
            back = self.headers.get('Referer') or ''
            if 'v=nodes' in back:
                self.redirect('/top?v=nodes')
            else:
                self.redirect('/top?v=info')
            return
        elif path == '/quick':
            i = query.get('i', [''])[0]
            if i.isdigit() and int(i) < len(QUICK_REPLIES):
                do_send(QUICK_REPLIES[int(i)])
            self.redirect('/top?v=chat')
            return
        elif path == '/trace':
            n = query.get('n', [''])[0]
            note = ''
            if n.isdigit():
                if mesh.sock is None or not mesh.connected:
                    note = "Not connected to the Heltec."
                else:
                    note = mesh.send_trace(int(n))
            if note:
                body = page_top('trace', None, note)
            else:
                self.redirect('/top?v=trace')
                return
        else:
            self.send_response(404)
            self.end_headers()
            return
        if PY3:
            body = body.encode('utf-8')
        self.send_response(200)
        self.send_header('Content-Type', 'text/html; charset=utf-8')
        self.send_header('Cache-Control', 'no-cache')
        self.send_header('Pragma', 'no-cache')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def redirect(self, where):
        self.send_response(302)
        self.send_header('Location', where)
        self.send_header('Content-Length', '0')
        self.end_headers()

    def log_message(self, *args):
        pass

class Server(ThreadingMixIn, HTTPServer):
    # several page requests at once (one can be waiting for changes)
    daemon_threads = True

def main():
    out("MeshChat %s - open Internet Explorer at http://localhost:%d"
        % (VERSION, WEB_PORT))
    # Claim the web page port FIRST. If another copy of MeshChat is
    # already running, this fails and we stop before touching the Heltec.
    try:
        server = Server(('127.0.0.1', WEB_PORT), Handler)
    except Exception:
        out("")
        out("MeshChat is ALREADY RUNNING.")
        out("Just open Internet Explorer at http://localhost:%d" % WEB_PORT)
        out("(To restart it: Settings > System > Memory >")
        out(" Running Programs, stop Python, then start again.)")
        time.sleep(15)
        return
    load_prefs()
    load_history()
    if GPS_ENABLED and GPS_START_ON:
        gps.turn_on()
    t = threading.Thread(target=mesh.reader)
    if PY3:
        t.daemon = True
    else:
        t.setDaemon(True)
    t.start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    mesh.running = False
    mesh.close()

if __name__ == "__main__":
    main()
