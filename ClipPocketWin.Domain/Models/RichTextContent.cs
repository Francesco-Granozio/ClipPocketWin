namespace ClipPocketWin.Domain.Models;

public sealed record RichTextContent(byte[]? RtfData, byte[]? HtmlData, string PlainText)
{
    public string DisplayString => PlainText.Length > 100 ? PlainText[..100] : PlainText;
}
