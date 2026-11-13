# Legacy: MeshChat for PythonCE

The first version of the project: a Python script that runs on the phone with
**PythonCE 2.5** and shows the chat in **Pocket Internet Explorer**.
It's slower than MeshChatCE, but needs no .NET install.

- `meshchat_web.py` — the app (channels, DMs, nodes, GPS, traceroute, range log, stats …)
- `MeshChat_start.py` — tap this to start it (loads faster after the first run)
- `gps_test.py` — checks that PythonCE can read a Bluetooth GPS COM port

Use: install PythonCE (`PythonCE-25-20061219.PPC2003_ARM.CAB`), copy these
files to the same folder, tap `MeshChat_start.py`, then open
`http://localhost:8080` in Internet Explorer (use `localhost`, not `127.0.0.1`).
Edit `HOST` and `GPS_PORT` at the top of `meshchat_web.py`.

Not maintained — MeshChatCE replaces it.
