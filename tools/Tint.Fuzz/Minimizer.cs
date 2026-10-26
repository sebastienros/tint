namespace Tint.Fuzz;

/// <summary>Shrinks an input while it still shows the same kind of finding: lines first, then characters.</summary>
public static class Minimizer
{
    /// <summary>The smallest input found within <paramref name="budget"/> runs of <paramref name="reproduces"/>.</summary>
    public static string[] Minimize(string[] lines, Func<string[], bool> reproduces, int budget)
    {
        var runs = 0;
        bool Try(string[] candidate) => runs++ < budget && reproduces(candidate);

        var current = lines;
        for (var chunk = Math.Max(1, current.Length / 2); chunk >= 1; chunk /= 2)
        {
            for (var start = 0; start + chunk <= current.Length && current.Length > 1;)
            {
                var candidate = current[..start].Concat(current[(start + chunk)..]).ToArray();
                if (candidate.Length > 0 && Try(candidate))
                {
                    current = candidate;
                }
                else
                {
                    start += chunk;
                }
            }
        }

        for (var index = 0; index < current.Length; index++)
        {
            var line = current[index];
            for (var chunk = Math.Max(1, line.Length / 2); chunk >= 1; chunk /= 2)
            {
                for (var start = 0; start + chunk <= line.Length;)
                {
                    var candidate = (string[])current.Clone();
                    candidate[index] = line.Remove(start, chunk);
                    if (Try(candidate))
                    {
                        current = candidate;
                        line = candidate[index];
                    }
                    else
                    {
                        start += chunk;
                    }
                }

                if (runs >= budget)
                {
                    return current;
                }
            }
        }

        return current;
    }
}
