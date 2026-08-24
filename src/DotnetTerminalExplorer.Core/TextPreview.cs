namespace DotnetTerminalExplorer.Core;

public sealed record TextPreview(TextPreviewKind Kind, string Text)
{
    public static TextPreview FromContent(string content) =>
        new(TextPreviewKind.Content, content);

    public static TextPreview ForDirectory() =>
        new(TextPreviewKind.Directory, "Select a file to preview its contents.");

    public static TextPreview ForBinary(string message) =>
        new(TextPreviewKind.Binary, message);

    public static TextPreview ForImage(string headerInfo) =>
        new(TextPreviewKind.Image, headerInfo);

    public static TextPreview FromError(string message) =>
        new(TextPreviewKind.Error, message);
}
