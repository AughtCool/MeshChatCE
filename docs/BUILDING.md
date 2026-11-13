# Building MeshChatCE

No Visual Studio needed. MeshChatCE is plain C# for the .NET Compact
Framework 3.5, compiled with the C# compiler that ships with Windows.

## You need (on a Windows PC — tested on Windows 10 and 11)

1. **.NET Framework 3.5** — Control Panel → Programs → *Turn Windows features
   on or off* → tick **.NET Framework 3.5**.
2. **.NET Compact Framework 3.5 Redistributable** — `NetCFSetupv35.msi`
   (Microsoft Download Center ID 65; also archived by Legacy Update).
   SHA-1 of the genuine file: `48EBA5C9AE1856CDA0C2B0833DFDC28C36DBBC04`.
   Installing it puts the reference assemblies in
   `C:\Program Files (x86)\Microsoft.NET\SDK\CompactFramework\v3.5\WindowsCE`
   and the device installers (`NETCFv35.wm.armv4i.cab` etc.) next to them.

## Build

1. Extract the repository (don't run it from inside the zip window).
2. Double-click **`src\build.bat`**.
3. On success you get **`src\MeshChatCE.exe`**. Messages are also saved in
   `src\build_log.txt`.

`build.bat` finds `csc.exe` (v3.5, or v4 as a fallback), compiles with
`/nostdlib` against the Compact Framework assemblies
(`mscorlib`, `System`, `System.Drawing`, `System.Windows.Forms`,
`Microsoft.WindowsCE.Forms`), embeds `MeshChatCE.ico` as the app icon and
as a resource for the pop-up notification.

## Source files

| File | What's in it |
|---|---|
| `Mesh.cs` | Connection to the node, Meshtastic framing and protobuf, messages, nodes, traceroute, renaming |
| `MainForm.cs` | Main screen, menus, settings loading, notifications, keep-awake |
| `ChatView.cs` | Hand-drawn chat bubbles with scrolling |
| `NodesForm.cs` | Nodes list, node details, traceroutes, shared settings file |
| `MapForm.cs` | Radar map |
| `Screens.cs` | Settings, search and rename screens |
| `Extras.cs` | GPS, distances, range log, stats, sound/vibration/LED, power, themes, fonts |

There are no external libraries. Meshtastic's protobuf messages are read and
written by a small hand-written encoder/decoder in `Mesh.cs`.
