using System.Text;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.Infrastructure;

public static class DisplayText
{
    public static string ForSafety(CleanupSafety safety)
    {
        return safety switch
        {
            CleanupSafety.UseSystemCleanup => "Use system cleanup",
            _ => SplitWords(safety.ToString())
        };
    }

    public static string ForAction(CleanupActionKind action)
    {
        return SplitWords(action.ToString());
    }

    public static string SplitWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            var previous = i > 0 ? value[i - 1] : '\0';
            var next = i + 1 < value.Length ? value[i + 1] : '\0';
            var startsWord = i > 0
                && char.IsUpper(current)
                && (char.IsLower(previous) || char.IsDigit(previous) || char.IsLower(next));

            if (startsWord)
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
