using Terminal.Gui.Drawing;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace DotnetTerminalExplorer;

internal static class TuiSchemes
{
    public static readonly TuiAttribute ContentAttribute =
        new(ColorName16.White, ColorName16.Black);

    public static readonly TuiAttribute InputAttribute =
        new(ColorName16.Black, ColorName16.White);

    public static readonly Scheme InputScheme = new()
    {
        Normal = ContentAttribute,
        HotNormal = ContentAttribute,
        Focus = InputAttribute,
        HotFocus = InputAttribute,
        Active = InputAttribute,
        HotActive = InputAttribute,
        Highlight = InputAttribute,
        Editable = InputAttribute,
        ReadOnly = ContentAttribute,
        Disabled = ContentAttribute,
    };
}
