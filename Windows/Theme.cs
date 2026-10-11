using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace TargetZoom.Windows;

// Shared look for both windows: a dark plum palette with a rose accent,
// rounded everything, and a few custom-drawn widgets.
public static class Theme
{
    public static readonly Vector4 Bg          = Rgb(0x15, 0x11, 0x1B);
    public static readonly Vector4 Panel       = Rgb(0x1F, 0x19, 0x28);
    public static readonly Vector4 PanelHover  = Rgb(0x2A, 0x22, 0x37);
    public static readonly Vector4 PanelActive = Rgb(0x33, 0x29, 0x43);
    public static readonly Vector4 Border      = Rgb(0x3A, 0x2F, 0x4A);
    public static readonly Vector4 Accent      = Rgb(0xE0, 0x54, 0x8A);
    public static readonly Vector4 AccentHover = Rgb(0xEB, 0x6E, 0x9E);
    public static readonly Vector4 AccentDeep  = Rgb(0xB8, 0x3C, 0x6C);
    public static readonly Vector4 AccentSoft  = new(0.878f, 0.329f, 0.541f, 0.18f);
    public static readonly Vector4 Text        = Rgb(0xEE, 0xE7, 0xF4);
    public static readonly Vector4 TextDim     = Rgb(0x9A, 0x8D, 0xAC);
    public static readonly Vector4 TextFaint   = Rgb(0x6B, 0x60, 0x7B);
    public static readonly Vector4 Good        = Rgb(0x6F, 0xD3, 0xA0);
    public static readonly Vector4 Warn        = Rgb(0xF0, 0xB4, 0x5A);

    public static float Scale => ImGuiHelpers.GlobalScale;

    public static uint U32(Vector4 c) => ImGui.GetColorU32(c);

    private static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    // ---- Push / pop the whole theme around a window ----

    public sealed class Scope : IDisposable
    {
        private readonly IDisposable colors;
        private readonly IDisposable styles;
        private readonly ImGuiDir prevMenuButtonPos;

        internal Scope(IDisposable colors, IDisposable styles, ImGuiDir prevMenuButtonPos)
        {
            this.colors = colors;
            this.styles = styles;
            this.prevMenuButtonPos = prevMenuButtonPos;
        }

        public void Dispose()
        {
            ImGui.GetStyle().WindowMenuButtonPosition = prevMenuButtonPos;
            styles.Dispose();
            colors.Dispose();
        }
    }

    public static Scope Push()
    {
        var s = Scale;

        // Hide ImGui's own collapse arrow (it hugs the X). Unlike NoCollapse, this still
        // lets the window collapse, so our replacement title bar button can drive it.
        var style = ImGui.GetStyle();
        var prevMenuButtonPos = style.WindowMenuButtonPosition;
        style.WindowMenuButtonPosition = ImGuiDir.None;

        var colors = ImRaii.PushColor(ImGuiCol.WindowBg, Bg)
            .Push(ImGuiCol.ChildBg, Vector4.Zero)
            .Push(ImGuiCol.PopupBg, Panel)
            .Push(ImGuiCol.Border, Border)
            .Push(ImGuiCol.BorderShadow, Vector4.Zero)
            .Push(ImGuiCol.Text, Text)
            .Push(ImGuiCol.TextDisabled, TextDim)
            .Push(ImGuiCol.TitleBg, Bg)
            .Push(ImGuiCol.TitleBgActive, Panel)
            .Push(ImGuiCol.TitleBgCollapsed, Bg)
            .Push(ImGuiCol.FrameBg, Panel)
            .Push(ImGuiCol.FrameBgHovered, PanelHover)
            .Push(ImGuiCol.FrameBgActive, PanelActive)
            .Push(ImGuiCol.Button, PanelHover)
            .Push(ImGuiCol.ButtonHovered, PanelActive)
            .Push(ImGuiCol.ButtonActive, AccentDeep)
            .Push(ImGuiCol.Header, AccentSoft)
            .Push(ImGuiCol.HeaderHovered, PanelHover)
            .Push(ImGuiCol.HeaderActive, PanelActive)
            .Push(ImGuiCol.CheckMark, Accent)
            .Push(ImGuiCol.SliderGrab, Accent)
            .Push(ImGuiCol.SliderGrabActive, AccentHover)
            .Push(ImGuiCol.Separator, Border)
            .Push(ImGuiCol.SeparatorHovered, Accent)
            .Push(ImGuiCol.SeparatorActive, Accent)
            .Push(ImGuiCol.ScrollbarBg, Vector4.Zero)
            .Push(ImGuiCol.ScrollbarGrab, Border)
            .Push(ImGuiCol.ScrollbarGrabHovered, PanelActive)
            .Push(ImGuiCol.ScrollbarGrabActive, Accent)
            .Push(ImGuiCol.ResizeGrip, Vector4.Zero)
            .Push(ImGuiCol.ResizeGripHovered, AccentSoft)
            .Push(ImGuiCol.ResizeGripActive, Accent)
            .Push(ImGuiCol.TextSelectedBg, AccentSoft);

        var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 12f * s)
            .Push(ImGuiStyleVar.ChildRounding, 10f * s)
            .Push(ImGuiStyleVar.FrameRounding, 7f * s)
            .Push(ImGuiStyleVar.PopupRounding, 10f * s)
            .Push(ImGuiStyleVar.GrabRounding, 7f * s)
            .Push(ImGuiStyleVar.ScrollbarRounding, 8f * s)
            .Push(ImGuiStyleVar.WindowBorderSize, 1f)
            .Push(ImGuiStyleVar.PopupBorderSize, 1f)
            .Push(ImGuiStyleVar.FrameBorderSize, 0f)
            .Push(ImGuiStyleVar.ScrollbarSize, 8f * s)
            .Push(ImGuiStyleVar.GrabMinSize, 12f * s)
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(16, 14) * s)
            .Push(ImGuiStyleVar.FramePadding, new Vector2(10, 7) * s)
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 7) * s)
            // Dalamud spaces its title bar buttons by this, and the gap between its first
            // button and the close X works out to (this - FramePadding.X). 14 vs 10 leaves
            // room; the collapse icon is nudged left (see below) to even both gaps out.
            .Push(ImGuiStyleVar.ItemInnerSpacing, new Vector2(14, 6) * s)
            .Push(ImGuiStyleVar.WindowTitleAlign, new Vector2(0.5f, 0.5f));

        return new Scope(colors, styles, prevMenuButtonPos);
    }

    // ---- Title bar ----

    // ImGui's own collapse arrow sits jammed against the close X and ignores our spacing.
    // Push() hides it; this adds our own as a Dalamud title bar button instead.
    // Dalamud sorts these by Priority, highest nearest the X. Its ≡ button is int.MinValue,
    // and its sort subtracts priorities, so anything above 0 overflows and lands on the
    // wrong side. 0 puts ours between ≡ and X.
    // (Not NoCollapse: ImGui force-expands NoCollapse windows, so they can't collapse at all.)
    public static void UseCustomCollapseButton(Window window)
    {
        // We own the collapsed state and apply it every frame.
        var collapsed = false;
        window.Collapsed = false;
        window.CollapsedCondition = ImGuiCond.Always;

        TitleBarButton? button = null;
        button = new TitleBarButton
        {
            Icon = FontAwesomeIcon.CaretDown,
            IconOffset = new Vector2(-3, 1), // 5px left of normal, to center it between ≡ and X
            Priority = 0,
            AvailableClickthrough = true,
            ShowTooltip = () => ImGui.SetTooltip(collapsed ? "Expand" : "Collapse"),
            Click = _ =>
            {
                collapsed = !collapsed;
                window.Collapsed = collapsed;
                window.CollapsedCondition = ImGuiCond.Always;
                button!.Icon = collapsed ? FontAwesomeIcon.CaretRight : FontAwesomeIcon.CaretDown;
            },
        };

        window.TitleBarButtons.Add(button);
    }

    // ---- Small building blocks ----

    public static void Icon(FontAwesomeIcon icon, Vector4 color)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        using var col = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(icon.ToIconString());
    }

    public static Vector2 IconSize(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(icon.ToIconString());
    }

    public static void DrawIcon(ImDrawListPtr dl, FontAwesomeIcon icon, Vector2 pos, Vector4 color)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        dl.AddText(pos, U32(color), icon.ToIconString());
    }

    // Accent icon + caps title + a faint rule to the right.
    public static void SectionHeader(FontAwesomeIcon icon, string title)
    {
        var s = Scale;
        ImGui.Dummy(new Vector2(0, 4 * s));
        Icon(icon, Accent);
        ImGui.SameLine(0, 8 * s);
        using (ImRaii.PushColor(ImGuiCol.Text, Text))
            ImGui.TextUnformatted(title.ToUpperInvariant());

        ImGui.SameLine(0, 10 * s);
        var p = ImGui.GetCursorScreenPos();
        var y = p.Y + ImGui.GetTextLineHeight() / 2f;
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        if (right > p.X)
            ImGui.GetWindowDrawList().AddLine(new Vector2(p.X, y), new Vector2(right, y), U32(Border), 1f);
        ImGui.NewLine();
    }

    // iOS-style switch. Clicking the label toggles too.
    public static bool Toggle(string label, ref bool value, string? help = null)
    {
        var s = Scale;
        var h = ImGui.GetFrameHeight() * 0.8f;
        var w = h * 1.85f;
        var frame = ImGui.GetFrameHeight();

        var start = ImGui.GetCursorScreenPos();
        var pos = start + new Vector2(0, (frame - h) / 2f);

        var changed = false;
        if (ImGui.InvisibleButton($"##toggle_{label}", new Vector2(w, frame)))
        {
            value = !value;
            changed = true;
        }
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var track = value ? (hovered ? AccentHover : Accent) : (hovered ? PanelActive : PanelHover);
        dl.AddRectFilled(pos, pos + new Vector2(w, h), U32(track), h / 2f);
        if (!value)
            dl.AddRect(pos, pos + new Vector2(w, h), U32(Border), h / 2f);

        var r = h / 2f - 3f * s;
        var cx = value ? pos.X + w - h / 2f : pos.X + h / 2f;
        dl.AddCircleFilled(new Vector2(cx, pos.Y + h / 2f), r, U32(value ? Text : TextDim));

        ImGui.SameLine(0, 10 * s);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (ImGui.IsItemClicked())
        {
            value = !value;
            changed = true;
        }

        if (help != null)
            HelpMarker(help);

        return changed;
    }

    // Label + current value on one line, full-width slider under it.
    public static bool Slider(string label, ref float value, float min, float max, string format, string? help = null)
    {
        ImGui.TextUnformatted(label);
        if (help != null)
            HelpMarker(help);

        ImGui.SetNextItemWidth(-1);
        return ImGui.SliderFloat($"##slider_{label}", ref value, min, max, format);
    }

    // Pill-shaped segmented picker. Returns true when the selection changes.
    public static bool Segmented(string id, ref int selected, params string[] options)
    {
        var s = Scale;
        var width = ImGui.GetContentRegionAvail().X;
        var h = ImGui.GetFrameHeight();
        var segW = width / options.Length;
        var start = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(start, start + new Vector2(width, h), U32(Panel), h / 2f);
        dl.AddRect(start, start + new Vector2(width, h), U32(Border), h / 2f);

        var changed = false;
        for (var i = 0; i < options.Length; i++)
        {
            var p = start + new Vector2(segW * i, 0);
            ImGui.SetCursorScreenPos(p);
            if (ImGui.InvisibleButton($"##{id}_{i}", new Vector2(segW, h)) && selected != i)
            {
                selected = i;
                changed = true;
            }
            var hovered = ImGui.IsItemHovered();

            var isSel = selected == i;
            if (isSel)
                dl.AddRectFilled(p + new Vector2(2 * s), p + new Vector2(segW, h) - new Vector2(2 * s), U32(Accent), (h - 4 * s) / 2f);
            else if (hovered)
                dl.AddRectFilled(p + new Vector2(2 * s), p + new Vector2(segW, h) - new Vector2(2 * s), U32(PanelHover), (h - 4 * s) / 2f);

            var ts = ImGui.CalcTextSize(options[i]);
            dl.AddText(p + new Vector2((segW - ts.X) / 2f, (h - ts.Y) / 2f), U32(isSel ? Text : TextDim), options[i]);
        }

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(width, h));
        return changed;
    }

    public static void HelpMarker(string text)
    {
        ImGui.SameLine(0, 6 * Scale);
        Icon(FontAwesomeIcon.InfoCircle, TextFaint);
        if (ImGui.IsItemHovered())
        {
            using var tip = ImRaii.Tooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }
    }

    // Filled accent button with an icon.
    public static bool AccentButton(FontAwesomeIcon icon, string label, Vector2? size = null)
    {
        using var col = ImRaii.PushColor(ImGuiCol.Button, Accent)
            .Push(ImGuiCol.ButtonHovered, AccentHover)
            .Push(ImGuiCol.ButtonActive, AccentDeep)
            .Push(ImGuiCol.Text, Rgb(0xFF, 0xFF, 0xFF));
        return IconTextButton(icon, label, size);
    }

    public static bool SoftButton(FontAwesomeIcon icon, string label, Vector2? size = null) => IconTextButton(icon, label, size);

    private static bool IconTextButton(FontAwesomeIcon icon, string label, Vector2? size)
    {
        var s = Scale;
        var iconSize = IconSize(icon);
        var textSize = ImGui.CalcTextSize(label);
        var pad = ImGui.GetStyle().FramePadding;
        var gap = 6f * s;
        var natural = new Vector2(pad.X * 2 + iconSize.X + gap + textSize.X, ImGui.GetFrameHeight());
        var btnSize = size ?? natural;

        var p = ImGui.GetCursorScreenPos();
        var clicked = ImGui.Button($"##btn_{label}", btnSize);

        var contentW = iconSize.X + gap + textSize.X;
        var x = p.X + (btnSize.X - contentW) / 2f;
        var dl = ImGui.GetWindowDrawList();
        var textCol = ImGui.GetColorU32(ImGuiCol.Text);
        using (ImRaii.PushFont(UiBuilder.IconFont))
            dl.AddText(new Vector2(x, p.Y + (btnSize.Y - iconSize.Y) / 2f), textCol, icon.ToIconString());
        dl.AddText(new Vector2(x + iconSize.X + gap, p.Y + (btnSize.Y - textSize.Y) / 2f), textCol, label);
        return clicked;
    }

    // Round icon-only button (header gear etc).
    public static bool RoundIconButton(string id, FontAwesomeIcon icon, string? tooltip = null)
    {
        var size = ImGui.GetFrameHeight();
        var p = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var center = p + new Vector2(size / 2f, size / 2f);
        dl.AddCircleFilled(center, size / 2f, U32(hovered ? PanelActive : Panel));
        var iconSize = IconSize(icon);
        DrawIcon(dl, icon, center - iconSize / 2f, hovered ? Accent : TextDim);

        if (hovered && tooltip != null)
            ImGui.SetTooltip(tooltip);
        return clicked;
    }
}
