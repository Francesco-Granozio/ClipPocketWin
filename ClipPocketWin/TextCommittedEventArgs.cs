using System;

namespace ClipPocketWin;

public sealed class TextCommittedEventArgs : EventArgs
{
    public TextCommittedEventArgs(string editedText)
    {
        EditedText = editedText;
    }

    public string EditedText { get; }
}
