using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace TargetZoom.Windows;

// Lists everyone nearby as cards. Right-click a card for options, double-click to zoom.
public sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private string filter = string.Empty;

    // Send Tell popup
    private string tellName = string.Empty;
    private string tellWorld = string.Empty;
    private string tellText = string.Empty;
    private string tellError = string.Empty;
    private bool openTellPopup;
    private bool focusTellInput;
    private Theme.Scope? theme;

    private record struct Entry(ICharacter Character, ulong Id, string Name, string Subtitle, uint IconId, float Distance, SyncSource Sync);

    private static readonly string[] SyncFilterLabels = ["Everyone", "Any sync", "Player Sync", "Lightless"];

    public MainWindow(Plugin plugin) : base("Target Zoom##TargetZoomMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        Size = new Vector2(360, 540);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 320),
            MaximumSize = new Vector2(900, 1400),
        };
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
        var local = Plugin.ObjectTable.LocalPlayer;
        if (local == null)
        {
            DrawEmpty(FontAwesomeIcon.UserSlash, "Not logged in", "Log in to a character to see who's around.");
            return;
        }

        var people = GetNearby(local, plugin.Config);

        DrawHeader(people.Count);
        DrawStatusCard(local);
        if (plugin.ZoomTargetId != 0 && plugin.Config.ShowPanPad)
            DrawPanPad();
        DrawSearch();
        DrawSyncFilter();

        var s = Theme.Scale;
        var footerH = ImGui.GetTextLineHeight() + 10 * s;

        using (var child = ImRaii.Child("##people", new Vector2(-1, -footerH), false))
        {
            if (child)
            {
                if (people.Count == 0)
                {
                    var syncFiltered = SyncFilterActive;
                    DrawEmpty(FontAwesomeIcon.Wind,
                        filter.Length > 0 || syncFiltered ? "No matches" : "No one nearby",
                        filter.Length > 0 ? "Try a different name."
                        : syncFiltered ? $"No {SyncFilterLabels[plugin.Config.ListSyncFilter]} users within {plugin.Config.ListRange:0} yalms."
                        : $"Nobody within {plugin.Config.ListRange:0} yalms.");
                }
                else
                {
                    foreach (var e in people)
                        DrawRow(e);
                }
            }
        }

        DrawFooter();
        DrawTellPopup();
    }

    // ---- Send Tell popup ----

    private void DrawTellPopup()
    {
        const string id = "##tellpopup";
        if (openTellPopup)
        {
            ImGui.OpenPopup(id);
            openTellPopup = false;
            focusTellInput = true;
        }

        var s = Theme.Scale;
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(14, 12) * s);
        ImGui.SetNextWindowSize(new Vector2(320 * s, 0));
        using var popup = ImRaii.Popup(id);
        if (!popup)
            return;

        Theme.Icon(FontAwesomeIcon.CommentDots, Theme.Accent);
        ImGui.SameLine(0, 8 * s);
        ImGui.TextUnformatted($"Tell {tellName}");
        if (tellWorld.Length > 0)
        {
            ImGui.SameLine(0, 6 * s);
            ImGui.TextDisabled($"@{tellWorld}");
        }
        ImGui.Separator();

        if (focusTellInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusTellInput = false;
        }
        ImGui.SetNextItemWidth(-1);
        var enter = ImGui.InputTextWithHint("##tellmsg", "Say something nice…", ref tellText, 400, ImGuiInputTextFlags.EnterReturnsTrue);

        if (tellError.Length > 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Warn))
                ImGui.TextWrapped(tellError);
        }

        ImGui.Dummy(new Vector2(0, 2 * s));
        var half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        if (Theme.SoftButton(FontAwesomeIcon.Times, "Cancel", new Vector2(half, ImGui.GetFrameHeight())))
            ImGui.CloseCurrentPopup();
        ImGui.SameLine();
        var send = Theme.AccentButton(FontAwesomeIcon.PaperPlane, "Send", new Vector2(half, ImGui.GetFrameHeight()));

        if (send || enter)
        {
            if (plugin.SendTell(tellName, tellWorld, tellText, out var error))
                ImGui.CloseCurrentPopup();
            else
                tellError = error;
        }
    }

    // ---- Header: title, count, settings gear ----

    private void DrawHeader(int count)
    {
        var s = Theme.Scale;

        Theme.Icon(FontAwesomeIcon.Crosshairs, Theme.Accent);
        ImGui.SameLine(0, 8 * s);
        ImGui.TextUnformatted("Nearby");
        ImGui.SameLine(0, 8 * s);

        // count pill
        var label = count.ToString();
        var ts = ImGui.CalcTextSize(label);
        var p = ImGui.GetCursorScreenPos();
        var pill = new Vector2(ts.X + 14 * s, ImGui.GetTextLineHeight() + 2 * s);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p - new Vector2(0, 1 * s), p + pill - new Vector2(0, 1 * s), Theme.U32(Theme.AccentSoft), pill.Y / 2f);
        dl.AddText(p + new Vector2(7 * s, 0), Theme.U32(Theme.Accent), label);
        ImGui.Dummy(pill);

        var gear = ImGui.GetFrameHeight();
        var gap = 6 * s;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - gear * 2 - gap);
        var rowY = ImGui.GetCursorPosY() - (gear - ImGui.GetTextLineHeight()) / 2f;
        ImGui.SetCursorPosY(rowY);

        // Show / hide the game UI (HUD, chat, nameplates). Target Zoom's window stays up either way.
        var hidden = plugin.IsGameUiHidden;
        if (Theme.RoundIconButton("##gameui", hidden ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye,
                hidden ? "Show the game UI" : "Hide the game UI"))
            plugin.SetGameUiHidden(!hidden);

        ImGui.SameLine(0, gap);
        ImGui.SetCursorPosY(rowY);
        if (Theme.RoundIconButton("##settings", FontAwesomeIcon.Cog, "Settings"))
            plugin.ConfigWindow.Toggle();

        ImGui.Dummy(new Vector2(0, 2 * s));
    }

    // ---- Status card: who you're zoomed on ----

    private void DrawStatusCard(IGameObject local)
    {
        var s = Theme.Scale;
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var height = 58 * s;
        var start = ImGui.GetCursorScreenPos();
        var end = start + new Vector2(width, height);

        var current = plugin.ZoomTargetId != 0 ? Plugin.ObjectTable.SearchById(plugin.ZoomTargetId) as ICharacter : null;

        if (current != null)
        {
            // accent gradient card
            dl.AddRectFilledMultiColor(start, end,
                Theme.U32(new Vector4(0.878f, 0.329f, 0.541f, 0.30f)),
                Theme.U32(new Vector4(0.878f, 0.329f, 0.541f, 0.08f)),
                Theme.U32(new Vector4(0.878f, 0.329f, 0.541f, 0.08f)),
                Theme.U32(new Vector4(0.878f, 0.329f, 0.541f, 0.30f)));
            dl.AddRect(start, end, Theme.U32(new Vector4(0.878f, 0.329f, 0.541f, 0.55f)), 10 * s, ImDrawFlags.None, 1.2f);

            var iconSize = 38 * s;
            var iconPos = start + new Vector2(12 * s, (height - iconSize) / 2f);
            DrawCharacterIcon(dl, JobIcon(current), current.Name.TextValue, iconPos, iconSize);

            var textX = iconPos.X + iconSize + 12 * s;
            var line = ImGui.GetTextLineHeight();
            var top = start.Y + (height - line * 2 - 2 * s) / 2f;

            var pulse = 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 4f);
            dl.AddCircleFilled(new Vector2(textX + 4 * s, top + line / 2f), 3.5f * s, Theme.U32(Theme.Accent with { W = pulse }));
            dl.AddText(new Vector2(textX + 13 * s, top), Theme.U32(Theme.Accent), plugin.IsMirroring ? "MIRROR" : "ZOOMED IN");
            dl.AddText(new Vector2(textX, top + line + 2 * s), Theme.U32(Theme.Text), current.Name.TextValue);

            // No Stop button: stop from the list (double-click / right-click), Esc, or /tzoom.
            // The card itself also stops on double-click, which covers Mirror (you're not in the list).
            ImGui.SetCursorScreenPos(start);
            ImGui.InvisibleButton("##statuscard", new Vector2(width, height));
            if (ImGui.IsItemHovered())
            {
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    plugin.StopZoom();
                else
                    ImGui.SetTooltip("Double-click to stop zooming");
            }
        }
        else
        {
            dl.AddRectFilled(start, end, Theme.U32(Theme.Panel), 10 * s);
            dl.AddRect(start, end, Theme.U32(Theme.Border), 10 * s, ImDrawFlags.None, 1f);

            var iconSize = Theme.IconSize(FontAwesomeIcon.Video);
            var circle = 38 * s;
            var cpos = start + new Vector2(12 * s, (height - circle) / 2f);
            dl.AddCircleFilled(cpos + new Vector2(circle / 2f), circle / 2f, Theme.U32(Theme.PanelHover));
            Theme.DrawIcon(dl, FontAwesomeIcon.Video, cpos + new Vector2(circle / 2f) - iconSize / 2f, Theme.TextDim);

            var textX = cpos.X + circle + 12 * s;
            var line = ImGui.GetTextLineHeight();
            var top = start.Y + (height - line * 2 - 2 * s) / 2f;
            dl.AddText(new Vector2(textX, top), Theme.U32(Theme.Text), "Camera is on you");
            dl.AddText(new Vector2(textX, top + line + 2 * s), Theme.U32(Theme.TextDim), "Right-click someone to zoom");

            // Mirror button on the right
            var btnLabel = "Mirror";
            var btnW = Theme.IconSize(FontAwesomeIcon.UserCircle).X + ImGui.CalcTextSize(btnLabel).X + 32 * s;
            var btnH = ImGui.GetFrameHeight();
            ImGui.SetCursorScreenPos(new Vector2(end.X - btnW - 12 * s, start.Y + (height - btnH) / 2f));
            if (Theme.SoftButton(FontAwesomeIcon.UserCircle, btnLabel, new Vector2(btnW, btnH)))
                plugin.StartMirror();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Look at yourself from the front, like a mirror.");
        }

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(width, height));
    }

    // ---- Framing pad: drag to slide the view around them ----

    private void DrawPanPad()
    {
        var s = Theme.Scale;
        var cfg = plugin.Config;
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var height = 96 * s;
        var start = ImGui.GetCursorScreenPos();
        var end = start + new Vector2(width, height);
        var center = start + new Vector2(width, height) / 2f;
        var range = MathF.Max(0.5f, cfg.PanRange);

        ImGui.InvisibleButton("##panpad", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
        {
            var delta = ImGui.GetIO().MouseDelta;
            var k = 0.02f * cfg.PanSensitivity / s;
            plugin.PanRight = Math.Clamp(plugin.PanRight + delta.X * k, -range, range);
            plugin.PanUp = Math.Clamp(plugin.PanUp - delta.Y * k, -range, range);
        }

        if (hovered && (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
            plugin.ResetPan();

        // background + grid
        dl.AddRectFilled(start, end, Theme.U32(active ? Theme.PanelHover : Theme.Panel), 10 * s);
        dl.AddRect(start, end, Theme.U32(active ? Theme.Accent with { W = 0.6f } : Theme.Border), 10 * s, ImDrawFlags.None, 1f);
        var grid = Theme.U32(Theme.Border with { W = 0.5f });
        for (var i = 1; i < 4; i++)
        {
            var x = start.X + width * i / 4f;
            var y = start.Y + height * i / 4f;
            dl.AddLine(new Vector2(x, start.Y + 6 * s), new Vector2(x, end.Y - 6 * s), grid, 1f);
            dl.AddLine(new Vector2(start.X + 6 * s, y), new Vector2(end.X - 6 * s, y), grid, 1f);
        }

        // centre crosshair (= on them)
        var cross = Theme.U32(Theme.TextFaint);
        dl.AddLine(center - new Vector2(7 * s, 0), center + new Vector2(7 * s, 0), cross, 1.5f);
        dl.AddLine(center - new Vector2(0, 7 * s), center + new Vector2(0, 7 * s), cross, 1.5f);

        // current offset dot
        var halfW = width / 2f - 12 * s;
        var halfH = height / 2f - 12 * s;
        var dot = center + new Vector2(plugin.PanRight / range * halfW, -plugin.PanUp / range * halfH);
        if (plugin.PanRight != 0 || plugin.PanUp != 0)
            dl.AddLine(center, dot, Theme.U32(Theme.Accent with { W = 0.5f }), 1.5f);
        dl.AddCircleFilled(dot, 8 * s, Theme.U32(Theme.Accent with { W = 0.25f }));
        dl.AddCircleFilled(dot, 5 * s, Theme.U32(active ? Theme.AccentHover : Theme.Accent));

        // labels
        var line = ImGui.GetTextLineHeight();
        dl.AddText(start + new Vector2(10 * s, 6 * s), Theme.U32(Theme.TextDim), "FRAMING");
        var hint = active ? $"{plugin.PanRight:+0.0;-0.0;0.0}, {plugin.PanUp:+0.0;-0.0;0.0}" : "Drag to pan · double-click to reset";
        var hs = ImGui.CalcTextSize(hint);
        dl.AddText(new Vector2(end.X - hs.X - 10 * s, end.Y - line - 6 * s), Theme.U32(Theme.TextFaint), hint);

        if (hovered && !active)
            ImGui.SetTooltip("Drag to slide the camera around them.\nDouble-click or right-click to re-center.");
    }

    // ---- Search box with a magnifier inside ----

    private void DrawSearch()
    {
        var s = Theme.Scale;
        var iconSize = Theme.IconSize(FontAwesomeIcon.Search);
        var pad = ImGui.GetStyle().FramePadding;

        var start = ImGui.GetCursorScreenPos();
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(pad.X + iconSize.X + 10 * s, pad.Y)))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##filter", "Search by name", ref filter, 64);
        }

        var dl = ImGui.GetWindowDrawList();
        var iconPos = start + new Vector2(pad.X + 2 * s, (ImGui.GetFrameHeight() - iconSize.Y) / 2f);
        Theme.DrawIcon(dl, FontAwesomeIcon.Search, iconPos, filter.Length > 0 ? Theme.Accent : Theme.TextFaint);

        ImGui.Dummy(new Vector2(0, 2 * s));
    }

    // ---- Sync filter (only shown when Player Sync / Lightless is running) ----

    private bool SyncFilterActive => plugin.AnySyncPluginFound && plugin.Config.ListSyncFilter != 0;

    private void DrawSyncFilter()
    {
        if (!plugin.AnySyncPluginFound)
            return;

        var cfg = plugin.Config;
        var sel = Math.Clamp(cfg.ListSyncFilter, 0, SyncFilterLabels.Length - 1);
        if (Theme.Segmented("syncfilter", ref sel, SyncFilterLabels))
        {
            cfg.ListSyncFilter = sel;
            cfg.Save();
        }

        ImGui.Dummy(new Vector2(0, 2 * Theme.Scale));
    }

    private static bool PassesSyncFilter(SyncSource sync, int filterIndex) => filterIndex switch
    {
        1 => sync != SyncSource.None,
        2 => sync == SyncSource.PlayerSync,
        3 => sync == SyncSource.Lightless,
        _ => true,
    };

    private static (string Label, Vector4 Color) SyncBadge(SyncSource sync) => sync switch
    {
        SyncSource.PlayerSync => ("PS", new Vector4(0.45f, 0.70f, 1.00f, 1f)),
        SyncSource.Lightless => ("LL", new Vector4(0.80f, 0.62f, 1.00f, 1f)),
        SyncSource.Other => ("SYNC", Theme.TextDim),
        _ => ("", Theme.TextDim),
    };

    // ---- One person ----

    private void DrawRow(Entry e)
    {
        var s = Theme.Scale;
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var height = 46 * s;
        var start = ImGui.GetCursorScreenPos();
        var end = start + new Vector2(width, height);
        var isZoomed = plugin.ZoomTargetId == e.Id;
        var popupId = $"##ctx{e.Id}";

        ImGui.InvisibleButton($"##row{e.Id}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(popupId);

        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (isZoomed) plugin.StopZoom();
            else plugin.StartZoom(e.Id);
        }

        // card
        var bg = isZoomed ? Theme.AccentSoft : hovered ? Theme.PanelHover : Theme.Panel;
        dl.AddRectFilled(start, end, Theme.U32(bg), 9 * s);
        if (isZoomed)
        {
            dl.AddRect(start, end, Theme.U32(Theme.Accent with { W = 0.6f }), 9 * s, ImDrawFlags.None, 1.2f);
            dl.AddRectFilled(start + new Vector2(0, 8 * s), new Vector2(start.X + 3 * s, end.Y - 8 * s), Theme.U32(Theme.Accent), 2 * s);
        }
        else if (hovered)
        {
            dl.AddRect(start, end, Theme.U32(Theme.Border), 9 * s, ImDrawFlags.None, 1f);
        }

        // icon
        var iconSize = 30 * s;
        var iconPos = start + new Vector2(10 * s, (height - iconSize) / 2f);
        DrawCharacterIcon(dl, e.IconId, e.Name, iconPos, iconSize);

        // name + subtitle
        var line = ImGui.GetTextLineHeight();
        var textX = iconPos.X + iconSize + 10 * s;
        var top = start.Y + (height - line * 2) / 2f;
        dl.AddText(new Vector2(textX, top), Theme.U32(isZoomed ? Theme.Accent : Theme.Text), e.Name);
        dl.AddText(new Vector2(textX, top + line), Theme.U32(Theme.TextDim), e.Subtitle);

        // pills on the right: [sync] [distance]
        var right = end.X - 10 * s;

        if (plugin.Config.ListShowDistance)
        {
            var label = $"{e.Distance:0.0}y";
            var ts = ImGui.CalcTextSize(label);
            var pill = new Vector2(ts.X + 14 * s, line + 4 * s);
            var pPos = new Vector2(right - pill.X, start.Y + (height - pill.Y) / 2f);
            var col = e.Distance < 10 ? Theme.Good : e.Distance < 30 ? Theme.Warn : Theme.TextDim;
            dl.AddRectFilled(pPos, pPos + pill, Theme.U32(col with { W = 0.14f }), pill.Y / 2f);
            dl.AddText(pPos + new Vector2(7 * s, 2 * s), Theme.U32(col), label);
            right = pPos.X - 6 * s;
        }

        if (plugin.Config.ListShowSyncBadge && e.Sync != SyncSource.None)
        {
            var (label, col) = SyncBadge(e.Sync);
            var ts = ImGui.CalcTextSize(label);
            var pill = new Vector2(ts.X + 12 * s, line + 4 * s);
            var pPos = new Vector2(right - pill.X, start.Y + (height - pill.Y) / 2f);
            dl.AddRect(pPos, pPos + pill, Theme.U32(col with { W = 0.6f }), pill.Y / 2f, ImDrawFlags.None, 1f);
            dl.AddText(pPos + new Vector2(6 * s, 2 * s), Theme.U32(col), label);
        }

        if (hovered && !ImGui.IsPopupOpen(popupId))
            ImGui.SetTooltip(isZoomed ? "Double-click to stop · Right-click for options" : "Double-click to zoom in · Right-click for options");

        DrawRowMenu(e, popupId, isZoomed);
    }

    private void DrawRowMenu(Entry e, string popupId, bool isZoomed)
    {
        var s = Theme.Scale;
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(10, 10) * s)
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4) * s);
        using var popup = ImRaii.Popup(popupId);
        if (!popup)
            return;

        // mini header
        DrawCharacterIcon(ImGui.GetWindowDrawList(), e.IconId, e.Name, ImGui.GetCursorScreenPos(), 22 * s);
        ImGui.Dummy(new Vector2(22 * s, 22 * s));
        ImGui.SameLine(0, 8 * s);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (22 * s - ImGui.GetTextLineHeight()) / 2f);
        ImGui.TextUnformatted(e.Name);
        ImGui.Separator();

        var w = 180 * s;
        if (isZoomed)
        {
            if (MenuRow(FontAwesomeIcon.Times, "Stop Zooming", w, Theme.Accent))
            {
                plugin.StopZoom();
                ImGui.CloseCurrentPopup();
            }
        }
        else if (MenuRow(FontAwesomeIcon.SearchPlus, "Zoom In", w, Theme.Accent))
        {
            plugin.StartZoom(e.Id);
            ImGui.CloseCurrentPopup();
        }

        if (MenuRow(FontAwesomeIcon.Bullseye, "Target", w, Theme.TextDim))
        {
            Plugin.TargetManager.Target = e.Character;
            ImGui.CloseCurrentPopup();
        }

        if (e.Character.ObjectKind == ObjectKind.Pc && MenuRow(FontAwesomeIcon.Tshirt, "Examine", w, Theme.TextDim))
        {
            plugin.Examine(e.Character);
            ImGui.CloseCurrentPopup();
        }

        if (e.Character is IPlayerCharacter pc && MenuRow(FontAwesomeIcon.CommentDots, "Send Tell", w, Theme.TextDim))
        {
            tellName = pc.Name.TextValue;
            tellWorld = pc.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;
            tellText = string.Empty;
            tellError = string.Empty;
            openTellPopup = true;
            ImGui.CloseCurrentPopup();
        }
    }

    // A full-width menu row with an icon, drawn ourselves for nicer hover.
    private static bool MenuRow(FontAwesomeIcon icon, string label, float width, Vector4 iconColor)
    {
        var s = Theme.Scale;
        var h = ImGui.GetFrameHeight();
        var p = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##menu_{label}", new Vector2(width, h));
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        if (hovered)
            dl.AddRectFilled(p, p + new Vector2(width, h), Theme.U32(Theme.PanelActive), 6 * s);

        var iconSize = Theme.IconSize(icon);
        Theme.DrawIcon(dl, icon, p + new Vector2(8 * s, (h - iconSize.Y) / 2f), hovered ? Theme.Accent : iconColor);
        dl.AddText(p + new Vector2(8 * s + 20 * s, (h - ImGui.GetTextLineHeight()) / 2f), Theme.U32(Theme.Text), label);
        return clicked;
    }

    // ---- Footer hint ----

    private static void DrawFooter()
    {
        var s = Theme.Scale;
        ImGui.Dummy(new Vector2(0, 2 * s));
        Theme.Icon(FontAwesomeIcon.MousePointer, Theme.TextFaint);
        ImGui.SameLine(0, 6 * s);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.TextFaint))
            ImGui.TextUnformatted("Double-click to zoom · Right-click for more");
    }

    private static void DrawEmpty(FontAwesomeIcon icon, string title, string body)
    {
        var s = Theme.Scale;
        var avail = ImGui.GetContentRegionAvail();
        var left = ImGui.GetCursorPosX();
        var iconSize = Theme.IconSize(icon);
        var titleSize = ImGui.CalcTextSize(title);
        var bodySize = ImGui.CalcTextSize(body);
        var blockH = iconSize.Y + titleSize.Y + bodySize.Y + 20 * s;

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Math.Max(0, (avail.Y - blockH) / 2.5f));

        ImGui.SetCursorPosX(left + (avail.X - iconSize.X) / 2f);
        Theme.Icon(icon, Theme.TextFaint);
        ImGui.Dummy(new Vector2(0, 4 * s));

        ImGui.SetCursorPosX(left + (avail.X - titleSize.X) / 2f);
        ImGui.TextUnformatted(title);

        ImGui.SetCursorPosX(left + (avail.X - bodySize.X) / 2f);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.TextDim))
            ImGui.TextUnformatted(body);
    }

    // Job icon if we have one, otherwise a soft circle with their initial.
    private static void DrawCharacterIcon(ImDrawListPtr dl, uint iconId, string name, Vector2 pos, float size)
    {
        var s = Theme.Scale;
        if (iconId != 0)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            dl.AddImage(wrap.Handle, pos, pos + new Vector2(size, size));
            return;
        }

        var center = pos + new Vector2(size / 2f);
        dl.AddCircleFilled(center, size / 2f, Theme.U32(Theme.PanelActive));
        dl.AddCircle(center, size / 2f, Theme.U32(Theme.Border), 0, 1f * s);
        var initial = string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();
        var ts = ImGui.CalcTextSize(initial);
        dl.AddText(center - ts / 2f, Theme.U32(Theme.TextDim), initial);
    }

    private static uint JobIcon(ICharacter c)
    {
        var job = c.ClassJob.RowId;
        return c.ObjectKind == ObjectKind.Pc && job != 0 ? 62100 + job : 0;
    }

    // ---- Data ----

    private List<Entry> GetNearby(IGameObject local, Configuration config)
    {
        var list = new List<Entry>();

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not ICharacter character || obj.Address == local.Address)
                continue;

            var isPlayer = obj.ObjectKind == ObjectKind.Pc;
            if (!isPlayer && !(config.ListIncludeNpcs && obj.ObjectKind is ObjectKind.BattleNpc or ObjectKind.EventNpc))
                continue;

            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name))
                continue;

            if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            var distance = Vector3.Distance(local.Position, obj.Position);
            if (distance > config.ListRange)
                continue;

            var sync = plugin.GetSync(obj.Address);
            if (SyncFilterActive && !PassesSyncFilter(sync, config.ListSyncFilter))
                continue;

            list.Add(new Entry(character, obj.GameObjectId, name, Subtitle(character), JobIcon(character), distance, sync));
        }

        return config.ListSortByName
            ? list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : list.OrderBy(e => e.Distance).ToList();
    }

    private static string Subtitle(ICharacter c)
    {
        if (c is not IPlayerCharacter pc)
            return "NPC";

        var job = pc.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? "";
        var world = pc.HomeWorld.ValueNullable?.Name.ExtractText() ?? "";
        var parts = new List<string>();
        if (job.Length > 0) parts.Add($"{job} {pc.Level}");
        if (world.Length > 0) parts.Add(world);
        return parts.Count > 0 ? string.Join("  ·  ", parts) : "Player";
    }
}
