# gps_test.py - check that PythonCE can read the GlobalSat BT-359 GPS
#
# 1. Make sure the GPS is on and paired, with an outgoing COM port set up
#    (Settings > Connections > Bluetooth > COM Ports).
# 2. Put that port number in GPS_PORT below.
# 3. Tap this file. It shows raw GPS lines first, then your position.
#    It stops by itself after RUN_MINUTES.
#
# Also runs on a computer for testing:  python gps_test.py nmea_file.txt

import sys, time

# ---------------- SETTINGS ----------------
GPS_PORT = "COM5:"     # Windows Mobile port name - keep the colon!
                       # If you set up "External GPS", try its program
                       # port instead (often "COM4:")
RUN_MINUTES = 10
RAW_LINES = 8          # how many raw lines to show first
# ------------------------------------------

if len(sys.argv) > 1:
    GPS_PORT = sys.argv[1]

def out(s):
    try:
        sys.stdout.write(s + "\n")
        sys.stdout.flush()
    except Exception:
        pass

def checksum_ok(line):
    """NMEA lines end with *XX, a check that the line arrived intact."""
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

def to_degrees(value, hemi):
    """NMEA writes 3012.3456 for 30 degrees 12.3456 minutes."""
    if not value:
        return None
    dot = value.find('.')
    if dot < 0:
        dot = len(value)
    deg = float(value[:dot - 2])
    minutes = float(value[dot - 2:])
    result = deg + minutes / 60.0
    if hemi in ('S', 'W'):
        result = -result
    return result

state = {'fix': False, 'sats': 0, 'lat': None, 'lon': None,
         'alt': None, 'speed': None, 'time': ''}

def handle(line):
    parts = line.split('*')[0].split(',')
    kind = parts[0][3:]            # GGA, RMC, ... (after $GP)
    try:
        if kind == 'GGA' and len(parts) >= 10:
            state['time'] = parts[1][:6]
            state['fix'] = parts[6] not in ('', '0')
            state['sats'] = int(parts[7] or 0)
            if state['fix']:
                state['lat'] = to_degrees(parts[2], parts[3])
                state['lon'] = to_degrees(parts[4], parts[5])
                if parts[9]:
                    state['alt'] = float(parts[9])
        elif kind == 'RMC' and len(parts) >= 8:
            if parts[2] == 'A' and parts[7]:
                state['speed'] = float(parts[7]) * 1.15078   # knots -> mph
    except ValueError:
        pass

def report():
    t = state['time']
    if len(t) == 6:
        t = "%s:%s:%s UTC" % (t[0:2], t[2:4], t[4:6])
    if not state['fix']:
        out("No fix yet - %d satellites  %s  (go outside or near a window)"
            % (state['sats'], t))
        return
    line = "FIX  %.5f, %.5f  sats %d" % (state['lat'], state['lon'],
                                         state['sats'])
    if state['alt'] is not None:
        line += "  alt %.0f m" % state['alt']
    if state['speed'] is not None:
        line += "  %.1f mph" % state['speed']
    out(line)

def main():
    out("GPS test - opening %s ..." % GPS_PORT)
    try:
        port = open(GPS_PORT, 'rb')
    except Exception:
        out("Could not open %s: %s" % (GPS_PORT, sys.exc_info()[1]))
        out("Check the GPS is on, paired, and the COM port number is right.")
        time.sleep(30)
        return
    out("Port open - waiting for data (Bluetooth may take a few seconds)")
    raw_shown = 0
    good = 0
    bad = 0
    last_report = 0
    end = time.time() + RUN_MINUTES * 60
    while time.time() < end:
        raw = port.readline()
        if not raw:
            if GPS_PORT.endswith(':'):
                time.sleep(0.2)          # no data yet, try again
                continue
            break                        # end of test file
        if not isinstance(raw, str):     # Python 3
            raw = raw.decode('ascii', 'replace')
        line = raw.strip()
        if not line.startswith('$'):
            continue
        if raw_shown < RAW_LINES:
            out("raw: " + line[:60])
            raw_shown += 1
        if not checksum_ok(line):
            bad += 1
            continue
        good += 1
        handle(line)
        if time.time() - last_report >= 2:
            report()
            last_report = time.time()
    report()
    out("-- done. %d good lines, %d damaged lines" % (good, bad))
    port.close()
    time.sleep(30)                       # leave the window up to read

if __name__ == "__main__":
    main()
