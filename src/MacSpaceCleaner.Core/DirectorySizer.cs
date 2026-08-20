namespace MacSpaceCleaner.Core;

public static class DirectorySizer
{
    public static long GetSizeBytes(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
            return 0;

        try
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }

        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // skip
                }
            }

            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var dir in dirs)
                stack.Push(dir);
        }

        return total;
    }

    public static async Task<long> GetSizeBytesAsync(string path, CancellationToken ct = default) =>
        await Task.Run(() => GetSizeBytes(path, ct), ct).ConfigureAwait(false);
}
