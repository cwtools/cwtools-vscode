namespace CSharpExtensions;

public static class DocumentStoreHelper
{
    public static string GetTextAtPosition(string fileText, int line, int character)
    {
        int currentLine = 0;
        ReadOnlySpan<char> currentLineText = null;
        foreach (var lineText in fileText.AsSpan().EnumerateLines())
        {
            if (currentLine == line)
            {
                currentLineText = lineText;
                break;
            }
            ++currentLine;
        }

        if (currentLineText.IsEmpty)
        {
            return string.Empty;
        }

        int start = 0;
        int end = 0;
        ReadOnlySpan<char> excludedChars = ['=', ' ', '{', '}', '\t', '"'];
        int adjustedPosition = Math.Min(character, currentLineText.Length - 1);
        for (int offset = adjustedPosition; offset >= 0; offset--)
        {
            if (excludedChars.Contains(currentLineText[offset]))
            {
                break;
            }
            start = offset;
        }

        for (int offset = adjustedPosition; offset < currentLineText.Length; offset++)
        {
            if (excludedChars.Contains(currentLineText[offset]))
            {
                break;
            }
            end = offset;
        }

        int length = start == 0 && end == 0 ? 0 : end - start + 1;
        return currentLineText.Slice(start, length).ToString();
    }
}