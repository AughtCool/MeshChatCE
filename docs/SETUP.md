# Setting up MeshChatCE

Four parts: the **hotspot**, the **Meshtastic node**, the **phone**, and
(optionally) a **Bluetooth GPS**.

---

## 1. The hotspot (skip if you have a Wi-Fi router)

The phone and the node both need to join the same Wi-Fi network. Neither can
create one, so a cheap ESP8266 board (e.g. Wemos D1 Mini) does it.

1. Install the **ESP8266 board package** in the Arduino IDE
   (File → Preferences → Additional Boards Manager URLs:
   `http://arduino.esp8266.com/stable/package_esp8266com_index.json`,
   then Tools → Board → Boards Manager → install **esp8266**).
2. Open [`hotspot/MeshHotspot/MeshHotspot.ino`](../hotspot/MeshHotspot/MeshHotspot.ino).
3. **Change the password** (`PASSWORD`, 8+ characters). Optionally lower
   `TX_POWER_DBM` to 10 to save battery when everything is close together.
4. Choose your board (e.g. "LOLIN(WEMOS) D1 mini") and upload.
5. Serial Monitor at 115200 should say `Hotspot "MeshNet" is up`.

The hotspot is at `192.168.4.1` and accepts WPA and WPA2.

## 2. The Meshtastic node

Tested with a Heltec V3. Configure it over USB at
[client.meshtastic.org](https://client.meshtastic.org) (Chrome/Edge), or with
the phone app.

1. **Region**: set your LoRa region (e.g. US).
2. **Network → Wi-Fi**: enable, SSID `MeshNet` (or your router), password.
3. **Network → IPv4**: static is easiest:
   - Address `192.168.4.250`, gateway `192.168.4.1`, subnet `255.255.255.0`,
     DNS `192.168.4.1`
4. Save. The node joins the network (its screen shows the IP).

Note: on ESP32 boards, turning Wi-Fi on turns Bluetooth off. To change
settings later, use USB, or browse to `http://192.168.4.250` from a computer
on the same network (close MeshChatCE first — the node serves one app at a time).

## 3. The phone

1. **Wi-Fi**: Settings → Connections → Wi-Fi → join `MeshNet`
   (WPA2-PSK/AES; on older ROMs WPA-PSK/TKIP also works).
   - Set *Networks to access* to **Only access points**.
   - In HTC's *Wireless LAN* settings, set Power Mode to **Best Performance**.
2. **.NET Compact Framework 3.5**: copy `NETCFv35.wm.armv4i.cab` (from
   Microsoft's `NetCFSetupv35.msi`, installed on a PC) to the phone, tap it,
   and install to **Device** (main memory). Soft-reset afterwards.
   *Installing to the storage card, or running out of memory, causes
   "could not load System.Windows.Forms" errors.*
3. Copy **`MeshChatCE.exe`** to the phone and tap it. Accept the
   "unknown publisher" prompt.
4. If your node isn't at `192.168.4.250`: **Menu → Options → Settings**.

**Start automatically:** in File Explorer, tap-and-hold `MeshChatCE.exe` →
Copy, go to `\Windows\StartUp`, Menu → Edit → **Paste Shortcut**.

**Power-up order:** hotspot → node → phone.

## 4. Bluetooth GPS (optional)

Any Bluetooth NMEA GPS should work (tested: GlobalSat BT-359).

1. Pair it: Settings → Connections → Bluetooth → Add new device (PIN often `0000`).
2. **COM Ports** tab → New Outgoing Port → choose the GPS, e.g. **COM5**,
   *Secure Connection* unticked.
3. In MeshChatCE: Menu → Options → Settings → GPS port (e.g. `COM5`).
4. Turn it on with **Menu → GPS**. The status line shows the satellite count.

MeshChatCE gives your position to the node, which shares it on the mesh. For
accurate distances, set **Position precision** on your channel to precise
(remember everyone on that channel can then see where you are).

## Troubleshooting

| Problem | Try |
|---|---|
| "Waiting for Wi-Fi..." forever | Join the network in Settings → Wi-Fi; check the hotspot is on |
| "Keeps getting cut off" | Another app is connected to the node — close it; restart the node |
| "No route to host" (10065) | The phone isn't on the same network as the node |
| Could not load System.Windows.Forms 3.5 | Reinstall .NET CF 3.5 to **Device**, with enough free memory |
| DM "failed: missing their key" | Nodes → Actions → **Swap info**, wait a minute, retry |
| GPS "not found - off" | GPS on? Paired? Correct COM port in Settings? |
| No sound / wrong light blinks | Settings → vibrator / notification LED numbers |
