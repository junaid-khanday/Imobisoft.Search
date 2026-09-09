namespace Imobisoft.Search.Models;

/// <summary>One result the AI answer drew on, so a visitor can check a claim against its source.</summary>
public sealed class AiAnswerSource
{
    /// <summary>1-based position, matching the [1] markers in the answer text.</summary>
    public int Number { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Url { get; set; }
}

/// <summary>
/// A short answer to the visitor's question, written from the search results and nothing else.
/// <para>
/// Null on the response whenever the AI add-on is off, unconfigured, over its rate ceiling, or
/// simply failed - the result list is unaffected either way.
/// </para>
/// </summary>
public sealed class AiAnswer
{
    /// <summary>
    /// The answer text. Already HTML-encoded by the engine, with citation markers left as plain
    /// text, so a view can render it directly.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The results the answer was drawn from, in citation order.</summary>
    public IList<AiAnswerSource> Sources { get; set; } = new List<AiAnswerSource>();

    /// <summary>
    /// Set when the model judged the results did not contain an answer. The package still returns
    /// the text so a view can say so honestly rather than inventing one.
    /// </summary>
    public bool IsInconclusive { get; set; }

    /// <summary>Model that produced it, echoed so the backoffice can show what actually answered.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>True when this came from the cache rather than a fresh model call.</summary>
    public bool FromCache { get; set; }
}
