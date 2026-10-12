namespace GameRadar.Core.Services;

/// <summary>
/// Turns Steam tags into TF-IDF vectors: each tag weighs its votes on the
/// game (TF) times its rarity among all candidates (IDF, inverse document
/// frequency). "Action" or "Multiplayer", on most games, say little (IDF
/// ~0.5); "Looter Shooter", on a few, says a lot (IDF ~3.6). Without it,
/// Warframe and Call of Duty looked alike (both "Shooter"); with it, their
/// similarity drops from 0.94 to 0.08 while Warframe and The Division 2 stay
/// close (0.67).
/// </summary>
public sealed class TagVectorizer
{
    private readonly Dictionary<int, double> _idf = new();
    private readonly double _unseenIdf;

    /// <param name="corpus">Tags of every candidate game: the rarity is measured among them.</param>
    public TagVectorizer(IEnumerable<IReadOnlyDictionary<int, double>?> corpus)
    {
        var documentFrequency = new Dictionary<int, int>();
        int documents = 0;
        foreach (var tags in corpus)
        {
            if (tags is not { Count: > 0 })
                continue;
            documents++;
            foreach (int tag in tags.Keys)
                documentFrequency[tag] = documentFrequency.GetValueOrDefault(tag) + 1;
        }

        // +1: a tag present everywhere still gets a finite (zero) weight.
        // Never negative: a tag on every game says nothing, not "the opposite".
        foreach (var (tag, count) in documentFrequency)
            _idf[tag] = Math.Max(0, Math.Log(documents / (count + 1.0)));
        // A tag no candidate has (only on a played game) is as rare as can be.
        _unseenIdf = Math.Log(Math.Max(documents, 1));
    }

    public double IdfOf(int tag) => _idf.GetValueOrDefault(tag, _unseenIdf);

    /// <summary>The TF-IDF vector of a game, or null when its tags are unknown.</summary>
    public Dictionary<int, double>? Vector(IReadOnlyDictionary<int, double>? tags)
    {
        if (tags is not { Count: > 0 })
            return null;

        var vector = new Dictionary<int, double>(tags.Count);
        foreach (var (tag, weight) in tags)
        {
            double value = weight * IdfOf(tag);
            if (value > 0)
                vector[tag] = value;
        }
        return vector.Count > 0 ? vector : null;
    }
}
