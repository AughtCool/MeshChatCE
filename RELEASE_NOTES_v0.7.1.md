## MeshChatCE v0.7.1 — first public release

Off-grid text messaging on a Windows Mobile 6 phone. MeshChatCE talks to a Meshtastic LoRa node over Wi-Fi, so you can chat on the mesh, see nodes and their signal, and share your GPS position. No cell service or internet needed.

Tested on the **AT&T 8525 (HTC Hermes)** with a **Heltec V3**. Any Windows Mobile 5/6 Pocket PC with Wi-Fi and .NET CF 3.5 should work.

### Download
- **MeshChatCE.exe** — copy it to the phone and tap it. You need .NET Compact Framework 3.5 installed to main memory first (see [SETUP](docs/SETUP.md)).
- **MeshHotspot.ino** — optional ESP8266 hotspot sketch, in the source.

### What's in it
- Channels and direct messages, with delivery status and plain-English errors
- Texting-style chat bubbles, or a classic list
- Nodes list (signal, hops, battery, distance) you can sort by tapping a column; favorites; traceroute
- Radar-style map with no internet needed
- Bluetooth GPS position sharing, range log (CSV), mesh stats
- Rename your node and swap info/keys from the phone
- Built-in screenshots (Tools → Screenshot)
- Background pop-ups, sound, vibration, notification light; Light, Dark and Night themes

### New in 0.7.1
- Tools → Screenshot (in 5 seconds) saves the screen as a PNG

### New in 0.7
- Rename your node (long and short name) from the phone
- **Send my info** and **Swap info** fix "missing key" DM errors
- Plain-English delivery errors
- Tap a column heading to sort the nodes list

Full history: [CHANGELOG.md](CHANGELOG.md)

*Not affiliated with or endorsed by Meshtastic LLC.*
