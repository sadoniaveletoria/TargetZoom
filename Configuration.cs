using System;
using Dalamud.Configuration;

namespace TargetZoom;

public enum SyncSource
{
    None = 0,
    PlayerSync = 1,
    Lightless = 2,
    Other = 3, // a Mare-based sync we can't tell apart
}

public enum CameraOnStop
{
    LeaveIt = 0,
    BehindMe = 1,
    RestorePrevious = 2,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // ---- Zoom ----
    // How close the camera gets when you pick Zoom In (game range is about 1.5 to 20).
    public float ZoomDistance { get; set; } = 2.5f;
    // Put your old zoom distance back when you stop zooming.
    public bool RestoreZoomOnStop { get; set; } = true;
    // Swing the camera around to face them when you zoom in.
    public bool StartInFront { get; set; } = true;
    // Where the camera angle goes when you stop zooming.
    public CameraOnStop CameraOnStop { get; set; } = CameraOnStop.BehindMe;

    // ---- Pan (framing pad) ----
    public bool ShowPanPad { get; set; } = true;
    public float PanRange { get; set; } = 3f;        // max yalms the view can slide off them
    public float PanSensitivity { get; set; } = 1f;

    // ---- Right-click menu ----
    public bool ShowContextMenuEntry { get; set; } = true;
    public bool ContextMenuIncludeNpcs { get; set; } = true;

    // ---- Auto-stop ----
    public bool StopOnCombat { get; set; } = true;
    public bool StopOnCutscene { get; set; } = true;
    // Stop if they get farther than this from you (yalms). 0 = never.
    public float StopBeyondDistance { get; set; } = 0f;

    // ---- Nearby list window ----
    public float ListRange { get; set; } = 50f;
    public bool ListIncludeNpcs { get; set; } = false;
    public bool ListSortByName { get; set; } = false;
    public bool ListShowDistance { get; set; } = true;
    // 0 = everyone, 1 = any sync, 2 = Player Sync only, 3 = Lightless only
    public int ListSyncFilter { get; set; } = 0;
    public bool ListShowSyncBadge { get; set; } = true;

    // ---- Server info bar (top bar) ----
    public bool ShowDtrEntry { get; set; } = true;
    public bool DtrShowName { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
