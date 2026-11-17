using System.Text;

namespace Stash
{
    internal static class CommandLine
    {
        // Quotes one argument using the CommandLineToArgvW rules, so values ending in
        // a backslash (for example "D:\") or containing quotes survive intact.
        internal static string Quote(string value)
        {
            value = value ?? "";
            StringBuilder result = new StringBuilder(value.Length + 2);
            result.Append('"');
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                }
                else
                {
                    result.Append('\\', backslashes);
                    result.Append(c);
                }
                backslashes = 0;
            }
            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }
    }
}
