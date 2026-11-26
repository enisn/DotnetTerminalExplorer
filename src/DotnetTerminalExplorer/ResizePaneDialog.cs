using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer;

internal sealed class ResizePaneDialog : Dialog
{
    private readonly Func<int> _getCurrentWidth;
    private readonly Func<int> _getTotalWidth;
    private readonly Func<bool> _isAutoWidth;
    private readonly Action<int> _shrink;
    private readonly Action<int> _expand;
    private readonly Action<int> _setWidth;
    private readonly Action _resetWidth;

    public Label StatusLabel { get; }

    public ResizePaneDialog(
        Func<int> getCurrentWidth,
        Func<int> getTotalWidth,
        Func<bool> isAutoWidth,
        Action<int> shrink,
        Action<int> expand,
        Action<int> setWidth,
        Action resetWidth)
    {
        ArgumentNullException.ThrowIfNull(getCurrentWidth);
        ArgumentNullException.ThrowIfNull(getTotalWidth);
        ArgumentNullException.ThrowIfNull(isAutoWidth);
        ArgumentNullException.ThrowIfNull(shrink);
        ArgumentNullException.ThrowIfNull(expand);
        ArgumentNullException.ThrowIfNull(setWidth);
        ArgumentNullException.ThrowIfNull(resetWidth);

        _getCurrentWidth = getCurrentWidth;
        _getTotalWidth = getTotalWidth;
        _isAutoWidth = isAutoWidth;
        _shrink = shrink;
        _expand = expand;
        _setWidth = setWidth;
        _resetWidth = resetWidth;

        var totalWidth = Math.Max(40, _getTotalWidth());
        var dialogWidth = Math.Clamp(totalWidth / 2, 42, 54);

        Title = "Resize Left Pane";
        // Anchor on the right so the left FileTreePane remains visible while resizing.
        X = Pos.AnchorEnd(dialogWidth + 1);
        Y = Pos.Center();
        Width = dialogWidth;
        Height = 11;
        SetScheme(TuiSchemes.InputScheme);

        StatusLabel = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
        };

        var hintLabel = new Label
        {
            Text = "Use ←/→ or -/+ (no Alt needed). 1-4 presets.",
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
        };

        var shrinkButton = new Button
        {
            Text = "← -4",
            X = 1,
            Y = 4,
        };
        shrinkButton.Accepting += (_, e) =>
        {
            _shrink(4);
            RefreshStatus();
            e.Handled = true;
        };

        var expandButton = new Button
        {
            Text = "+4 →",
            X = Pos.Right(shrinkButton) + 1,
            Y = 4,
        };
        expandButton.Accepting += (_, e) =>
        {
            _expand(4);
            RefreshStatus();
            e.Handled = true;
        };

        var wideButton = new Button
        {
            Text = "Wide 50%",
            X = Pos.Right(expandButton) + 1,
            Y = 4,
        };
        wideButton.Accepting += (_, e) =>
        {
            ApplyPercentPreset(0.50);
            e.Handled = true;
        };

        var autoButton = new Button
        {
            Text = "Auto",
            X = Pos.Right(wideButton) + 1,
            Y = 4,
        };
        autoButton.Accepting += (_, e) =>
        {
            _resetWidth();
            RefreshStatus();
            e.Handled = true;
        };

        var doneButton = new Button
        {
            Text = "Done",
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            IsDefault = true,
        };
        doneButton.Accepting += (_, e) =>
        {
            RequestStop();
            e.Handled = true;
        };

        Add(StatusLabel, hintLabel, shrinkButton, expandButton, wideButton, autoButton, doneButton);

        void OnResizeShortcutKey(object? sender, Key keyEvent)
        {
            if (HandleResizeKey(keyEvent, handleEnter: ReferenceEquals(sender, this)))
            {
                keyEvent.Handled = true;
            }
        }

        KeyDown += OnResizeShortcutKey;
        shrinkButton.KeyDown += OnResizeShortcutKey;
        expandButton.KeyDown += OnResizeShortcutKey;
        wideButton.KeyDown += OnResizeShortcutKey;
        autoButton.KeyDown += OnResizeShortcutKey;
        doneButton.KeyDown += OnResizeShortcutKey;

        Initialized += (_, _) => doneButton.SetFocus();
        RefreshStatus();
    }

    internal bool HandleResizeKey(Key keyEvent, bool handleEnter = true)
    {
        if (keyEvent == Key.CursorLeft || IsPlainRune(keyEvent, '-') || IsPlainRune(keyEvent, '<') || IsPlainRune(keyEvent, 'h'))
        {
            _shrink(4);
            RefreshStatus();
            return true;
        }

        if (keyEvent == Key.CursorRight || IsPlainRune(keyEvent, '+') || IsPlainRune(keyEvent, '=') || IsPlainRune(keyEvent, '>') || IsPlainRune(keyEvent, 'l'))
        {
            _expand(4);
            RefreshStatus();
            return true;
        }

        if (IsPlainRune(keyEvent, '1'))
        {
            ApplyPercentPreset(0.25);
            return true;
        }

        if (IsPlainRune(keyEvent, '2') || IsPlainRune(keyEvent, '0') || IsPlainRune(keyEvent, 'r'))
        {
            _resetWidth();
            RefreshStatus();
            return true;
        }

        if (IsPlainRune(keyEvent, '3'))
        {
            ApplyPercentPreset(0.50);
            return true;
        }

        if (IsPlainRune(keyEvent, '4'))
        {
            ApplyPercentPreset(0.70);
            return true;
        }

        if (keyEvent == Key.Esc || (handleEnter && keyEvent == Key.Enter))
        {
            RequestStop();
            return true;
        }

        return false;
    }

    private void ApplyPercentPreset(double fraction)
    {
        var total = Math.Max(40, _getTotalWidth());
        _setWidth((int)Math.Round(total * fraction));
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        var current = _getCurrentWidth();
        var total = Math.Max(1, _getTotalWidth());
        var percent = (int)Math.Round((double)current * 100 / total);
        var mode = _isAutoWidth() ? "Auto" : "Custom";
        const int barWidth = 14;
        var filled = Math.Clamp((int)Math.Round((double)current * barWidth / total), 0, barWidth);
        var bar = new string('█', filled) + new string('░', barWidth - filled);
        StatusLabel.Text = $"[{bar}] {current}/{total} cols ({percent}%, {mode})";
    }

    private static bool IsPlainRune(Key keyEvent, char ch) =>
        !keyEvent.IsCtrl
        && !keyEvent.IsAlt
        && (keyEvent.AsRune.Value == ch
            || keyEvent.NoShift.AsRune.Value == ch
            || (uint)(keyEvent.KeyCode & KeyCode.CharMask) == ch);
}
