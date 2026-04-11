namespace LibDumbVersion;

public static class CommandLineUtility
{
    public static string[] SanitizeArgs(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            return args;
        }

        var sanitized = new List<string>(args.Length);
        foreach (var arg in args)
        {
            if (arg.Contains('"'))
            {
                var parts = arg.Split('"', StringSplitOptions.RemoveEmptyEntries);
                sanitized.AddRange(parts
                    .Select(part => part.Trim())
                    .Where(trimmed => !string.IsNullOrWhiteSpace(trimmed)));
            }
            else
            {
                sanitized.Add(arg);
            }
        }

        return sanitized.ToArray();
    }
}