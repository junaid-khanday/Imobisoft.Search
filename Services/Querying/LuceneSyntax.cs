using System.Text;
using Imobisoft.Search.Models;

namespace Imobisoft.Search.Services.Querying;

/// <summary>
/// Builds Lucene query syntax by hand.
/// <para>
/// The package composes a query string rather than chaining a fluent query builder because the rule
/// set needs boosting, wildcards, fuzziness, grouping and negation combined freely - which reads far
/// more clearly as syntax than as nested builder calls, and keeps the exact query available to show
/// in the dashboard's diagnostics panel.
/// </para>
/// </summary>
internal static class LuceneSyntax
{
    /// <summary>Characters Lucene's parser treats as operators and that must be escaped in a term.</summary>
    private const string ReservedCharacters = "+-&|!(){}[]^\"~*?:\\/";

    /// <summary>Escapes a user-supplied term so it is matched literally.</summary>
    public static string EscapeTerm(string term)
    {
        if (string.IsNullOrEmpty(term))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(term.Length + 8);

        foreach (var c in term)
        {
            if (ReservedCharacters.IndexOf(c) >= 0)
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Field names are not user input, but a document type property alias could still contain a
    /// character the parser treats specially, so they get escaped too.
    /// </summary>
    public static string EscapeFieldName(string field) => EscapeTerm(field);

    /// <summary>Applies a match mode to an already-escaped term.</summary>
    public static string ApplyMatchMode(string escapedTerm, FieldMatchMode mode, float fuzziness)
        => mode switch
        {
            FieldMatchMode.Prefix => escapedTerm + "*",
            FieldMatchMode.Wildcard => "*" + escapedTerm + "*",
            FieldMatchMode.Fuzzy => escapedTerm + "~" + fuzziness.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture),
            _ => escapedTerm,
        };

    /// <summary>Wraps a clause in parentheses unless it is a single bare token.</summary>
    public static string Group(string clause)
        => string.IsNullOrWhiteSpace(clause) || IsAlreadyGrouped(clause) ? clause : "(" + clause + ")";

    /// <summary>Joins clauses with an operator, dropping blanks and skipping the join when only one survives.</summary>
    public static string Join(IEnumerable<string> clauses, string op)
    {
        var kept = clauses.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

        return kept.Count switch
        {
            0 => string.Empty,
            1 => kept[0],
            _ => string.Join($" {op} ", kept.Select(Group)),
        };
    }

    /// <summary><c>field:(value1 OR value2)</c>, with each value escaped.</summary>
    public static string FieldIn(string field, IEnumerable<string> values)
    {
        var kept = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(EscapeTerm).ToList();

        if (kept.Count == 0)
        {
            return string.Empty;
        }

        return $"{EscapeFieldName(field)}:({string.Join(" OR ", kept)})";
    }

    /// <summary>Applies a relevance multiplier to a clause. A boost of 1 is the default and is omitted.</summary>
    public static string Boost(string clause, float boost)
    {
        if (string.IsNullOrWhiteSpace(clause) || Math.Abs(boost - 1f) < 0.001f)
        {
            return clause;
        }

        return Group(clause) + "^" + boost.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Marks a clause as required.</summary>
    public static string Require(string clause)
        => string.IsNullOrWhiteSpace(clause) ? clause : "+" + Group(clause);

    /// <summary>Marks a clause as prohibited.</summary>
    public static string Prohibit(string clause)
        => string.IsNullOrWhiteSpace(clause) ? clause : "-" + Group(clause);

    private static bool IsAlreadyGrouped(string clause)
    {
        clause = clause.Trim();

        if (clause.Length < 2 || clause[0] != '(' || clause[^1] != ')')
        {
            return false;
        }

        // "(a) OR (b)" starts and ends with brackets but is not a single group.
        var depth = 0;

        for (var i = 0; i < clause.Length; i++)
        {
            if (clause[i] == '(' && (i == 0 || clause[i - 1] != '\\'))
            {
                depth++;
            }
            else if (clause[i] == ')' && clause[i - 1] != '\\')
            {
                depth--;

                if (depth == 0 && i < clause.Length - 1)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }
}
