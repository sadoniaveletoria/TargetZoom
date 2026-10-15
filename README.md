# Target Zoom

A Dalamud plugin for FFXIV. Right-click someone and pick **Zoom In** — the camera moves onto them, swings around to face them and zooms in close.

- Zoom In from the right-click menu in the world, the party list or chat
- Nearby list with job icons, distance and a name search (`/tzoom open`)
- Framing pad to slide the camera around them
- Mirror mode to look at yourself from the front (`/tzoom mirror`)
- Player Sync / Lightless filter and badges
- Top bar button, and the camera goes back behind you when you stop

## Install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste:
   ```
   https://raw.githubusercontent.com/sadoniaveletoria/TargetZoom/main/repo.json
   ```
   then click **+**, tick **Enabled**, and **Save and Close**.
3. Type `/xlplugins`, search for **Target Zoom** and install it.

## Commands

| Command | What it does |
| --- | --- |
| `/tzoom` | Stop zooming |
| `/tzoom open` | Open the nearby list |
| `/tzoom settings` | Open settings |
| `/tzoom mirror` | Look at yourself from the front |
| `/tzoom <distance>` | Set how close Zoom In gets (1.5–20) |
