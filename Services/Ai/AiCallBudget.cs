using System.Collections.Concurrent;

namespace Imobisoft.Search.Services.Ai;

/// <summary>
/// A ceiling on how many provider calls the search pipeline may make per minute, counted across the
/// whole site rather than per visitor.
/// <para>
/// The visitor-facing text tools are rate limited per caller, which is the right shape for them: a
/// visitor has to press a button, so one caller pressing it repeatedly is the thing to stop. The
/// pipeline stages have no button. They run because a search ran, and a crawler walking a site's
/// search page with a different term each time is not one caller hitting a limit - it is a bill.
/// Cached answers make a popular search free, but a stream of terms nobody has searched before
/// cannot be served from a cache by definition.
/// </para>
/// <para>
/// So this is a site-wide fixed window. Rough on purpose, and in-process: it needs no schema and no
/// dependency, and on a load-balanced site each server enforces its own share. Once the window is
/// full, searches are served exactly as they would be with the AI switched off - which is the same
/// thing that happens when the provider is slow.
/// </para>
/// </summary>
internal static class AiCallBudget
{
    private static readonly ConcurrentDictionary<string, Window> Windows = new();

    /// <summary>
    /// Takes one call from this minute's allowance, or false when it is spent.
    /// </summary>
    /// <param name="stage">
    /// Which stage is asking. Counted separately so a busy answer card cannot starve the
    /// re-ranking, and so one stage being hammered is visible in what stops rather than everything
    /// degrading at once.
    /// </param>
    /// <param name="perMinute">The allowance. Zero or less refuses everything.</param>
    public static bool TryTake(string stage, int perMinute)
    {
        if (perMinute <= 0)
        {
            return false;
        }

        DateTime now = DateTime.UtcNow;

        Window window = Windows.GetOrAdd(stage, _ => new Window { StartedUtc = now });

        lock (window)
        {
            if (now - window.StartedUtc >= TimeSpan.FromMinutes(1))
            {
                window.StartedUtc = now;
                window.Count = 0;
            }

            if (window.Count >= perMinute)
            {
                return false;
            }

            window.Count++;

            return true;
        }
    }

    private sealed class Window
    {
        public DateTime StartedUtc { get; set; }

        public int Count { get; set; }
    }
}
