# Imobisoft Search

A self-contained, rule-driven site search package for **Umbraco 17**.

Install the NuGet package and a **Search** section appears in the backoffice. Everything the search
does - which indexes it reads, which document types are in scope, field weighting, ranking, filters,
sort options, snippets, paging and "Load more" - is configured there. No files, no code and no
startup wiring are needed in the consuming project.

## Getting a search page on the front end

On first boot the package creates an **Imobi Search** document type and its template. Add a page of
that type anywhere in the Content tree, publish it, and that page is a working search page. The
template is a single line:

```cshtml
@await Component.InvokeAsync("ImobisoftSearchListing")
```

To wrap it in the site's own layout, set `Layout` in that template to the site's master view.

The package also serves a standalone page at `/imobisoft-search` so search works before any content
node exists. Turn it off with `Imobisoft:Search:EnableSearchPage`.

## Embedding search anywhere else

Drop the same line into any template:

```cshtml
@await Component.InvokeAsync("ImobisoftSearchListing")
```

Optional arguments override what the component reads from the query string:

```cshtml
@await Component.InvokeAsync("ImobisoftSearchListing", new { profileAlias = "knowledge-base" })
```

## Calling search from code

```csharp
public sealed class MyController : Controller
{
    private readonly IImobisoftSearchService _search;

    public MyController(IImobisoftSearchService search) => _search = search;

    public async Task<IActionResult> Index(string q)
    {
        SearchResponse response = await _search.SearchAsync(q);
        return View(response);
    }
}
```

## Styling and themes

Every piece of the listing is an overridable partial. Creating a file of the same name in the
consuming project wins over the package's copy, with no configuration:

- `Views/Partials/Search/styles.cshtml`
- `Views/Partials/Search/filters.cshtml`
- `Views/Partials/Search/searchbar.cshtml`
- `Views/Partials/Search/results.cshtml`
- `Views/Partials/Search/noresults.cshtml`
- `Views/Partials/Search/loadmore.cshtml`

A **theme** is a folder holding any subset of those files:

```
Views/Partials/Search/Themes/<name>/results.cshtml
```

Themes are discovered automatically and appear in the backoffice picker under
**Search &gt; your profile &gt; Results &gt; Search Page Theme**. Anything a theme does not define
falls back to the package's own partial, so a theme can restyle just the result cards and inherit
the rest. The selected theme applies to the front-end page and to the backoffice Test Search panel
alike.

The package ships one theme, **Modern** - a two-tone blue and black skin - alongside the built-in
look. Copy `Views/Partials/Search/Themes/modern/` into the site as a starting point for one of your
own.

## AI Search add-on

**It works out of the box — no key, no account, no cost.** The default answer engine is built into
the package: it reads the results the search already found and pulls out the sentences that answer
the question, with a citation each. Nothing leaves your server, there is nothing to sign up for, and
the answer box appears on the front-end search page from first install.

Adding your own model provider under **Search &gt; Settings &gt; AI Search Add-on** upgrades the same
box to a written answer and unlocks two further features. You choose the provider:

| Engine | Needs | Covers |
| --- | --- | --- |
| **Built-in** | Nothing | The free default. No key, no account, no network call. |
| **Claude** | An [Anthropic key](https://console.anthropic.com) | Claude, through the official SDK. |
| **OpenAI-compatible** | A base URL, your key, a model name | OpenAI, Azure OpenAI, Google Gemini, Groq, Mistral, DeepSeek, Together, OpenRouter — and a local Ollama or LM Studio, where the key can be left blank. |

The model name is free text, not a fixed list, so a model released after this package still works.
Base URLs are the API root — `/chat/completions` is appended for you:

```
https://api.openai.com/v1
https://generativelanguage.googleapis.com/v1beta/openai      (Gemini)
https://api.groq.com/openai/v1
https://openrouter.ai/api/v1
http://localhost:11434/v1                                     (Ollama)
```

| Feature | What it does | Built-in (free) | With a model provider |
| --- | --- | --- | --- |
| **AI answer** | A short, cited answer above the results — from what the search found, and nothing else. | Quotes the sentences that answer it | Writes the answer |
| **Query understanding** | Rewrites a question into the words your pages use before the query runs. *"how do I cancel my booking"* also finds *"Cancellations and refunds"*. | — | Optional, off by default |
| **AI re-ranking** | Reorders the first page by which result answers the question, not by word frequency. | — | Optional, off by default |

The two model-only features are off by default because each adds a model call to the path a visitor
is waiting on. Turn them on when the quality is worth the latency.

### What the built-in engine can and cannot do

It **extracts**, it does not write. It will surface an answer a page states, and it says so plainly
when the results do not contain one rather than quoting something that merely shares a word:

> *"These results do not directly answer that question. They cover Cancellations and refunds,
> Changing your dates and Contact the team."*

What it cannot do is reason across pages, or phrase something your content never says. It also has
no prompt to inject — indexed page content is only ever quoted, never interpreted — which makes it
a reasonable fit for sites that cannot send content to a third party at all.

**Everything fails open.** A missing key, an expired one, a timeout, a rate limit, a provider outage
or a refusal all cost the AI extras — never the results. Search falls back to exactly the keyword
behaviour it has without the add-on, so the page a visitor sees is never worse than it was.

**Cost controls** (model providers only — the built-in engine has nothing to bill or rate-limit):

- **Answer caching** (default 60 minutes) — the same question asked repeatedly is one model call, not one per visitor. This is the single biggest lever on spend.
- **Calls per minute** (default 60) — a ceiling across the whole site. Past it, search quietly serves keyword results, so a traffic spike or a crawler cannot run up an unbounded bill.
- **Model** — pick a cheaper one before turning features off; summarising results the search already found is well within a small model's reach.
- **Timeout** (default 12s) — how long a visitor waits before the results are served without AI.

The API key is **write-only**: it is stored server-side and replaced with a mask whenever settings
are read back, so it cannot be recovered through the backoffice once saved. Use **Test connection**
to verify a key — it sends one small request and reports what the provider said.

The answer box renders through `Views/Partials/Search/aianswer.cshtml`, overridable and themeable
like every other part.

## Configuration

Everything has a working default; a consuming site needs no configuration at all.

```json
{
  "Imobisoft": {
    "Search": {
      "Enabled": true,
      "EnableSearchPage": true,
      "CreateSearchPageDocumentType": true,
      "TakeOverSiteSearch": true,
      "ReadFiltersFromQueryString": true,
      "FilterQueryPrefix": "f_",
      "AddResponseHeader": true
    }
  }
}
```

| Setting | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Master switch. Off makes every search return nothing. |
| `EnableSearchPage` | `true` | Serves the built-in page at `/imobisoft-search`. |
| `CreateSearchPageDocumentType` | `true` | Creates the "Imobi Search" document type and template on first boot. |
| `TakeOverSiteSearch` | `true` | Puts the package behind the site's own `ISearchService`, if it has one. |
| `ReadFiltersFromQueryString` | `true` | Reads filter selections straight off the query string. |
| `FilterQueryPrefix` | `f_` | Prefix marking a filter: `?q=news&f_section=content`. |
| `AddResponseHeader` | `true` | Adds `X-Imobisoft-Search` naming the profile that answered. |

## Requirements

- Umbraco CMS 17.x
- .NET 10
