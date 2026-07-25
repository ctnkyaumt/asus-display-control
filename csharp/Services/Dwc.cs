using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AsusDisplayControl;

/// <summary>Thin wrapper around the ASUS dwc.exe CLI (locating it, running commands, parsing).</summary>
internal static class Dwc
{
    public static string? Path { get; } = Locate();

    /// <summary>Run dwc.exe with the given args. Returns stdout; throws on a non-zero exit.</summary>
    public static string Run(params string[] args)
    {
        if (string.IsNullOrEmpty(Path))
            throw new FileNotFoundException("ASUS Display Control CLI (dwc.exe) was not found.");

        var psi = new ProcessStartInfo
        {
            FileName = Path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dwc.exe");
        // dwc emits tiny outputs, so sequential reads won't deadlock.
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            string msg = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            throw new InvalidOperationException(msg);
        }
        return stdout;
    }

    /// <summary>Get a property as an int, or null if unsupported / unreadable.</summary>
    public static int? GetInt(string prop, string monitorId)
    {
        try { return int.Parse(Run("get", prop, "--id", monitorId).Trim()); }
        catch { return null; }
    }

    /// <summary>
    /// Read several properties at once. Uses dedicated threads rather than the thread pool: each
    /// read blocks on a child process (~0.1s when supported, ~1.3s when the monitor rejects the
    /// code) and a pool-based fan-out starves on thread injection. Parallelism buys little — the
    /// DDC/CI bus serialises anyway — so the worker count stays low, and one batch runs at a time.
    /// </summary>
    public static Dictionary<string, int?> ProbeMany(IReadOnlyCollection<string> props, string monitorId, int workers = 2)
    {
        ProbeGate.Wait();
        try
        {
            var results = new System.Collections.Concurrent.ConcurrentDictionary<string, int?>();
            var queue = new System.Collections.Concurrent.ConcurrentQueue<string>(props);
            RunWorkers(Math.Min(workers, props.Count),
                       () => { while (queue.TryDequeue(out var p)) results[p] = GetInt(p, monitorId); });
            return props.ToDictionary(p => p, p => results.GetValueOrDefault(p));
        }
        finally { ProbeGate.Release(); }
    }

    /// <summary>Write several properties, ignoring individual failures. Same threading rationale as <see cref="ProbeMany"/>.</summary>
    public static void WriteMany(IReadOnlyCollection<(string prop, int value)> writes, string monitorId, int workers = 2)
    {
        var queue = new System.Collections.Concurrent.ConcurrentQueue<(string prop, int value)>(writes);
        RunWorkers(Math.Min(workers, writes.Count), () =>
        {
            while (queue.TryDequeue(out var w))
                try { Run("set", w.prop, w.value.ToString(), "--id", monitorId); } catch { }
        });
    }

    private static void RunWorkers(int count, Action worker)
    {
        var threads = new List<Thread>();
        for (int i = 0; i < count; i++)
        {
            var t = new Thread(() => worker()) { IsBackground = true };
            t.Start();
            threads.Add(t);
        }
        foreach (var t in threads) t.Join();
    }

    private static readonly SemaphoreSlim ProbeGate = new(1, 1);

    public record Monitor(string Id, string Model);

    /// <summary>Parse the `dwc list` table into monitors.</summary>
    public static List<Monitor> ParseList(string output)
    {
        var monitors = new List<Monitor>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("ID") || line.StartsWith("--") || line.StartsWith("Detected")) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) monitors.Add(new Monitor(parts[0], parts[1]));
        }
        return monitors;
    }

    /// <summary>Extract the ColorTemp codes (VCP 0x14) a monitor advertises, or null if unknown.</summary>
    public static List<int>? ParseSupportedColorTemps(string caps)
    {
        var m = Regex.Match(caps, @"[\s(]14\(([0-9A-Fa-f ]+)\)");
        if (!m.Success) return null;
        var codes = new List<int>();
        foreach (var tok in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(tok, System.Globalization.NumberStyles.HexNumber, null, out int c))
                codes.Add(c);
        return codes.Count > 0 ? codes : null;
    }

    private static string? Locate()
    {
        // Search dirs: next to the app (published layout), the process dir, then PATH.
        var bases = new List<string>();
        try { bases.Add(AppContext.BaseDirectory); } catch { }
        try { var d = System.IO.Path.GetDirectoryName(Environment.ProcessPath); if (d != null) bases.Add(d); } catch { }

        string[] rels =
        {
            System.IO.Path.Combine("cli", "windows", "dwc", "dwc.exe"),
            System.IO.Path.Combine("bin", "dwc", "dwc.exe"),
            "dwc.exe",
        };
        foreach (var b in bases)
            foreach (var rel in rels)
            {
                var cand = System.IO.Path.Combine(b, rel);
                if (File.Exists(cand)) return cand;
            }

        // Fall back to PATH.
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(System.IO.Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var cand = System.IO.Path.Combine(dir.Trim(), "dwc.exe");
                if (File.Exists(cand)) return cand;
            }
            catch { }
        }
        return null;
    }
}
