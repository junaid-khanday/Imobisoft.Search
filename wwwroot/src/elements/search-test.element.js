import { html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { ImobisoftSearchElement } from './imobisoft-search-element.js';
import { sharedStyles } from '../ui/styles.js';
import { select } from '../ui/controls.js';

/**
 * Runs a real search against a saved profile and shows what came back, plus why.
 *
 * The diagnostics panel is the point of this dashboard: it reports the query that ran, the fields it
 * ran against and which rules changed the outcome, so tuning a profile is a matter of reading rather
 * than guessing.
 */
export class SearchTestElement extends ImobisoftSearchElement {
	static styles = [sharedStyles];

	static properties = {
		...ImobisoftSearchElement.properties,
		profiles: { type: Array, state: true },
		profileAlias: { type: String, state: true },
		term: { type: String, state: true },
		results: { type: Object, state: true },
		searching: { type: Boolean, state: true },
		showDiagnostics: { type: Boolean, state: true },
		suggestions: { type: Array, state: true },
	};

	/** Debounces type-ahead so a fast typist does not fire a request per keystroke. */
	#autocompleteTimer;

	constructor() {
		super();
		this.profiles = [];
		this.profileAlias = '';
		this.term = '';
		this.results = undefined;
		this.searching = false;
		this.showDiagnostics = true;
		this.suggestions = [];
	}

	async firstLoad() {
		this.loading = true;
		this.profiles = (await this.call(this.api.getProfiles())) ?? [];
		this.profileAlias = (this.profiles.find((p) => p.isDefault) ?? this.profiles[0])?.alias ?? '';
		this.loading = false;
	}

	render() {
		if (this.loading) {
			return html`<uui-loader></uui-loader>`;
		}

		return html`
			${this.error ? html`<div class="error-banner">${this.error}</div>` : nothing}

			<uui-box headline="Try a search">
				<div class="row">
					<uui-input
						class="grow"
						placeholder="Search for something…"
						.value=${this.term}
						@input=${(e) => this.#onTyping(e.target.value)}
						@keydown=${(e) => e.key === 'Enter' && this.#search()}></uui-input>

					${select(
						this.profileAlias,
						this.profiles.map((p) => ({
							name: p.isDefault ? `${p.name} (default)` : p.name,
							value: p.alias,
						})),
						(value) => (this.profileAlias = value),
						{ width: '260px' },
					)}

					<uui-button
						look="primary"
						label="Search"
						?disabled=${this.searching}
						state=${this.searching ? 'waiting' : nothing}
						@click=${this.#search}>
						Search
					</uui-button>
				</div>

				${this.suggestions.length
					? html`
							<div class="row-tight" style="margin-top: var(--uui-size-space-3)">
								<span class="muted">Type-ahead:</span>
								${this.suggestions.map(
									(suggestion) => html`
										<uui-button
											compact
											look="outline"
											label=${suggestion.text}
											@click=${() => this.#useTerm(suggestion.text)}>
											${suggestion.text}
										</uui-button>
									`,
								)}
							</div>
						`
					: nothing}
			</uui-box>

			${this.results ? this.#renderResults() : nothing}
		`;
	}

	/**
	 * Mirrors what a site's search box would do: ask for type-ahead as the visitor types, on a short
	 * debounce so a fast typist produces one request rather than a dozen.
	 */
	#onTyping(value) {
		this.term = value;

		clearTimeout(this.#autocompleteTimer);

		if (value.trim().length < 2) {
			this.suggestions = [];
			return;
		}

		this.#autocompleteTimer = setTimeout(async () => {
			const suggestions = await this.call(this.api.autocomplete(value.trim(), this.profileAlias, 8), {
				silent: true,
			});
			this.suggestions = suggestions ?? [];
		}, 200);
	}

	#useTerm(term) {
		this.term = term;
		this.suggestions = [];
		this.#search();
	}

	#renderResults() {
		const results = this.results;

		return html`
			<uui-box style="margin-top: var(--uui-size-space-5)">
				<div slot="headline" class="spread">
					<span>
						${results.totalResults.toLocaleString()} result${results.totalResults === 1 ? '' : 's'}
						${results.diagnostics ? html`<span class="muted"> in ${results.diagnostics.elapsedMilliseconds} ms</span>` : nothing}
					</span>
					<uui-button
						compact
						look="secondary"
						label=${this.showDiagnostics ? 'Hide why' : 'Show why'}
						@click=${() => (this.showDiagnostics = !this.showDiagnostics)}>
						${this.showDiagnostics ? 'Hide why' : 'Show why'}
					</uui-button>
				</div>

				${results.suggestion
					? html`
							<div class="row-tight" style="margin-bottom: var(--uui-size-space-3)">
								<span>Did you mean</span>
								<uui-button
									compact
									look="outline"
									label=${results.suggestion}
									@click=${() => this.#useTerm(results.suggestion)}>
									${results.suggestion}
								</uui-button>
								<span class="muted">— drawn from what is actually in the index.</span>
							</div>
						`
					: nothing}

				${results.results.length === 0
					? html`<div class="empty">Nothing matched. Open "Show why" to see which rule stopped it.</div>`
					: html`
							<table>
								<thead>
									<tr>
										<th style="width: 40px">#</th>
										<th>Result</th>
										<th>Type</th>
										<th>Index</th>
										<th class="numeric">Score</th>
									</tr>
								</thead>
								<tbody>
									${results.results.map(
										(item, index) => html`
											<tr>
												<td class="numeric muted">${(results.page - 1) * results.pageSize + index + 1}</td>
												<td>
													<div class="row-tight">
														<strong>${item.name || '(no name)'}</strong>
														${item.isBestBet ? html`<uui-tag color="positive">Pinned</uui-tag>` : nothing}
													</div>
													${item.url ? html`<div class="muted">${item.url}</div>` : nothing}
													${item.highlight ? html`<div class="muted">${this.#renderHighlight(item.highlight)}</div>` : nothing}
												</td>
												<td>${item.contentTypeAlias}</td>
												<td class="muted">${item.indexName}</td>
												<td class="numeric">${item.score?.toFixed(3)}</td>
											</tr>
										`,
									)}
								</tbody>
							</table>
						`}

				${this.#renderGroups(results)} ${this.#renderFacets(results)}
			</uui-box>

			${this.showDiagnostics && results.diagnostics ? this.#renderDiagnostics(results.diagnostics) : nothing}
		`;
	}

	/**
	 * Highlights arrive as a snippet whose surrounding text the server already HTML-encoded, so the
	 * only markup left is the configured highlight tag. Rendering it as text keeps this element
	 * safe without needing to trust indexed content.
	 */
	#renderHighlight(snippet) {
		return snippet.replace(/<\/?[^>]+>/g, '');
	}

	#renderGroups(results) {
		const groups = Object.entries(results.groups ?? {});

		if (!groups.length) {
			return nothing;
		}

		return html`
			<div style="margin-top: var(--uui-size-space-4)">
				<strong>By document type</strong>
				<div class="chips" style="margin-top: var(--uui-size-space-2)">
					${groups.map(([alias, count]) => html`<span class="chip">${alias} — ${count}</span>`)}
				</div>
			</div>
		`;
	}

	#renderFacets(results) {
		if (!results.facets?.length) {
			return nothing;
		}

		return html`
			<div style="margin-top: var(--uui-size-space-4)">
				<strong>Filters</strong>
				${results.facets.map(
					(facet) => html`
						<div style="margin-top: var(--uui-size-space-2)">
							<span class="muted">${facet.label}</span>
							<div class="chips">
								${facet.values.map((value) => html`<span class="chip">${value.label} — ${value.count}</span>`)}
							</div>
						</div>
					`,
				)}
			</div>
		`;
	}

	#renderDiagnostics(diagnostics) {
		return html`
			<uui-box headline="Why these results" style="margin-top: var(--uui-size-space-5)">
				<div class="grid-2">
					<div class="field">
						<label>Indexes searched</label>
						<div class="chips">
							${diagnostics.indexesSearched.map((name) => html`<span class="chip">${name}</span>`)}
						</div>
					</div>
					<div class="field">
						<label>Words searched for</label>
						<div class="chips">
							${diagnostics.resolvedTerms.map((term) => html`<span class="chip">${term}</span>`)}
						</div>
					</div>
				</div>

				<div class="field">
					<label>Fields searched (${diagnostics.fieldsSearched.length})</label>
					<div class="chips">
						${diagnostics.fieldsSearched.map((name) => html`<span class="chip">${name}</span>`)}
					</div>
				</div>

				<div class="field">
					<label>Query</label>
					${Object.entries(diagnostics.queries ?? {}).map(
						([index, query]) => html`
							<div style="margin-bottom: var(--uui-size-space-2)">
								<span class="muted">${index}</span>
								<div><code>${query}</code></div>
							</div>
						`,
					)}
				</div>

				<div class="field">
					<label>What the rules did</label>
					${diagnostics.notes?.length
						? html`<ul>
								${diagnostics.notes.map((note) => html`<li>${note}</li>`)}
							</ul>`
						: html`<span class="muted">No rules changed the outcome.</span>`}
				</div>
			</uui-box>
		`;
	}

	async #search() {
		if (!this.term.trim()) {
			return;
		}

		this.searching = true;

		this.results = await this.call(
			this.api.preview({
				term: this.term,
				profileAlias: this.profileAlias || undefined,
				page: 1,
			}),
		);

		this.searching = false;
	}
}

export default SearchTestElement;

customElements.define('imobisoft-search-test', SearchTestElement);
