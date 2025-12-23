using System.Text;

namespace SayP.Application.Services;

/// <summary>
/// Provides fuzzy matching capabilities using Levenshtein distance algorithm
/// </summary>
public class FuzzyMatchingService
{
    /// <summary>
    /// Calculate similarity score between two strings (0.0 to 1.0)
    /// </summary>
    public double CalculateSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return 0.0;

        var distance = LevenshteinDistance(source.ToLower(), target.ToLower());
        var maxLength = Math.Max(source.Length, target.Length);
        
        return 1.0 - ((double)distance / maxLength);
    }

    /// <summary>
    /// Find best matches from a list of candidates
    /// </summary>
    public List<FuzzyMatch<T>> FindBestMatches<T>(
        string searchTerm, 
        IEnumerable<T> candidates, 
        Func<T, string> nameSelector,
        double threshold = 0.6,
        int maxResults = 5)
    {
        var matches = new List<FuzzyMatch<T>>();

        foreach (var candidate in candidates)
        {
            var candidateName = nameSelector(candidate);
            var similarity = CalculateSimilarity(searchTerm, candidateName);

            if (similarity >= threshold)
            {
                matches.Add(new FuzzyMatch<T>
                {
                    Item = candidate,
                    Name = candidateName,
                    Similarity = similarity
                });
            }
        }

        return matches
            .OrderByDescending(m => m.Similarity)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Check if there's an exact or very close match
    /// </summary>
    public FuzzyMatchResult CheckMatch(string searchTerm, string candidate, double exactThreshold = 0.95, double closeThreshold = 0.7)
    {
        var similarity = CalculateSimilarity(searchTerm, candidate);

        if (similarity >= exactThreshold)
            return FuzzyMatchResult.Exact;
        else if (similarity >= closeThreshold)
            return FuzzyMatchResult.Close;
        else
            return FuzzyMatchResult.NoMatch;
    }

    /// <summary>
    /// Calculate Levenshtein distance between two strings
    /// </summary>
    private int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return string.IsNullOrEmpty(target) ? 0 : target.Length;

        if (string.IsNullOrEmpty(target))
            return source.Length;

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var distance = new int[sourceLength + 1, targetLength + 1];

        // Initialize first column and row
        for (var i = 0; i <= sourceLength; i++)
            distance[i, 0] = i;

        for (var j = 0; j <= targetLength; j++)
            distance[0, j] = j;

        // Calculate distances
        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(
                        distance[i - 1, j] + 1,      // deletion
                        distance[i, j - 1] + 1),     // insertion
                    distance[i - 1, j - 1] + cost);  // substitution
            }
        }

        return distance[sourceLength, targetLength];
    }

    /// <summary>
    /// Normalize Turkish characters for better matching
    /// </summary>
    public string NormalizeTurkish(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var normalized = text.ToLower();
        
        // Turkish character mappings
        normalized = normalized
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c')
            .Replace('İ', 'i')
            .Replace('Ğ', 'g')
            .Replace('Ü', 'u')
            .Replace('Ş', 's')
            .Replace('Ö', 'o')
            .Replace('Ç', 'c');

        return normalized;
    }

    /// <summary>
    /// Calculate similarity with Turkish normalization
    /// </summary>
    public double CalculateTurkishSimilarity(string source, string target)
    {
        return CalculateSimilarity(
            NormalizeTurkish(source), 
            NormalizeTurkish(target)
        );
    }
}

public class FuzzyMatch<T>
{
    public T Item { get; set; } = default!;
    public string Name { get; set; } = string.Empty;
    public double Similarity { get; set; }
}

public enum FuzzyMatchResult
{
    Exact,
    Close,
    NoMatch
}
