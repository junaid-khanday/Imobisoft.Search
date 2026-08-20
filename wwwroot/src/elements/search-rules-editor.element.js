import { LitElement, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { sharedStyles } from '../ui/styles.js';
import {
	checkList,
	chipList,
	field,
	mapEditor,
	numberInput,
	repeater,
	select,
	tagInput,
	textInput,
	toggle,
} from '../ui/controls.js';

const MATCH_MODES = [
	{ value: 'exact', name: 'Exact — the word as typed' },
	{ value: 'prefix', name: 'Prefix — matches the start of a word' },
	{ value: 'fuzzy', name: 'Fuzzy — tolerates spelling mistakes' },
	{ value: 'wildcard', name: 'Wildcard — matches anywhere in a word' },
];

const INDEX_TYPES = [
	{ value: 'content', name: 'Content' },
	{ value: 'media', name: 'Media' },
	{ value: 'member', name: 'Members' },
];

const FACET_KINDS = [
	{ value: 'field', name: 'One choice per value' },
	{ value: 'dateRange', name: 'Date ranges' },
	{ value: 'numeric', name: 'Number ranges' },
];

/**
 * Plain-English names for the fields Umbraco writes into every document. Without these the field
 * picker reads as `__NodeTypeAlias` and `__IndexType`, which is not something an editor should have
 * to know.
 */
const FRIENDLY_FIELD_LABELS = {
	__NodeTypeAlias: 'Document or media type',
	__IndexType: 'Content, media or member',
	__NodeName: 'Page name',
	__Culture: 'Language',
	__Published: 'Published state',
	nodeName: 'Page name',
	updateDate: 'Last edited',
	createDate: 'Created',
	urlName: 'URL segment',
	level: 'Depth in the tree',
	creatorName: 'Created by',
	writerName: 'Last edited by',
	contentTypeAlias: 'Document type',
};

/** Date buckets that come with the "Last edited" and "Created" presets. */
const DATE_BUCKETS = [
	{ alias: 'week', label: 'Past week', from: 'now-7d', to: '' },
	{ alias: 'month', label: 'Past month', from: 'now-30d', to: '' },
	{ alias: 'year', label: 'Past year', from: 'now-1y', to: '' },
];

/** Starting buckets when someone picks the number-range style. */
const NUMBER_BUCKETS = [
	{ alias: 'under-10', label: 'Under 10', from: '', to: '10' },
	{ alias: '10-to-50', label: '10 to 50', from: '10', to: '50' },
	{ alias: '50-plus', label: '50 and over', from: '50', to: '' },
];

/** Units offered by the date-bucket builder, and the suffix each writes into the bound. */
const DATE_UNITS = [
	{ value: 'd', name: 'days' },
	{ value: 'w', name: 'weeks' },
	{ value: 'm', name: 'months' },
	{ value: 'y', name: 'years' },
];

/** Fields that hold a date whatever the index reports, because Umbraco always writes them as one. */
const KNOWN_DATE_FIELDS = new Set(['updatedate', 'createdate', 'publishdate', 'releasedate', 'expiredate']);

/** Fields that hold a number whatever the index reports. */
const KNOWN_NUMBER_FIELDS = new Set(['level', 'sortorder', 'id', 'parentid', 'nodeid', 'writerid', 'creatorid']);

/**
 * Ready-made filters. Adding one fills in the field, kind, label and buckets, so the common cases
 * are a single click and only an unusual filter needs the detail underneath.
 */
const FACET_PRESETS = [
	{ label: 'Document type', icon: 'icon-document', field: '__NodeTypeAlias', kind: 'field' },
	{ label: 'Content or media', icon: 'icon-documents', field: '__IndexType', kind: 'field' },
	{ label: 'Last edited', icon: 'icon-time', field: 'updateDate', kind: 'dateRange', ranges: DATE_BUCKETS },
	{ label: 'Created', icon: 'icon-calendar', field: 'createDate', kind: 'dateRange', ranges: DATE_BUCKETS },
	{ label: 'Language', icon: 'icon-globe', field: '__Culture', kind: 'field' },
];

/**
 * The rule editor. Renders one tab per rule group and emits the whole rule set back on every edit,
 * so the owning dashboard holds a single source of truth and decides when to save.
 *
 * Throughout, an empty selection means "no restriction". That is what lets a freshly installed
 * package search everything without any configuration at all, and it is why the index and document
 * type pickers pair with a "search everything" toggle rather than pre-ticking every box.
 */
export class SearchRulesEditorElement extends UmbElementMixin(LitElement) {
	static styles = [sharedStyles];

	static properties = {
		rules: { type: Object },
		catalog: { type: Object },
		activeTab: { type: String, state: true },
	};

	constructor() {
		super();
		this.rules = undefined;
		this.catalog = undefined;
		this.activeTab = 'sources';
	}

	render() {
		if (!this.rules) {
			return html`<uui-loader></uui-loader>`;
		}

		return html`
			<uui-tab-group>
				${this.#tab('sources', 'Sources', 'icon-filter')}
				${this.#tab('matching', 'Matching', 'icon-search')}
				${this.#tab('ranking', 'Ranking', 'icon-sort')}
				${this.#tab('results', 'Results', 'icon-list')}
				${this.#tab('facets', 'Facets', 'icon-categories')}
			</uui-tab-group>

			<div style="padding-top: var(--uui-size-space-5)">${this.#renderActiveTab()}</div>
		`;
	}

	#tab(alias, label, icon) {
		return html`
			<uui-tab
				label=${label}
				?active=${this.activeTab === alias}
				@click=${() => (this.activeTab = alias)}>
				<uui-icon slot="icon" name=${icon}></uui-icon>
				${label}
			</uui-tab>
		`;
	}

	#renderActiveTab() {
		switch (this.activeTab) {
			case 'matching':
				return this.#renderMatching();
			case 'ranking':
				return this.#renderRanking();
			case 'results':
				return this.#renderResults();
			case 'facets':
				return this.#renderFacets();
			default:
				return this.#renderSources();
		}
	}

	// ---------------------------------------------------------------- sources

	#renderSources() {
		const sources = this.rules.sources;
		const indexes = this.catalog?.indexes ?? [];
		const searchAllIndexes = (sources.indexes ?? []).length === 0;

		return html`
			<uui-box headline="Where to search">
				${field(
					'Indexes',
					'Every Examine index registered on this site, including any your own project adds.',
					html`
						<div class="stack">
							${toggle(
								'Search every index',
								searchAllIndexes,
								(on) => this.#set('sources', 'indexes', on ? [] : indexes.map((i) => i.name)),
								'Leave this on and newly added indexes are picked up automatically.',
							)}
							${searchAllIndexes
								? html`<div class="muted">
										Searching all ${indexes.length} index${indexes.length === 1 ? '' : 'es'}.
									</div>`
								: checkList(
										indexes.map((i) => ({
											value: i.name,
											name: i.name,
											hint: `${i.documentCount.toLocaleString()} documents${i.isHealthy ? '' : ' — unhealthy'}`,
										})),
										sources.indexes ?? [],
										(value) => this.#set('sources', 'indexes', value),
										{ emptyMessage: 'No Examine indexes were found on this site.' },
									)}
						</div>
					`,
				)}
				${field(
					'Kinds of document',
					'Leave all unticked to search content, media and members alike.',
					checkList(INDEX_TYPES, sources.indexTypes ?? [], (value) => this.#set('sources', 'indexTypes', value)),
				)}
			</uui-box>

			<uui-box headline="Document types" style="margin-top: var(--uui-size-space-5)">
				<div class="grid-2">
					${field(
						'Only these document types',
						'Empty means every document type is searchable.',
						checkList(this.#contentTypeOptions(), sources.includeContentTypes ?? [], (value) =>
							this.#set('sources', 'includeContentTypes', value),
						),
					)}
					${field(
						'Never these document types',
						'Applied after the include list.',
						checkList(this.#contentTypeOptions(), sources.excludeContentTypes ?? [], (value) =>
							this.#set('sources', 'excludeContentTypes', value),
						),
					)}
					${field(
						'Only these media types',
						'Empty means every media type is searchable.',
						checkList(this.#mediaTypeOptions(), sources.includeMediaTypes ?? [], (value) =>
							this.#set('sources', 'includeMediaTypes', value),
						),
					)}
					${field(
						'Never these media types',
						'Applied after the include list.',
						checkList(this.#mediaTypeOptions(), sources.excludeMediaTypes ?? [], (value) =>
							this.#set('sources', 'excludeMediaTypes', value),
						),
					)}
				</div>
			</uui-box>

			<uui-box headline="Which part of the tree" style="margin-top: var(--uui-size-space-5)">
				${field(
					'Search only under these nodes',
					'Empty means the whole site. Picking a node includes everything beneath it.',
					this.#nodePicker(sources.rootNodeKeys ?? [], (value) => this.#set('sources', 'rootNodeKeys', value)),
				)}
				${field(
					'Never return these nodes',
					'',
					html`
						<div class="stack">
							${this.#nodePicker(sources.excludedNodeKeys ?? [], (value) =>
								this.#set('sources', 'excludedNodeKeys', value),
							)}
							${toggle(
								'Also exclude everything beneath them',
								sources.excludeDescendantsOfExcludedNodes,
								(on) => this.#set('sources', 'excludeDescendantsOfExcludedNodes', on),
							)}
						</div>
					`,
				)}
			</uui-box>

			<uui-box headline="Visibility and language" style="margin-top: var(--uui-size-space-5)">
				${field(
					'Languages',
					'Empty means every language. Umbraco stores variant content in culture-specific fields, so this changes which fields are searched.',
					checkList(
						(this.catalog?.languages ?? []).map((l) => ({
							value: l.isoCode,
							name: l.name,
							hint: l.isDefault ? 'default' : undefined,
						})),
						sources.cultures ?? [],
						(value) => this.#set('sources', 'cultures', value),
						{ emptyMessage: 'This site has one language.' },
					),
				)}
				<div class="stack">
					${toggle(
						'Hide pages marked "hide in navigation"',
						sources.respectNaviHide,
						(on) => this.#set('sources', 'respectNaviHide', on),
						'Honours the umbracoNaviHide property.',
					)}
					${toggle(
						'Hide member-protected pages',
						sources.excludeProtected,
						(on) => this.#set('sources', 'excludeProtected', on),
						'Keeps content behind public access out of results.',
					)}
					${toggle(
						'Published content only',
						sources.publishedOnly,
						(on) => this.#set('sources', 'publishedOnly', on),
						'Matters when the InternalIndex is in the index list, since it also holds drafts.',
					)}
				</div>
			</uui-box>
		`;
	}

	// --------------------------------------------------------------- matching

	#renderMatching() {
		const matching = this.rules.matching;
		const fields = matching.fields ?? [];
		const fieldOptions = this.#searchableFieldOptions();

		return html`
			<uui-box headline="Which fields carry the search">
				<p class="muted">
					A weight above 1 makes a match in that field count for more. Weighting a title field at 10 means a
					title match outranks a body match. Leave the list empty and every searchable field in the index is
					used, with the node name weighted up.
				</p>

				${fields.length === 0
					? html`
							<div class="empty">
								No fields configured — searching every searchable field. Add one to take control.
							</div>
						`
					: nothing}

				${repeater(
					fields,
					(rule, index) => html`
						<span class="grow">
							${select(rule.name, fieldOptions, (value) => this.#updateFieldRule(index, { name: value }), {
								width: '100%',
							})}
						</span>
						<uui-input
							type="number"
							step="0.5"
							min="0"
							style="width: 90px"
							title="Weight"
							.value=${rule.boost}
							@change=${(e) => this.#updateFieldRule(index, { boost: Number(e.target.value) || 1 })}></uui-input>
						${select(rule.matchMode, MATCH_MODES, (value) => this.#updateFieldRule(index, { matchMode: value }), {
							width: '260px',
						})}
						<uui-toggle
							label="Enabled"
							?checked=${rule.enabled !== false}
							@change=${(e) => this.#updateFieldRule(index, { enabled: e.target.checked })}></uui-toggle>
					`,
					{
						addLabel: 'Add field',
						emptyMessage: '',
						onAdd: () =>
							this.#set('matching', 'fields', [
								...fields,
								{ name: fieldOptions[0]?.value ?? 'nodeName', boost: 1, matchMode: 'prefix', enabled: true },
							]),
						onRemove: (index) =>
							this.#set(
								'matching',
								'fields',
								fields.filter((_, i) => i !== index),
							),
					},
				)}

				<div style="margin-top: var(--uui-size-space-4)">
					<uui-button
						look="secondary"
						label="Add every searchable field"
						@click=${() =>
							this.#set(
								'matching',
								'fields',
								fieldOptions.map((o) => ({
									name: o.value,
									boost: o.value === 'nodeName' || o.value === '__NodeName' ? 10 : 1,
									matchMode: 'prefix',
									enabled: true,
								})),
							)}>
						Add every searchable field
					</uui-button>
					<uui-button look="secondary" label="Clear" @click=${() => this.#set('matching', 'fields', [])}>
						Clear
					</uui-button>
				</div>
			</uui-box>

			<uui-box headline="How forgiving matching is" style="margin-top: var(--uui-size-space-5)">
				<div class="grid-2">
					${field(
						'Combine fields with',
						'OR casts the widest net; AND requires the term in every field.',
						select(
							matching.defaultOperator,
							[
								{ value: 'or', name: 'OR — a match in any field counts' },
								{ value: 'and', name: 'AND — must match in every field' },
							],
							(value) => this.#set('matching', 'defaultOperator', value),
						),
					)}
					${field(
						'Fuzziness',
						'How close a word has to be for fuzzy matching. 1 is exact, 0.6 is very forgiving.',
						numberInput(matching.fuzziness, (v) => this.#set('matching', 'fuzziness', v), {
							min: 0,
							max: 1,
							step: 0.05,
						}),
					)}
					${field(
						'Minimum term length',
						'Shorter queries return nothing, which stops single letters scanning the index.',
						numberInput(matching.minimumQueryLength, (v) => this.#set('matching', 'minimumQueryLength', v), {
							min: 1,
						}),
					)}
					${field(
						'Minimum score',
						'Drops weak matches. 0 keeps everything.',
						numberInput(matching.minimumScore, (v) => this.#set('matching', 'minimumScore', v), {
							min: 0,
							step: 0.01,
						}),
					)}
				</div>

				${toggle(
					'Every word must match',
					matching.allTermsMustMatch,
					(on) => this.#set('matching', 'allTermsMustMatch', on),
					'With this off, a two-word search returns documents matching either word.',
				)}
			</uui-box>

			<uui-box headline="Rewriting the query" style="margin-top: var(--uui-size-space-5)">
				${field(
					'Stop words',
					'Removed from the query before it runs. A search made up entirely of stop words returns nothing.',
					tagInput(matching.stopWords ?? [], (value) => this.#set('matching', 'stopWords', value), {
						placeholder: 'e.g. the, and, of',
					}),
				)}
				${field(
					'Synonyms',
					'Searching for the word on the left also searches for the alternatives on the right.',
					mapEditor(
						matching.synonyms ?? {},
						(key, value, setValue) =>
							html`<div class="grow">${tagInput(value ?? [], setValue, { placeholder: 'Alternative' })}</div>`,
						{
							keyPlaceholder: 'Word',
							addLabel: 'Add synonym',
							onChange: (value) => this.#set('matching', 'synonyms', value),
						},
					),
				)}
			</uui-box>
		`;
	}

	// ---------------------------------------------------------------- ranking

	#renderRanking() {
		const ranking = this.rules.ranking;
		const sortBy = ranking.sortBy ?? [];
		const sortFieldOptions = [
			{ value: 'score', name: 'Relevance' },
			...this.#sortableFieldOptions(),
		];

		return html`
			<uui-box headline="Order">
				<p class="muted">
					Leave this empty to order by relevance. Add levels to sort by a field instead — later levels break
					ties in earlier ones. Pinned results always stay on top whatever the order.
				</p>

				${repeater(
					sortBy,
					(rule, index) => html`
						<span class="grow">
							${select(rule.field, sortFieldOptions, (value) => this.#updateSortRule(index, { field: value }), {
								width: '100%',
							})}
						</span>
						${select(
							rule.direction,
							[
								{ value: 'descending', name: 'Descending' },
								{ value: 'ascending', name: 'Ascending' },
							],
							(value) => this.#updateSortRule(index, { direction: value }),
							{ width: '200px' },
						)}
					`,
					{
						addLabel: 'Add sort level',
						emptyMessage: 'Ordered by relevance.',
						onAdd: () =>
							this.#set('ranking', 'sortBy', [...sortBy, { field: 'score', direction: 'descending' }]),
						onRemove: (index) =>
							this.#set(
								'ranking',
								'sortBy',
								sortBy.filter((_, i) => i !== index),
							),
					},
				)}
			</uui-box>

			<uui-box headline="Weighting by document type" style="margin-top: var(--uui-size-space-5)">
				${field(
					'',
					'Multiplies the relevance of every result of that type. 2 doubles it, 0.5 halves it.',
					mapEditor(
						ranking.contentTypeBoosts ?? {},
						(key, value, setValue) =>
							html`
								<uui-input
									type="number"
									step="0.1"
									min="0"
									style="width: 110px"
									.value=${value}
									@change=${(e) => setValue(Number(e.target.value) || 1)}></uui-input>
							`,
						{
							keyPlaceholder: 'Document type alias',
							addLabel: 'Add weighting',
							onChange: (value) => this.#set('ranking', 'contentTypeBoosts', value),
						},
					),
				)}
			</uui-box>

			<uui-box headline="Editorial overrides" style="margin-top: var(--uui-size-space-5)">
				${field(
					'Pinned results',
					'When someone searches for any of these words, these pages go straight to the top regardless of score.',
					repeater(
						ranking.bestBets ?? [],
						(bet, index) => html`
							<div class="grow stack">
								${tagInput(
									bet.terms ?? [],
									(value) => this.#updateBestBet(index, { terms: value }),
									{ placeholder: 'Search word that triggers this' },
								)}
								${this.#nodePicker(bet.nodeKeys ?? [], (value) => this.#updateBestBet(index, { nodeKeys: value }))}
							</div>
						`,
						{
							addLabel: 'Add pinned result',
							emptyMessage: 'No pinned results.',
							onAdd: () =>
								this.#set('ranking', 'bestBets', [...(ranking.bestBets ?? []), { terms: [], nodeKeys: [] }]),
							onRemove: (index) =>
								this.#set(
									'ranking',
									'bestBets',
									(ranking.bestBets ?? []).filter((_, i) => i !== index),
								),
						},
					),
				)}
				${field(
					'Blocked words',
					'Searching for one of these returns nothing at all.',
					tagInput(ranking.blockedTerms ?? [], (value) => this.#set('ranking', 'blockedTerms', value)),
				)}
			</uui-box>

			<uui-box headline="Favour recent content" style="margin-top: var(--uui-size-space-5)">
				${toggle(
					'Lift recently edited content',
					ranking.recency?.enabled,
					(on) => this.#setNested('ranking', 'recency', { enabled: on }),
				)}
				${ranking.recency?.enabled
					? html`
							<div class="grid-2" style="margin-top: var(--uui-size-space-4)">
								${field(
									'Date field',
									'',
									select(
										ranking.recency.field,
										[
											{ value: 'updateDate', name: 'Last edited' },
											{ value: 'createDate', name: 'Created' },
										],
										(value) => this.#setNested('ranking', 'recency', { field: value }),
									),
								)}
								${field(
									'Half-life in days',
									'How old content has to be before the lift has halved.',
									numberInput(
										ranking.recency.halfLifeDays,
										(v) => this.#setNested('ranking', 'recency', { halfLifeDays: v }),
										{ min: 1 },
									),
								)}
								${field(
									'Strength',
									'How much brand new content can gain. 0.5 means up to 50% more relevance.',
									numberInput(
										ranking.recency.weight,
										(v) => this.#setNested('ranking', 'recency', { weight: v }),
										{ min: 0, step: 0.1 },
									),
								)}
							</div>
						`
					: nothing}
			</uui-box>
		`;
	}

	// ---------------------------------------------------------------- results

	#renderResults() {
		const results = this.rules.results;
		const highlight = results.highlight ?? {};

		return html`
			<uui-box headline="Paging">
				<div class="grid-2">
					${field(
						'Results per page',
						'',
						numberInput(results.pageSize, (v) => this.#set('results', 'pageSize', v), { min: 1 }),
					)}
					${field(
						'Maximum results considered',
						'How many matches are pulled from the index before weighting, pinning and de-duplication run. Higher is more accurate and slower.',
						numberInput(results.maxResults, (v) => this.#set('results', 'maxResults', v), {
							min: 1,
							max: 5000,
						}),
					)}
				</div>
			</uui-box>

			<uui-box headline="What each result carries" style="margin-top: var(--uui-size-space-5)">
				${field(
					'Fields to return',
					'Empty returns a useful default set. Naming fields keeps the response small on content-heavy sites.',
					tagInput(results.returnFields ?? [], (value) => this.#set('results', 'returnFields', value), {
						placeholder: 'Field name',
					}),
				)}
				${field(
					'Collapse duplicates on',
					'Results sharing a value for this field collapse to the highest scoring one. Leave empty to keep everything.',
					textInput(results.deduplicateByField, (v) => this.#set('results', 'deduplicateByField', v), {
						placeholder: 'e.g. title',
					}),
				)}
				${toggle('Include a breakdown by document type', results.groupByContentType, (on) =>
					this.#set('results', 'groupByContentType', on),
				)}
			</uui-box>

			<uui-box headline="Matching text under each result" style="margin-top: var(--uui-size-space-5)">
				<p class="muted">
					Show visitors where their word appears, rather than a generic summary. The two switches are
					independent: you can show the sentence without colouring the word, or colour the word inside a
					summary you already show.
				</p>

				<div class="stack">
					${toggle(
						'Show the text the word was found in',
						highlight.enabled,
						(on) => this.#setNested('results', 'highlight', { enabled: on }),
					)}
					${toggle(
						'Mark the searched word inside that text',
						highlight.highlightMatches !== false,
						(on) => this.#setNested('results', 'highlight', { highlightMatches: on }),
					)}
				</div>

				${highlight.enabled
					? html`
							<div class="grid-2" style="margin-top: var(--uui-size-space-4)">
								${field(
									'How much text to show',
									'',
									select(
										highlight.mode ?? 'sentence',
										[
											{ value: 'sentence', name: 'The whole sentence the word is in' },
											{ value: 'characters', name: 'A fixed amount of text around the word' },
										],
										(value) => this.#setNested('results', 'highlight', { mode: value }),
									),
								)}
								${(highlight.mode ?? 'sentence') === 'sentence'
									? field(
											'Sentences either side',
											'0 shows only the sentence containing the word.',
											numberInput(
												highlight.sentenceContext ?? 0,
												(v) => this.#setNested('results', 'highlight', { sentenceContext: v }),
												{ min: 0, max: 3 },
											),
										)
									: nothing}
								${field(
									'Where to look for the text',
									'Empty uses the first searched field the page actually filled in.',
									textInput(
										highlight.field,
										(v) => this.#setNested('results', 'highlight', { field: v }),
										{ placeholder: 'e.g. bodyText' },
									),
								)}
								${field(
									(highlight.mode ?? 'sentence') === 'sentence' ? 'Longest allowed' : 'How much text',
									(highlight.mode ?? 'sentence') === 'sentence'
										? 'A sentence longer than this falls back to a fixed amount of text.'
										: 'Characters either side of the word.',
									numberInput(
										highlight.snippetLength,
										(v) => this.#setNested('results', 'highlight', { snippetLength: v }),
										{ min: 40 },
									),
								)}
								${highlight.highlightMatches !== false
									? field(
											'Wrap the word with',
											'Opening and closing tag. Style them with CSS on your site.',
											html`
												<div class="row-tight">
													${textInput(highlight.startTag, (v) =>
														this.#setNested('results', 'highlight', { startTag: v }),
													)}
													${textInput(highlight.endTag, (v) =>
														this.#setNested('results', 'highlight', { endTag: v }),
													)}
												</div>
											`,
										)
									: nothing}
							</div>
						`
					: nothing}
			</uui-box>
		`;
	}

	// ----------------------------------------------------------------- facets

	#renderFacets() {
		const facets = this.rules.results.facets ?? [];

		return html`
			<uui-box headline="Filters shown alongside results">
				<p class="muted">
					Pick what visitors can narrow their results by. Each filter comes back with live counts, and the
					counts for one filter ignore its own selection — so choosing an option never empties the ones next
					to it.
				</p>

				<div class="field">
					<label>Add a filter</label>
					<div class="row-tight">
						${FACET_PRESETS.map(
							(preset) => html`
								<uui-button
									look="outline"
									label=${preset.label}
									?disabled=${facets.some((f) => f.field === preset.field)}
									@click=${() => this.#addFacet(preset)}>
									<uui-icon name=${preset.icon}></uui-icon> ${preset.label}
								</uui-button>
							`,
						)}
						<uui-button look="outline" label="Something else" @click=${() => this.#addFacet(null)}>
							<uui-icon name="icon-add"></uui-icon> Something else
						</uui-button>
					</div>
				</div>

				${facets.length === 0
					? html`<div class="empty">No filters yet. Pick one above.</div>`
					: facets.map((facet, index) => this.#renderFacet(facet, index))}
			</uui-box>
		`;
	}

	#renderFacet(facet, index) {
		const kind = facet.kind ?? 'field';
		const isRange = kind === 'dateRange' || kind === 'numeric';
		const mismatch = this.#fieldMismatch(facet.field, kind);

		return html`
			<div
				class="stack"
				style="border: 1px solid var(--uui-color-divider); border-radius: var(--uui-border-radius);
				       padding: var(--uui-size-space-4); margin-bottom: var(--uui-size-space-3)">
				<div class="row">
					<div class="grow">
						<label class="muted">Heading visitors see</label>
						<uui-input
							style="width: 100%"
							placeholder="e.g. Type of page"
							.value=${facet.label ?? ''}
							@change=${(e) =>
								this.#updateFacet(index, {
									label: e.target.value,
									// The web-address name is derived unless someone has set it deliberately.
									alias: facet.alias || slugify(e.target.value),
								})}></uui-input>
					</div>

					<div>
						<label class="muted">Style</label>
						${select(kind, FACET_KINDS, (value) => this.#changeFacetKind(index, facet, value), {
							width: '200px',
						})}
					</div>

					<div class="grow">
						<label class="muted">Group results by</label>
						${select(
							facet.field,
							this.#facetFieldOptions(facet.field, kind),
							(value) =>
								this.#updateFacet(index, {
									field: value,
									label: facet.label || friendlyLabel(value),
									alias: facet.alias || slugify(friendlyLabel(value)),
								}),
							{ width: '100%' },
						)}
					</div>

					<uui-button
						compact
						look="secondary"
						color="danger"
						label="Remove"
						title="Remove this filter"
						@click=${() =>
							this.#set(
								'results',
								'facets',
								(this.rules.results.facets ?? []).filter((_, i) => i !== index),
							)}>
						<uui-icon name="icon-trash"></uui-icon>
					</uui-button>
				</div>

				${mismatch
					? html`
							<div class="error-banner">
								<strong>${friendlyLabel(facet.field)}</strong> holds ${mismatch}, so this filter will find
								nothing and show as empty. Pick a ${kind === 'dateRange' ? 'date' : 'number'} field above,
								or change the style back to <em>One choice per value</em>.
							</div>
						`
					: nothing}

				${isRange ? this.#renderFacetRanges(facet, index) : nothing}

				<details>
					<summary class="muted">More options</summary>
					<div class="grid-2" style="margin-top: var(--uui-size-space-3)">
						${field(
							'Most choices to show',
							'Highest counts first.',
							numberInput(facet.maxValues ?? 20, (v) => this.#updateFacet(index, { maxValues: v }), {
								min: 1,
							}),
						)}
						${field(
							'Name used in the web address',
							'Filled in for you. Only change it if the site already expects a particular name.',
							textInput(facet.alias, (v) => this.#updateFacet(index, { alias: v })),
						)}
						<div class="field">
							${toggle(
								'Hide choices that match nothing',
								facet.hideEmpty !== false,
								(on) => this.#updateFacet(index, { hideEmpty: on }),
								'Turn this off while setting a filter up — an empty choice tells you it matched nothing.',
							)}
						</div>
					</div>
				</details>
			</div>
		`;
	}

	/**
	 * Switching style also moves the field and the buckets, because a date filter pointed at a text
	 * field silently finds nothing. Better to land on something that works and let it be changed.
	 */
	#changeFacetKind(index, facet, kind) {
		const patch = { kind };

		if (this.#fieldMismatch(facet.field, kind)) {
			const options = this.#facetFieldOptions('', kind);
			patch.field = options[0]?.value ?? facet.field;
			patch.label = friendlyLabel(patch.field);
			patch.alias = slugify(patch.label);
		}

		if (kind === 'dateRange' && (facet.ranges ?? []).length === 0) {
			patch.ranges = DATE_BUCKETS.map((r) => ({ ...r }));
		}

		if (kind === 'numeric' && (facet.ranges ?? []).length === 0) {
			patch.ranges = NUMBER_BUCKETS.map((r) => ({ ...r }));
		}

		this.#updateFacet(index, patch);
	}

	/**
	 * Describes what a field actually holds when it cannot serve the chosen style, or null when the
	 * pairing is fine.
	 */
	#fieldMismatch(fieldName, kind) {
		if (!fieldName || kind === 'field') {
			return null;
		}

		const classification = classifyField(fieldName, this.#fieldType(fieldName));

		if (kind === 'dateRange' && classification !== 'date') {
			return classification === 'number' ? 'numbers, not dates' : 'text, not dates';
		}

		if (kind === 'numeric' && classification !== 'number') {
			return classification === 'date' ? 'dates, not numbers' : 'text, not numbers';
		}

		return null;
	}

	#fieldType(fieldName) {
		return this.#fieldsInScope().find((f) => f.name.toLowerCase() === fieldName?.toLowerCase())?.type ?? '';
	}

	#renderFacetRanges(facet, facetIndex) {
		const ranges = facet.ranges ?? [];
		const isDate = facet.kind === 'dateRange';

		return html`
			<div style="padding-left: var(--uui-size-space-4); border-left: 2px solid var(--uui-color-divider)">
				<div class="spread">
					<span class="muted">
						${isDate
							? 'Each choice covers a span of time counted back from today, so "Past week" keeps meaning the past week.'
							: 'From is included, To is not. Leave either blank for no limit.'}
					</span>
					${ranges.length === 0
						? html`
								<uui-button
									compact
									look="outline"
									label="Use the usual ones"
									@click=${() =>
										this.#updateFacet(facetIndex, {
											ranges: (isDate ? DATE_BUCKETS : NUMBER_BUCKETS).map((r) => ({ ...r })),
										})}>
									Use the usual ones
								</uui-button>
							`
						: nothing}
				</div>

				${repeater(
					ranges,
					(range, rangeIndex) =>
						isDate
							? this.#renderDateBucket(facetIndex, rangeIndex, range)
							: this.#renderNumberBucket(facetIndex, rangeIndex, range),
					{
						addLabel: 'Add a choice',
						emptyMessage: 'No choices yet.',
						onAdd: () =>
							this.#updateFacet(facetIndex, {
								ranges: [
									...ranges,
									isDate
										? { alias: '', label: '', from: 'now-7d', to: '' }
										: { alias: '', label: '', from: '', to: '' },
								],
							}),
						onRemove: (rangeIndex) =>
							this.#updateFacet(facetIndex, { ranges: ranges.filter((_, i) => i !== rangeIndex) }),
					},
				)}
			</div>
		`;
	}

	/**
	 * A date bucket as "the past N days", rather than as two free-text bounds. The relative
	 * expression the engine wants is composed here, so it is never mistyped.
	 */
	#renderDateBucket(facetIndex, rangeIndex, range) {
		const relative = parseRelative(range.from);

		return html`
			<uui-input
				class="grow"
				placeholder="Label, e.g. Past week"
				.value=${range.label ?? ''}
				@change=${(e) =>
					this.#updateFacetRange(facetIndex, rangeIndex, {
						label: e.target.value,
						alias: range.alias || slugify(e.target.value),
					})}></uui-input>

			<span class="muted">the past</span>
			<uui-input
				type="number"
				min="1"
				style="width: 90px"
				.value=${relative.amount}
				@change=${(e) =>
					this.#updateFacetRange(facetIndex, rangeIndex, {
						from: buildRelative(Number(e.target.value) || 1, relative.unit),
						to: '',
					})}></uui-input>
			${select(
				relative.unit,
				DATE_UNITS,
				(unit) =>
					this.#updateFacetRange(facetIndex, rangeIndex, {
						from: buildRelative(relative.amount, unit),
						to: '',
					}),
				{ width: '120px' },
			)}
		`;
	}

	#renderNumberBucket(facetIndex, rangeIndex, range) {
		return html`
			<uui-input
				class="grow"
				placeholder="Label, e.g. Under 10"
				.value=${range.label ?? ''}
				@change=${(e) =>
					this.#updateFacetRange(facetIndex, rangeIndex, {
						label: e.target.value,
						alias: range.alias || slugify(e.target.value),
					})}></uui-input>
			<uui-input
				type="number"
				style="width: 130px"
				placeholder="From"
				.value=${range.from ?? ''}
				@change=${(e) => this.#updateFacetRange(facetIndex, rangeIndex, { from: e.target.value })}></uui-input>
			<span class="muted">up to</span>
			<uui-input
				type="number"
				style="width: 130px"
				placeholder="To"
				.value=${range.to ?? ''}
				@change=${(e) => this.#updateFacetRange(facetIndex, rangeIndex, { to: e.target.value })}></uui-input>
		`;
	}

	/**
	 * The fields a filter can group by: Umbraco's own fields named in plain English and listed
	 * first, then every other field discovered in the indexes this profile searches.
	 *
	 * Narrowed to what the chosen style can actually read, so a date filter never offers a text
	 * field. That pairing is the one way to build a filter that saves happily and then shows nothing.
	 */
	#facetFieldOptions(selected, kind = 'field') {
		const wanted = kind === 'dateRange' ? 'date' : kind === 'numeric' ? 'number' : null;
		const seen = new Set();
		const options = [];

		const add = (value, name, type) => {
			if (!value || seen.has(value.toLowerCase())) return;
			if (wanted && classifyField(value, type) !== wanted) return;

			seen.add(value.toLowerCase());
			options.push({ name, value });
		};

		for (const [name, label] of Object.entries(FRIENDLY_FIELD_LABELS)) {
			add(name, label, this.#fieldType(name));
		}

		for (const f of this.#fieldsInScope()) {
			add(f.name, f.name, f.type);
		}

		// A field the profile already points at but that no index currently reports - keep it
		// selectable rather than silently switching the filter to something else.
		if (selected && !seen.has(selected.toLowerCase())) {
			options.push({ name: `${selected} (not found in the current indexes)`, value: selected });
		}

		return options;
	}

	#addFacet(preset) {
		const facets = this.rules.results.facets ?? [];

		const facet = preset
			? {
					alias: slugify(preset.label),
					label: preset.label,
					field: preset.field,
					kind: preset.kind,
					maxValues: 20,
					hideEmpty: true,
					ranges: (preset.ranges ?? []).map((r) => ({ ...r })),
				}
			: {
					alias: '',
					label: '',
					field: this.#facetFieldOptions('')[0]?.value ?? '__NodeTypeAlias',
					kind: 'field',
					maxValues: 20,
					hideEmpty: true,
					ranges: [],
				};

		this.#set('results', 'facets', [...facets, facet]);
	}

	// ------------------------------------------------------------- node picker

	/**
	 * Picks content nodes through Umbraco's own document picker. The picker is imported on demand so
	 * that a change to its module path in a future Umbraco release degrades to manual GUID entry
	 * rather than breaking the whole dashboard at load time.
	 */
	#nodePicker(keys, onChange) {
		return html`
			<div class="stack">
				${chipList(
					keys,
					(index) => onChange(keys.filter((_, i) => i !== index)),
					(key) => html`<code>${key}</code>`,
				)}
				<div class="row-tight">
					<uui-button look="secondary" label="Choose content" @click=${() => this.#openDocumentPicker(keys, onChange)}>
						<uui-icon name="icon-document"></uui-icon> Choose content
					</uui-button>
					<uui-input
						placeholder="…or paste a node GUID and press Enter"
						style="min-width: 320px"
						@keydown=${(e) => {
							if (e.key !== 'Enter') return;
							e.preventDefault();

							const value = e.target.value.trim();
							if (value && !keys.includes(value)) {
								onChange([...keys, value]);
							}
							e.target.value = '';
						}}></uui-input>
				</div>
			</div>
		`;
	}

	async #openDocumentPicker(keys, onChange) {
		try {
			const [{ UMB_DOCUMENT_PICKER_MODAL }, { umbOpenModal }] = await Promise.all([
				import('@umbraco-cms/backoffice/document'),
				import('@umbraco-cms/backoffice/modal'),
			]);

			const result = await umbOpenModal(this, UMB_DOCUMENT_PICKER_MODAL, {
				data: { multiple: true },
				value: { selection: keys },
			});

			if (result?.selection) {
				onChange(result.selection.filter(Boolean));
			}
		} catch (error) {
			// A cancelled modal rejects too, so this is not necessarily a failure worth shouting about.
			if (error) {
				this.dispatchEvent(
					new CustomEvent('picker-unavailable', {
						bubbles: true,
						composed: true,
						detail: 'The content picker could not be opened. Paste a node GUID instead.',
					}),
				);
			}
		}
	}

	// ------------------------------------------------------------- option data

	#contentTypeOptions() {
		return (this.catalog?.contentTypes ?? [])
			.filter((t) => !t.isElement)
			.map((t) => ({ value: t.alias, name: t.name, hint: t.alias }));
	}

	#mediaTypeOptions() {
		return (this.catalog?.mediaTypes ?? []).map((t) => ({ value: t.alias, name: t.name, hint: t.alias }));
	}

	/** Fields from the indexes this profile actually searches, de-duplicated across them. */
	#fieldsInScope() {
		const selected = this.rules?.sources?.indexes ?? [];
		const indexes = (this.catalog?.indexes ?? []).filter(
			(i) => selected.length === 0 || selected.some((s) => s.toLowerCase() === i.name.toLowerCase()),
		);

		const byName = new Map();

		for (const index of indexes) {
			for (const f of index.fields ?? []) {
				if (!byName.has(f.name)) {
					byName.set(f.name, f);
				}
			}
		}

		return [...byName.values()];
	}

	#searchableFieldOptions() {
		return this.#fieldsInScope()
			.filter((f) => f.isSearchable)
			.map((f) => ({ value: f.name, name: f.name }));
	}

	#sortableFieldOptions() {
		return this.#fieldsInScope()
			.filter((f) => f.isSortable)
			.map((f) => ({ value: f.name, name: f.name }));
	}

	// ----------------------------------------------------------------- updates

	#set(section, key, value) {
		this.#emit({
			...this.rules,
			[section]: { ...this.rules[section], [key]: value },
		});
	}

	#setNested(section, key, patch) {
		this.#set(section, key, { ...(this.rules[section][key] ?? {}), ...patch });
	}

	#updateFieldRule(index, patch) {
		const fields = (this.rules.matching.fields ?? []).map((f, i) => (i === index ? merge(f, patch) : f));
		this.#set('matching', 'fields', fields);
	}

	#updateSortRule(index, patch) {
		const sortBy = (this.rules.ranking.sortBy ?? []).map((s, i) => (i === index ? merge(s, patch) : s));
		this.#set('ranking', 'sortBy', sortBy);
	}

	#updateBestBet(index, patch) {
		const bestBets = (this.rules.ranking.bestBets ?? []).map((b, i) => (i === index ? merge(b, patch) : b));
		this.#set('ranking', 'bestBets', bestBets);
	}

	#updateFacet(index, patch) {
		const facets = (this.rules.results.facets ?? []).map((f, i) => (i === index ? merge(f, patch) : f));
		this.#set('results', 'facets', facets);
	}

	#updateFacetRange(facetIndex, rangeIndex, patch) {
		const facet = (this.rules.results.facets ?? [])[facetIndex];
		if (!facet) return;

		const ranges = (facet.ranges ?? []).map((r, i) => (i === rangeIndex ? { ...r, ...patch } : r));
		this.#updateFacet(facetIndex, { ranges });
	}

	#emit(rules) {
		this.rules = rules;
		this.dispatchEvent(new CustomEvent('rules-change', { detail: rules, bubbles: true, composed: true }));
	}
}

/**
 * Merges a patch, ignoring any key whose value is undefined.
 *
 * A control that reports `undefined` must leave the existing value alone. Writing it through means
 * the key disappears from the JSON entirely, the server falls back to the type's default, and the
 * editor's choice is lost on save without any error.
 */
function merge(target, patch) {
	const clean = {};

	for (const [key, value] of Object.entries(patch)) {
		if (value !== undefined) {
			clean[key] = value;
		}
	}

	return { ...target, ...clean };
}

/**
 * Works out whether a field holds dates, numbers or text, so a filter is only ever offered fields
 * its style can actually read.
 */
function classifyField(name, type) {
	const lower = (name ?? '').toLowerCase();

	if (KNOWN_DATE_FIELDS.has(lower)) return 'date';
	if (KNOWN_NUMBER_FIELDS.has(lower)) return 'number';

	const t = (type ?? '').toLowerCase();

	if (t.startsWith('date')) return 'date';
	if (['int', 'long', 'float', 'double', 'number'].includes(t)) return 'number';

	return 'text';
}

/** Reads "now-7d" back into the amount and unit the builder shows. */
function parseRelative(bound) {
	const match = /^now-(\d+)([dwmyh])$/i.exec((bound ?? '').trim());

	return match ? { amount: Number(match[1]), unit: match[2].toLowerCase() } : { amount: 7, unit: 'd' };
}

/** Composes the relative expression the engine parses, so it can never be mistyped. */
function buildRelative(amount, unit) {
	return `now-${Math.max(1, amount || 1)}${unit || 'd'}`;
}

/** The plain-English name for a field, falling back to the field's own name. */
function friendlyLabel(field) {
	return FRIENDLY_FIELD_LABELS[field] ?? field ?? '';
}

/**
 * Turns a heading into something safe for a web address. Filter selections travel in the query
 * string, so the name has to survive being put there.
 */
function slugify(value) {
	return (value ?? '')
		.toLowerCase()
		.trim()
		.replace(/[^a-z0-9]+/g, '-')
		.replace(/^-+|-+$/g, '')
		.slice(0, 50);
}

customElements.define('imobisoft-search-rules-editor', SearchRulesEditorElement);
