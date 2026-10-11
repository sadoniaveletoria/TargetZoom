using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using RaptureAtkModule = FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkModule;
using TargetZoom.Windows;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace TargetZoom;

public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;

    private IDtrBarEntry? dtrEntry;
    private string? lastDtrText;

    private const string Command = "/tzoom";

    // World camera layout (same values Cammy/Hypostasis use).
    private const int GetCameraTargetVfIndex = 18;
    private const int GetCameraPositionVfIndex = 16;
    private const int CanChangePerspectiveVfIndex = 23;
    private const int CurrentZoomOffset = 0x124;
    private const int MinZoomOffset = 0x128;
    private const int MaxZoomOffset = 0x12C;
    private const int HRotationOffset = 0x140;   // -pi..pi
    private const int VRotationOffset = 0x144;
    private const float DefaultVRotation = -0.349066f; // the game's default tilt (about 20° down)
    private const float FrontVRotation = -0.1f;        // nearly level, for a face-on view

    private delegate GameObjectStruct* GetCameraTargetDelegate(nint camera);
    private readonly Hook<GetCameraTargetDelegate> getCameraTargetHook;

    // Where the camera orbits around. We nudge it to pan.
    private delegate void GetCameraPositionDelegate(nint camera, nint target, Vector3* position, byte swapPerson);
    private readonly Hook<GetCameraPositionDelegate> getCameraPositionHook;

    // Pan offset while zoomed, in yalms. Right is relative to the camera, Up is world height.
    public float PanRight { get; set; }
    public float PanUp { get; set; }
    public void ResetPan() => PanRight = PanUp = 0f;

    private readonly Configuration config;
    public Configuration Config => config;

    private readonly WindowSystem windowSystem = new("TargetZoom");
    public MainWindow MainWindow { get; }
    public ConfigWindow ConfigWindow { get; }

    public ulong ZoomTargetId => zoomTargetId;
    private ulong zoomTargetId;      // 0 = not zoomed on anyone
    private nint zoomTargetAddress;  // refreshed every frame
    private float? savedZoom;        // your zoom before Zoom In, restored on stop
    private float savedHRotation;    // camera angle before Zoom In
    private float savedVRotation;
    private float? savedMinZoom;     // the game's closest zoom, lowered while zoomed for close-ups

    // Asked by the game before switching 1st <-> 3rd person (e.g. scrolling all the way in).
    // We say no while zoomed, so scrolling in stops at the closest zoom instead.
    private delegate byte CanChangePerspectiveDelegate(nint camera);
    private readonly Hook<CanChangePerspectiveDelegate> canChangePerspectiveHook;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var camera = WorldCamera;
        if (camera == 0)
            throw new InvalidOperationException("Target Zoom: world camera not found.");

        var vtbl = *(nint**)camera;
        getCameraTargetHook = GameInterop.HookFromAddress<GetCameraTargetDelegate>(vtbl[GetCameraTargetVfIndex], GetCameraTargetDetour);
        getCameraTargetHook.Enable();
        getCameraPositionHook = GameInterop.HookFromAddress<GetCameraPositionDelegate>(vtbl[GetCameraPositionVfIndex], GetCameraPositionDetour);
        getCameraPositionHook.Enable();
        canChangePerspectiveHook = GameInterop.HookFromAddress<CanChangePerspectiveDelegate>(vtbl[CanChangePerspectiveVfIndex], CanChangePerspectiveDetour);
        canChangePerspectiveHook.Enable();

        MainWindow = new MainWindow(this);
        ConfigWindow = new ConfigWindow(this);
        windowSystem.AddWindow(MainWindow);
        windowSystem.AddWindow(ConfigWindow);
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigWindow;

        dtrEntry = DtrBar.Get("Target Zoom");
        dtrEntry.Tooltip = "Target Zoom — click to open the nearby list";
        dtrEntry.OnClick = _ => MainWindow.Toggle();
        UpdateDtr();

        ContextMenu.OnMenuOpened += OnMenuOpened;
        Framework.Update += OnFrameworkUpdate;
        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Stop zooming. \"/tzoom open\" lists people nearby, \"/tzoom settings\" opens settings, \"/tzoom mirror\" looks at yourself from the front,\"/tzoom <distance>\" sets how close Zoom In gets (1.5–20).",
        });
    }

    private void ToggleMainWindow() => MainWindow.Toggle();
    private void ToggleConfigWindow() => ConfigWindow.Toggle();

    public void Dispose()
    {
        StopZoom();
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigWindow;
        windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(Command);
        Framework.Update -= OnFrameworkUpdate;
        ContextMenu.OnMenuOpened -= OnMenuOpened;
        dtrEntry?.Remove();
        getCameraTargetHook.Dispose();
        getCameraPositionHook.Dispose();
        canChangePerspectiveHook.Dispose();
    }

    private static nint WorldCamera
    {
        get
        {
            var mgr = CameraManager.Instance();
            return mgr == null ? 0 : (nint)mgr->Camera;
        }
    }

    private static float* CameraFloat(int offset) => (float*)(WorldCamera + offset);

    // ---- Right-click menu ----

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!config.ShowContextMenuEntry)
            return;

        if (args.MenuType != ContextMenuType.Default || args.Target is not MenuTargetDefault target)
            return;

        if (ResolveMenuTarget(target) is not { } character)
            return;

        if (!config.ContextMenuIncludeNpcs && character.ObjectKind != ObjectKind.Pc)
            return;

        if (character.Address == ObjectTable.LocalPlayer?.Address)
            return;

        var id = character.GameObjectId;
        var zoomingThis = zoomTargetId == id;

        args.AddMenuItem(new MenuItem
        {
            Name = zoomingThis ? "Stop Zooming" : "Zoom In",
            PrefixChar = 'Z',
            OnClicked = _ =>
            {
                if (zoomingThis)
                    StopZoom();
                else
                    StartZoom(id);
            },
        });
    }

    // Right-clicking someone in the world gives us the object directly. Right-clicking a
    // name in chat (or the friend list, etc.) only gives a name + home world, so we look
    // for a matching player nearby. If they aren't nearby, no Zoom In entry is added.
    private static ICharacter? ResolveMenuTarget(MenuTargetDefault target)
    {
        if (target.TargetObject is ICharacter direct)
            return direct;

        var name = target.TargetName;
        if (string.IsNullOrEmpty(name))
            return null;

        var worldId = target.TargetHomeWorld.RowId;
        foreach (var obj in ObjectTable)
        {
            if (obj is IPlayerCharacter pc
                && pc.Name.TextValue == name
                && (worldId == 0 || pc.HomeWorld.RowId == worldId))
                return pc;
        }

        return null;
    }

    // ---- Zoom control ----

    // Mirror: point the camera at yourself from the front, like looking in a mirror.
    public void StartMirror()
    {
        if (ObjectTable.LocalPlayer is { } me)
            StartZoom(me.GameObjectId, forceFront: true);
    }

    public bool IsMirroring => zoomTargetId != 0 && zoomTargetAddress == (ObjectTable.LocalPlayer?.Address ?? -1);

    public void StartZoom(ulong id, bool forceFront = false)
    {
        var obj = ObjectTable.SearchById(id);
        if (obj == null || WorldCamera == 0)
            return;

        zoomTargetId = id;
        ResetPan();
        zoomTargetAddress = obj.Address;

        if (savedZoom == null)
        {
            savedZoom = *CameraFloat(CurrentZoomOffset);
            savedHRotation = *CameraFloat(HRotationOffset);
            savedVRotation = *CameraFloat(VRotationOffset);
        }

        savedMinZoom ??= *CameraFloat(MinZoomOffset);
        ApplyCloseUpLimit();

        var min = *CameraFloat(MinZoomOffset);
        var max = *CameraFloat(MaxZoomOffset);
        *CameraFloat(CurrentZoomOffset) = Math.Clamp(config.ZoomDistance, min, max);

        if (config.HideUiWhileZoomed)
            HideGameUi();

        if (config.StartInFront || forceFront)
        {
            // Camera angle equal to their facing puts the camera in front of them
            // (behind would be facing + 180°). Nearly level so you see their face.
            *CameraFloat(HRotationOffset) = obj.Rotation;
            *CameraFloat(VRotationOffset) = FrontVRotation;
        }
    }

    // ---- Hide UI while zoomed ----
    // Uses the same switch as the game's Toggle UI key (Scroll Lock by default), so pressing
    // that key brings everything back, and Dalamud hides plugin windows along with it.

    private bool weHidUi;

    private static bool GameUiVisible
    {
        get
        {
            var atk = RaptureAtkModule.Instance();
            return atk == null || atk->IsUiVisible;
        }
        set
        {
            var atk = RaptureAtkModule.Instance();
            if (atk != null)
                atk->IsUiVisible = value;
        }
    }

    private void HideGameUi()
    {
        if (!GameUiVisible)
            return; // already hidden by the player; leave it to them
        GameUiVisible = false;
        weHidUi = true;
    }

    private void RestoreGameUi()
    {
        // Only undo what we did. If they already brought it back with Scroll Lock, nothing to do.
        if (weHidUi && !GameUiVisible)
            GameUiVisible = true;
        weHidUi = false;
    }

    public void StopZoom()
    {
        RestoreGameUi();
        var wasZooming = zoomTargetId != 0;
        zoomTargetId = 0;
        zoomTargetAddress = 0;

        if (wasZooming && WorldCamera != 0)
        {
            if (config.RestoreZoomOnStop && savedZoom is { } zoom)
                *CameraFloat(CurrentZoomOffset) = zoom;

            switch (config.CameraOnStop)
            {
                case CameraOnStop.RestorePrevious:
                    *CameraFloat(HRotationOffset) = savedHRotation;
                    *CameraFloat(VRotationOffset) = savedVRotation;
                    break;

                case CameraOnStop.BehindMe when ObjectTable.LocalPlayer is { } me:
                    // Camera angle is the character's facing flipped 180°.
                    var h = me.Rotation + MathF.PI;
                    if (h > MathF.PI) h -= MathF.Tau;
                    *CameraFloat(HRotationOffset) = h;
                    *CameraFloat(VRotationOffset) = DefaultVRotation;
                    break;
            }
        }

        // Put the game's closest zoom back before restoring your zoom distance.
        if (savedMinZoom is { } minZoom && WorldCamera != 0)
            *CameraFloat(MinZoomOffset) = minZoom;
        savedMinZoom = null;

        savedZoom = null;
        ResetPan();
    }

    // Server info bar entry: "Zoom" normally, "Zoom: Name" while zoomed.
    private void UpdateDtr()
    {
        if (dtrEntry == null)
            return;

        dtrEntry.Shown = config.ShowDtrEntry;
        if (!config.ShowDtrEntry)
            return;

        var text = "Zoom";
        if (zoomTargetId != 0 && config.DtrShowName && ObjectTable.SearchById(zoomTargetId) is { } obj)
            text = $"Zoom: {obj.Name.TextValue}";

        if (text == lastDtrText)
            return;

        dtrEntry.Text = text;
        lastDtrText = text;
    }

    // ---- Sync plugin detection (Player Sync / Lightless) ----
    // Two independent ways to spot synced people, merged together:
    //  1. Ask the sync plugins which characters they're handling (names confirmed from their source).
    //     Player Sync's answer can fail in crowded areas (it throws internally), so we also...
    //  2. Ask Penumbra which mod collection each nearby player has. Sync plugins give every synced
    //     person a temporary collection named "Mare_<id>" (Player Sync) or "Lightless_<id>".
    private static readonly (string Ipc, SyncSource Source)[] SyncIpcs =
    [
        ("PlayerSync.GetHandledAddresses", SyncSource.PlayerSync),
        ("LightlessSync.GetHandledAddresses", SyncSource.Lightless),
        ("Lightless.GetHandledAddresses", SyncSource.Lightless),
        ("MareSynchronos.GetHandledAddresses", SyncSource.Other),
    ];

    private readonly Dictionary<nint, SyncSource> synced = new();
    private DateTime nextSyncPoll = DateTime.MinValue;

    // Last good answer from each sync plugin, reused briefly if a call fails mid-update.
    private readonly Dictionary<string, (List<nint> List, DateTime At)> lastGoodIpc = new();
    private int penumbraMatches;
    private string penumbraStatus = "?";

    // Which sync plugin (if any) is handling this character.
    public SyncSource GetSync(nint address) => synced.TryGetValue(address, out var s) ? s : SyncSource.None;

    // True when a sync plugin is present, so the UI can hide the filter otherwise.
    public bool AnySyncPluginFound { get; private set; }

    private void PollSyncPlugins()
    {
        if (DateTime.UtcNow < nextSyncPoll)
            return;
        nextSyncPoll = DateTime.UtcNow.AddSeconds(1);

        synced.Clear();
        syncIpcStatus.Clear();
        var found = false;

        // 1. Sync plugins
        foreach (var (ipc, source) in SyncIpcs)
        {
            List<nint>? list = null;
            try
            {
                list = PluginInterface.GetIpcSubscriber<List<nint>>(ipc).InvokeFunc();
                lastGoodIpc[ipc] = (list, DateTime.UtcNow);
                syncIpcStatus[ipc] = $"answered, {list.Count} people";
                found = true;
            }
            catch (Exception ex)
            {
                // IpcNotReadyError = that plugin isn't installed. Anything else means it IS
                // installed but its answer failed this time.
                var installed = ex is not Dalamud.Plugin.Ipc.Exceptions.IpcNotReadyError;
                found |= installed;
                if (installed && lastGoodIpc.TryGetValue(ipc, out var last) && DateTime.UtcNow - last.At < TimeSpan.FromSeconds(10))
                {
                    list = last.List;
                    syncIpcStatus[ipc] = $"failed ({ex.GetType().Name}), using last answer: {list.Count} people";
                }
                else
                {
                    syncIpcStatus[ipc] = installed ? $"failed ({ex.GetType().Name})" : "not installed";
                }
            }

            if (list != null)
                foreach (var addr in list)
                    synced.TryAdd(addr, source); // first (most specific) name wins
        }

        // 2. Penumbra temporary collections
        penumbraMatches = 0;
        try
        {
            // Penumbra.Api's GetCollectionForObject, called directly so we don't need the package.
            var getCollection = PluginInterface.GetIpcSubscriber<int, (bool, bool, (Guid, string))>("Penumbra.GetCollectionForObject.V5");
            var me = ObjectTable.LocalPlayer?.Address ?? 0;
            foreach (var obj in ObjectTable)
            {
                if (obj.ObjectKind != ObjectKind.Pc || obj.Address == me)
                    continue;

                var (valid, individual, (_, name)) = getCollection.InvokeFunc(obj.ObjectIndex);
                if (!valid || !individual || string.IsNullOrEmpty(name))
                    continue;

                var source = name.StartsWith("Lightless_", StringComparison.Ordinal) ? SyncSource.Lightless
                    : name.StartsWith("Mare_", StringComparison.Ordinal) ? SyncSource.PlayerSync
                    : SyncSource.None;
                if (source == SyncSource.None)
                    continue;

                found = true;
                penumbraMatches++;
                synced.TryAdd(obj.Address, source);
            }
            penumbraStatus = $"answered, {penumbraMatches} synced collections";
        }
        catch (Exception ex)
        {
            penumbraStatus = ex is Dalamud.Plugin.Ipc.Exceptions.IpcNotReadyError ? "not installed" : $"failed ({ex.GetType().Name})";
        }

        AnySyncPluginFound = found;
    }

    private readonly Dictionary<string, string> syncIpcStatus = new();

    // /tzoom syncinfo — prints what the sync plugins report, to track down detection problems.
    private void PrintSyncInfo()
    {
        nextSyncPoll = DateTime.MinValue;
        PollSyncPlugins();

        Chat.Print("[Target Zoom] Sync plugin check:");
        foreach (var (ipc, _) in SyncIpcs)
            Chat.Print($"  {ipc}: {syncIpcStatus.GetValueOrDefault(ipc, "?")}");
        Chat.Print($"  Penumbra collections: {penumbraStatus}");

        var players = 0;
        var matched = new HashSet<nint>();
        foreach (var obj in ObjectTable)
        {
            if (obj.ObjectKind != ObjectKind.Pc || obj.Address == ObjectTable.LocalPlayer?.Address)
                continue;
            players++;
            if (synced.ContainsKey(obj.Address))
                matched.Add(obj.Address);
        }

        var unmatched = synced.Keys.Count(a => !matched.Contains(a));
        Chat.Print($"  Players loaded near you: {players}. Reported as synced: {synced.Count}. Matched to a player: {matched.Count}. Not matched: {unmatched}.");
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        UpdateDtr();
        PollSyncPlugins();

        if (zoomTargetId == 0)
            return;

        // Drop back to yourself if they leave, you zone, a cutscene starts, or combat starts.
        var obj = ObjectTable.SearchById(zoomTargetId);
        var local = ObjectTable.LocalPlayer;
        if (obj == null
            || !obj.IsValid()
            || local == null
            || Condition[ConditionFlag.BetweenAreas]
            || Condition[ConditionFlag.BetweenAreas51]
            || (config.StopOnCombat && Condition[ConditionFlag.InCombat])
            || (config.StopOnCutscene && (Condition[ConditionFlag.WatchingCutscene] || Condition[ConditionFlag.OccupiedInCutSceneEvent]))
            || (config.StopBeyondDistance > 0 && Vector3.Distance(local.Position, obj.Position) > config.StopBeyondDistance))
        {
            StopZoom();
            return;
        }

        zoomTargetAddress = obj.Address;
        ApplyCloseUpLimit(); // the game can reset it (e.g. on mount/dismount), so keep it applied
    }

    // While zoomed, let the camera get closer than the game normally allows.
    private void ApplyCloseUpLimit()
    {
        if (WorldCamera == 0 || savedMinZoom is not { } gameMin)
            return;
        var limit = config.AllowCloseUps ? MathF.Min(config.CloseUpLimit, gameMin) : gameMin;
        if (*CameraFloat(MinZoomOffset) != limit)
            *CameraFloat(MinZoomOffset) = limit;
    }

    private byte CanChangePerspectiveDetour(nint camera)
    {
        if (zoomTargetId != 0 && config.AllowCloseUps)
            return 0;
        return canChangePerspectiveHook.Original(camera);
    }

    private void GetCameraPositionDetour(nint camera, nint target, Vector3* position, byte swapPerson)
    {
        getCameraPositionHook.Original(camera, target, position, swapPerson);

        if (zoomTargetAddress == 0 || camera != WorldCamera || (PanRight == 0 && PanUp == 0))
            return;

        // Slide sideways relative to where the camera is facing (same math Cammy uses for its side offset).
        var a = *CameraFloat(HRotationOffset) - MathF.PI / 2f;
        position->X += -PanRight * MathF.Sin(a);
        position->Z += -PanRight * MathF.Cos(a);
        position->Y += PanUp;
    }

    private GameObjectStruct* GetCameraTargetDetour(nint camera)
    {
        if (zoomTargetAddress != 0 && camera == WorldCamera)
            return (GameObjectStruct*)zoomTargetAddress;

        return getCameraTargetHook.Original(camera);
    }

    // ---- /tzoom ----

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();

        switch (trimmed.ToLowerInvariant())
        {
            case "open":
            case "list":
                MainWindow.Toggle();
                return;
            case "settings":
            case "config":
                ConfigWindow.Toggle();
                return;
            case "stop":
                trimmed = string.Empty;
                break;
            case "syncinfo":
                PrintSyncInfo();
                return;
            case "mirror":
                if (IsMirroring) StopZoom();
                else StartMirror();
                return;
        }

        if (trimmed.Length > 0)
        {
            if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
            {
                Chat.PrintError("Usage: /tzoom [stop | open | settings | <distance>]");
                return;
            }

            config.ZoomDistance = Math.Clamp(distance, 0.2f, 20f);
            config.Save();
            Chat.Print($"Zoom In distance set to {config.ZoomDistance:0.0}.");
            return;
        }

        if (zoomTargetId != 0)
            StopZoom();
        else
            Chat.Print("Not zoomed on anyone. Right-click someone and pick Zoom In.");
    }
}
