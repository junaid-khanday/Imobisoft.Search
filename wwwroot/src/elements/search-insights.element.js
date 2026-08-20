import { css, html, nothing, unsafeCSS } from '@umbraco-cms/backoffice/external/lit';
import { ImobisoftSearchElement } from './imobisoft-search-element.js';
import { sharedStyles } from '../ui/styles.js';
import { renderVolumeChart, volumeChartStyles } from '../ui/volume-chart.js';
import { field, numberInput, toggle } from '../ui/controls.js';

const PERIODS = [
	{ days: 7, label: '7 days' },
	{ days: 30, label: '30 days' },
	{ days: 90, label: '90 days' },
	{ days: 365, label: '12 months' },
];

/**
 * What visitors actually searched for.
 *
 * The zero-result table is the point of this dashboard: it is a list of things people expected the
 * site to have and could not find, which is the shortest route from search configuration to a real
 * content decision.
 */
export class SearchInsightsElement extends ImobisoftSearchElement {
	static styles = [
		sharedStyles,
		css`
			${unsafeCSS(volumeChartStyles)}

			.tiles {
				display: grid;
				grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
				gap: var(--uui-size-space-4);
			}

			.tile {
				border: 1px solid var(--uui-color-divider);
				border-radius: var(--uui-border-radius);
				padding: var(--uui-size-space-4);
			}

			.tile .value {
				font-size: var(--uui-type-h3-size, 1.75rem);
				font-weight: 700;
				line-height: 1.1;
			}

			.tile .label {
				color: var(--uui-color-text-alt);
				font-size: var(--uui-type-small-size);
				margin-top: var(--uui-size-space-1);
			}
		`,
	];

	static properties = {
		...ImobisoftSearchElement.properties,
		report: { type: Object, state: true },
		settings: { type: Object, state: true },
		days: { type: Number, state: true },
		hovered: { type: Number, state: true },
		savingSettings: { type: Boolean, state: true },
	};

	constructor() {
		super();
		this.report = undefined;
		this.settings = undefined;
		this.days = 30;
		this.hovered = -1;
		this.savingSettings = false;
	}

	async firstLoad() {
		this.loading = true;

		const [report, settings] = await Promise.all([
			this.call(this.api.getInsights(this.days)),
			this.call(this.api.getSettings()),
		]);

		this.report = report;
		this.settings = settings;
		this.loading = false;
	}

	render() {
		if (this.loading) {
			return html`<uui-loader></uui-loader>`;
		}

		return html`
			${this.error ? html`<div class="error-banner">${this.error}</div>` : nothing}
			${this.#renderOverview()} ${this.#renderTables()} ${this.#renderSettings()}
		`;
	}

	#renderOverview() {
		const summary = this.report?.summary;

		if (!summary) {
			return nothing;
		}

		return html`
			<uui-box>
				<div slot="headline" class="spread">
					<span>Search activity</span>
					<uui-button-group>
						${PERIODS.map(
							(period) => html`
								<uui-button
									compact
									look=${this.days === period.days ? 'primary' : 'secondary'}
									label=${period.label}
									@click=${() => this.#changePeriod(period.days)}>
									${period.label}
								</uui-button>
							`,
						)}
					</uui-button-group>
				</div>

				<div class="tiles" style="margin-bottom: var(--uui-size-space-5)">
					${this.#tile(summary.totalSearches.toLocaleString(), 'Searches')}
					${this.#tile(summary.uniqueTerms.toLocaleString(), 'Different terms')}
					${this.#tile(percent(summary.zeroResultRate), 'Found nothing')}
					${this.#tile(percent(summary.clickThroughRate), 'Opened a result')}
					${this.#tile(`${Math.round(summary.averageDurationMilliseconds)} ms`, 'Average time')}
					${this.#tile(summary.averageResultCount.toFixed(1), 'Average results')}
				</div>

				${renderVolumeChart(this.report.volume, {
					hovered: this.hovered,
					onHover: (index) => (this.hovered = index),
				})}
			</uui-box>
		`;
	}

	#tile(value, label) {
		return html`
			<div class="tile">
				<div class="value">${value}</div>
				<div class="label">${label}</div>
			</div>
		`;
	}

	#renderTables() {
		if (!this.report) {
			return nothing;
		}

		return html`
			<uui-box headline="Searches that found nothing" style="margin-top: var(--uui-size-space-5)">
				<p class="muted">
					Each of these is someone expecting the site to have something it did not return. Either the content
					is missing, or a rule is hiding it — the Test Search tab will tell you which.
				</p>
				${this.#renderTermTable(this.report.zeroResultTerms, { showZero: true })}
			</uui-box>

			<uui-box headline="Most searched" style="margin-top: var(--uui-size-space-5)">
				${this.#renderTermTable(this.report.topTerms, { showZero: true })}
			</uui-box>

			<uui-box headline="Found results, but nobody opened one" style="margin-top: var(--uui-size-space-5)">
				<p class="muted">
					These returned something and were ignored, which usually means the right answer is ranking too low.
					Weighting the document type or pinning a result will fix it.
				</p>
				${this.#renderTermTable(this.report.unclickedTerms, { showZero: false })}
			</uui-box>
		`;
	}

	#renderTermTable(rows, { showZero }) {
		if (!rows?.length) {
			return html`<div class="empty">Nothing recorded in this period.</div>`;
		}

		return html`
			<table>
				<thead>
					<tr>
						<th>Term</th>
						<th class="numeric">Searches</th>
						<th class="numeric">Average results</th>
						${showZero ? html`<th class="numeric">Found nothing</th>` : nothing}
						<th class="numeric">Opened</th>
						<th class="numeric">Average position</th>
						<th>Last searched</th>
					</tr>
				</thead>
				<tbody>
					${rows.map(
						(row) => html`
							<tr>
								<td><strong>${row.term}</strong></td>
								<td class="numeric">${row.searchCount.toLocaleString()}</td>
								<td class="numeric">${row.averageResultCount.toFixed(1)}</td>
								${showZero
									? html`<td class="numeric">
											${row.zeroResultCount > 0
												? html`<uui-tag color="danger">${row.zeroResultCount}</uui-tag>`
												: '—'}
										</td>`
									: nothing}
								<td class="numeric">${row.clickCount > 0 ? `${row.clickCount} (${percent(row.clickThroughRate)})` : '—'}</td>
								<td class="numeric">${row.averageClickPosition > 0 ? row.averageClickPosition.toFixed(1) : '—'}</td>
								<td class="muted">${new Date(row.lastSearched).toLocaleDateString()}</td>
							</tr>
						`,
					)}
				</tbody>
			</table>
		`;
	}

	#renderSettings() {
		const settings = this.settings;

		if (!settings) {
			return nothing;
		}

		const analytics = settings.analytics ?? {};
		const suggestions = settings.suggestions ?? {};

		return html`
			<uui-box headline="Settings" style="margin-top: var(--uui-size-space-5)">
				<p class="muted">These apply to the whole site, not to a single profile.</p>

				<div class="grid-2">
					<div>
						<h5>Recording</h5>
						${toggle('Record what visitors search for', analytics.enabled, (on) =>
							this.#patchAnalytics({ enabled: on }),
						)}
						${toggle(
							'Record which result they opened',
							analytics.trackClicks,
							(on) => this.#patchAnalytics({ trackClicks: on }),
							'Requires the site to call RecordClick when a visitor follows a result.',
						)}
						${toggle(
							'Only record searches that found nothing',
							analytics.recordZeroResultsOnly,
							(on) => this.#patchAnalytics({ recordZeroResultsOnly: on }),
							'Keeps the volume down on a busy site where the failures are the signal.',
						)}
						${field(
							'Keep records for (days)',
							'0 keeps them forever. A nightly job deletes anything older.',
							numberInput(analytics.retentionDays, (v) => this.#patchAnalytics({ retentionDays: v }), { min: 0 }),
						)}
						${field(
							'Ignore terms shorter than',
							'',
							numberInput(analytics.minimumTermLength, (v) => this.#patchAnalytics({ minimumTermLength: v }), {
								min: 1,
							}),
						)}
					</div>

					<div>
						<h5>Suggestions</h5>
						${toggle(
							'Offer spelling corrections and type-ahead',
							suggestions.enabled,
							(on) => this.#patchSuggestions({ enabled: on }),
							'Suggestions are built from what is actually in the index, so they never point at nothing.',
						)}
						${field(
							'Suggest a correction below this many results',
							'0 only suggests when nothing at all was found.',
							numberInput(
								suggestions.suggestBelowResultCount,
								(v) => this.#patchSuggestions({ suggestBelowResultCount: v }),
								{ min: 0 },
							),
						)}
						${field(
							'Correction fuzziness',
							'How close a word has to be before it is offered, between 0 and 1.',
							numberInput(suggestions.fuzziness, (v) => this.#patchSuggestions({ fuzziness: v }), {
								min: 0,
								max: 1,
								step: 0.05,
							}),
						)}
						${field(
							'Largest allowed typo',
							'Number of single-character edits between what was typed and the suggestion.',
							numberInput(
								suggestions.maximumEditDistance,
								(v) => this.#patchSuggestions({ maximumEditDistance: v }),
								{ min: 1, max: 6 },
							),
						)}
						${field(
							'Type-ahead suggestions to return',
							'',
							numberInput(suggestions.autocompleteSize, (v) => this.#patchSuggestions({ autocompleteSize: v }), {
								min: 1,
								max: 50,
							}),
						)}
					</div>
				</div>

				<div class="row" style="margin-top: var(--uui-size-space-4)">
					<uui-button
						look="primary"
						color="positive"
						label="Save settings"
						?disabled=${this.savingSettings}
						state=${this.savingSettings ? 'waiting' : nothing}
						@click=${this.#saveSettings}>
						Save settings
					</uui-button>
					<uui-button look="secondary" label="Delete old records now" @click=${this.#purge}>
						Delete old records now
					</uui-button>
				</div>
			</uui-box>
		`;
	}

	async #changePeriod(days) {
		this.days = days;
		this.hovered = -1;
		this.report = await this.call(this.api.getInsights(days));
	}

	#patchAnalytics(patch) {
		this.settings = { ...this.settings, analytics: { ...this.settings.analytics, ...patch } };
	}

	#patchSuggestions(patch) {
		this.settings = { ...this.settings, suggestions: { ...this.settings.suggestions, ...patch } };
	}

	async #saveSettings() {
		this.savingSettings = true;
		const saved = await this.call(this.api.saveSettings(this.settings));
		this.savingSettings = false;

		if (saved) {
			this.settings = saved;
			this.notify('positive', 'Search settings saved.');
		}
	}

	async #purge() {
		const removed = await this.call(this.api.purgeInsights());

		if (removed !== undefined) {
			this.notify('positive', `Deleted ${removed} recorded search${removed === 1 ? '' : 'es'}.`);
			this.report = await this.call(this.api.getInsights(this.days));
		}
	}
}

function percent(rate) {
	return `${Math.round((rate ?? 0) * 100)}%`;
}

export default SearchInsightsElement;

customElements.define('imobisoft-search-insights', SearchInsightsElement);
