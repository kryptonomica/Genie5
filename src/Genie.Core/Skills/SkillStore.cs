using System.Collections.Concurrent;

namespace Genie.Core.Skills;

/// <summary>
/// Live store of the character's skill ranks. Populated by
/// <c>GameStateEngine.ApplyComponent</c> when DR emits
/// <c>&lt;component id='exp &lt;SkillName&gt;'&gt;...rank...</c> events.
///
/// <para>
/// Used by the AutoMapper's weighted Dijkstra to filter exits the
/// character can't take (e.g. a wall requiring Climbing 50 when the
/// character has Climbing 30). Per the AutoMapper design doc, edges
/// with unmet requirements get infinite weight and are excluded from
/// pathfinding entirely.
/// </para>
///
/// <para>
/// Storage is <see cref="ConcurrentDictionary{TKey,TValue}"/> because
/// the parser writes from the event observable's thread while readers
/// (pathfinder, UI) read from various threads. Reads are lock-free.
/// </para>
/// </summary>
public sealed class SkillStore
{
    private readonly ConcurrentDictionary<string, int> _ranks
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Current rank for the named skill, 0 if unknown. Skill names are
    /// case-insensitive ("Climbing" and "climbing" return the same value).
    /// </summary>
    public int Rank(string skill)
        => _ranks.TryGetValue(skill, out var r) ? r : 0;

    /// <summary>
    /// True if we've seen any rank data for the skill at all. Useful for
    /// the pathfinder's "skill unknown, don't filter" fallback — better
    /// than treating unknown as rank=0 which would silently exclude
    /// every gated exit on a freshly-connected session.
    /// </summary>
    public bool IsKnown(string skill) => _ranks.ContainsKey(skill);

    /// <summary>Set the rank for a skill — called by the parser hook.</summary>
    public void SetRank(string skill, int rank)
    {
        if (string.IsNullOrWhiteSpace(skill)) return;
        _ranks[skill] = rank;
        Changed?.Invoke();
    }

    /// <summary>Snapshot of every known skill → rank, for UI surfaces.</summary>
    public IReadOnlyDictionary<string, int> Snapshot()
        => new Dictionary<string, int>(_ranks, StringComparer.OrdinalIgnoreCase);

    /// <summary>Drop all known ranks (e.g. on a fresh connect, so a previous
    /// character's skills don't bleed into the next). Clears in place — callers
    /// that hold this store by reference keep working.</summary>
    public void Clear()
    {
        if (_ranks.IsEmpty) return;
        _ranks.Clear();
        Changed?.Invoke();
    }

    /// <summary>Fires whenever a rank is updated. UI may want to refresh.</summary>
    public event Action? Changed;
}
