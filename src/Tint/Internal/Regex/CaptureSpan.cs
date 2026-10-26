namespace Tint.Internal.Regex;

// A capture of a match as vscode-textmate sees it: start and end offsets in the line. Groups that did not participate
// are (0, 0), as Onigwrap reports them; vscode-textmate treats them like any empty capture.
internal readonly struct CaptureSpan
{
    public CaptureSpan(int start, int end)
    {
        Start = start;
        End = end;
    }

    public int Start { get; }

    public int End { get; }

    public int Length => End - Start;
}
