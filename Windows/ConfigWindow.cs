using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace TargetZoom.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private Theme.Scope? theme;

    public ConfigWindow(Plugin plugin) : base("Target Zoom Settings##TargetZoomConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 0) };
        Theme.UseCustomCollapseButton(this);
    }

    public override void PreDraw() => theme = Theme.Push();

    public override void PostDraw()
    {
        theme?.Dispose();
        theme = null;
    }

    public override void Draw()
    {
        var c = plugin.Config;
        var s = Theme.Scale;
        var changed = false;

        // keep the window a steady width so sliders don't jump
        ImGui.Dummy(new Vector2(360 * s, 0));

        // ---- Zoom ----
        Theme.SectionHeader(FontAwesomeIcon.SearchPlus, "Zoom");

        var zoom = c.ZoomDistance;
        var zoomMin = c.AllowCloseUps ? c.CloseUpLimit : 1.5f;
        if (Theme.Slider("Zoom In distance", ref zoom, zoomMin, 20f, "%.1f", "How close the camera starts when you pick Zoom In. Lower is closer. You can still scroll in or out after."))
        {
            c.ZoomDistance = zoom;
            changed = true;
        }

        var openOnZoom = c.OpenWindowOnZoom;
        if (Theme.Toggle("Open the Target Zoom window when I zoom in", ref openOnZoom,
                "Opens the window with the Stop button and framing pad whenever you zoom in, including from the game's right-click menu or chat."))
        {
            c.OpenWindowOnZoom = openOnZoom;
            changed = true;
        }

        var front = c.StartInFront;
        if (Theme.Toggle("Start in front of them", ref front, "Swings the camera around to face them when you zoom in. Turn off to keep your current angle."))
        {
            c.StartInFront = front;
            changed = true;
        }

        var closeUps = c.AllowCloseUps;
        if (Theme.Toggle("Allow close-ups", ref closeUps,
                "While zoomed, scrolling in keeps going past the normal limit instead of flipping to first person, so you can get a close-up of their face. Back to normal when you stop zooming."))
        {
            c.AllowCloseUps = closeUps;
            changed = true;
        }

        using (ImRaii.Disabled(!c.AllowCloseUps))
        {
            var limit = c.CloseUpLimit;
            if (Theme.Slider("Closest the camera can get", ref limit, 0.1f, 1.5f, "%.2f",
                    "The game normally stops at 1.5. Very low values can put the camera inside their head."))
            {
                c.CloseUpLimit = limit;
                if (c.ZoomDistance < limit)
                    c.ZoomDistance = limit;
                changed = true;
            }
        }

        var hideUi = c.HideUiWhileZoomed;
        if (Theme.Toggle("Hide the game UI while zoomed", ref hideUi,
                "Hides the HUD, chat, nameplates and other plugin windows for a clean view, the same as Scroll Lock. " +
                "Target Zoom's own windows stay open so you can still stop. Everything comes back when you stop zooming."))
        {
            c.HideUiWhileZoomed = hideUi;
            changed = true;
        }

        var restore = c.RestoreZoomOnStop;
        if (Theme.Toggle("Restore my zoom when I stop", ref restore))
        {
            c.RestoreZoomOnStop = restore;
            changed = true;
        }

        ImGui.TextUnformatted("Camera angle when I stop");
        Theme.HelpMarker("Behind me: swings the camera back behind your character at the normal tilt.\nAs before: puts it back at the exact angle it had before you zoomed.\nLeave it: keeps whatever angle it ended on.");
        var mode = c.CameraOnStop switch
        {
            CameraOnStop.BehindMe => 0,
            CameraOnStop.RestorePrevious => 1,
            _ => 2,
        };
        if (Theme.Segmented("camstop", ref mode, "Behind me", "As before", "Leave it"))
        {
            c.CameraOnStop = mode switch
            {
                0 => CameraOnStop.BehindMe,
                1 => CameraOnStop.RestorePrevious,
                _ => CameraOnStop.LeaveIt,
            };
            changed = true;
        }

        // ---- Framing ----
        Theme.SectionHeader(FontAwesomeIcon.ArrowsAlt, "Framing");

        var pad = c.ShowPanPad;
        if (Theme.Toggle("Show the framing pad while zoomed", ref pad, "A pad in the nearby list you drag to slide the camera around them."))
        {
            c.ShowPanPad = pad;
            changed = true;
        }

        using (ImRaii.Disabled(!c.ShowPanPad))
        {
            var panRange = c.PanRange;
            if (Theme.Slider("How far it can slide", ref panRange, 0.5f, 10f, "%.1f yalms"))
            {
                c.PanRange = panRange;
                changed = true;
            }

            var sens = c.PanSensitivity;
            if (Theme.Slider("Drag sensitivity", ref sens, 0.25f, 3f, "%.2fx"))
            {
                c.PanSensitivity = sens;
                changed = true;
            }
        }

        // ---- Right-click menu ----
        Theme.SectionHeader(FontAwesomeIcon.MousePointer, "Right-click menu");

        var showMenu = c.ShowContextMenuEntry;
        if (Theme.Toggle("Add Zoom In to the game's right-click menu", ref showMenu))
        {
            c.ShowContextMenuEntry = showMenu;
            changed = true;
        }

        using (ImRaii.Disabled(!c.ShowContextMenuEntry))
        {
            var menuNpcs = c.ContextMenuIncludeNpcs;
            if (Theme.Toggle("Show it on NPCs too", ref menuNpcs))
            {
                c.ContextMenuIncludeNpcs = menuNpcs;
                changed = true;
            }
        }

        // ---- Auto-stop ----
        Theme.SectionHeader(FontAwesomeIcon.StopCircle, "Stop automatically");

        var combat = c.StopOnCombat;
        if (Theme.Toggle("When I enter combat", ref combat))
        {
            c.StopOnCombat = combat;
            changed = true;
        }

        var esc = c.StopOnEsc;
        if (Theme.Toggle("When I press Esc", ref esc, "Pressing Esc stops zooming (not while you're typing in chat)."))
        {
            c.StopOnEsc = esc;
            changed = true;
        }

        var cutscene = c.StopOnCutscene;
        if (Theme.Toggle("When a cutscene starts", ref cutscene))
        {
            c.StopOnCutscene = cutscene;
            changed = true;
        }

        var beyond = c.StopBeyondDistance;
        if (Theme.Slider("When they get this far away", ref beyond, 0f, 100f, beyond <= 0 ? "Never" : "%.0f yalms",
                "Drag to 0 to never stop for distance. Zoning or them leaving the area always stops it."))
        {
            c.StopBeyondDistance = beyond;
            changed = true;
        }

        // ---- Nearby list ----
        Theme.SectionHeader(FontAwesomeIcon.List, "Nearby list");

        var range = c.ListRange;
        if (Theme.Slider("Range", ref range, 5f, 150f, "%.0f yalms"))
        {
            c.ListRange = range;
            changed = true;
        }

        var listNpcs = c.ListIncludeNpcs;
        if (Theme.Toggle("Include NPCs", ref listNpcs))
        {
            c.ListIncludeNpcs = listNpcs;
            changed = true;
        }

        var byName = c.ListSortByName;
        if (Theme.Toggle("Sort by name instead of distance", ref byName))
        {
            c.ListSortByName = byName;
            changed = true;
        }

        var showDist = c.ListShowDistance;
        if (Theme.Toggle("Show distance", ref showDist))
        {
            c.ListShowDistance = showDist;
            changed = true;
        }

        var syncBadge = c.ListShowSyncBadge;
        if (Theme.Toggle("Show Player Sync / Lightless badges", ref syncBadge,
                "Marks people who are synced with you through Player Sync (PS) or Lightless (LL). The filter above the list only shows up when one of those plugins is running."))
        {
            c.ListShowSyncBadge = syncBadge;
            changed = true;
        }

        // ---- Server info bar ----
        Theme.SectionHeader(FontAwesomeIcon.WindowMaximize, "Top bar");

        var showDtr = c.ShowDtrEntry;
        if (Theme.Toggle("Show a Zoom button in the top bar", ref showDtr, "Click it in game to open the nearby list."))
        {
            c.ShowDtrEntry = showDtr;
            changed = true;
        }

        using (ImRaii.Disabled(!c.ShowDtrEntry))
        {
            var dtrName = c.DtrShowName;
            if (Theme.Toggle("Show who I'm zoomed on", ref dtrName))
            {
                c.DtrShowName = dtrName;
                changed = true;
            }
        }

        // ---- Footer ----
        ImGui.Dummy(new Vector2(0, 8 * s));
        if (Theme.AccentButton(FontAwesomeIcon.Users, "Open nearby list", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight() * 1.15f)))
            plugin.MainWindow.IsOpen = true;

        if (changed)
            c.Save();
    }
}
