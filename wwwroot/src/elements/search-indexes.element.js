import { html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { ImobisoftSearchElement } from './imobisoft-search-element.js';
import { sharedStyles } from '../ui/styles.js';

/**
 * What the package can see. Every Examine index registered on the site, whether it is readable, how
 * much is in it, and which of its fields are worth searching or sorting on.
 *
 * This is the first place to look when a search returns nothing: an empty or unhealthy index
 * explains it immediately.
 */
export class SearchIndexesElement extends ImobisoftSearchElement {
	static styles = [sharedStyles];

	static properties = {
		...ImobisoftSearchElement.properties,
		indexes: { type: Array, state: true },
		expanded: { type: String, state: true },
		showSystemFields: { type: Boolean, state: true },
	};

	constructor() {
		super();
		this.indexes = [];
		this.expanded = '';
		this.showSystemFields = false;
	}

	async firstLoad() {
		this.loading = true;
		this.indexes = (await this.call(this.api.getIndexes(true))) ?? [];
		this.loading = false;
	}

	render() {
		if (this.loading) {
			return html`<uui-loader></uui-loader>`;
		}

		return html`
			${this.error ? html`<div class="error-banner">${this.error}</div>` : nothing}

			<uui-box>
				<div slot="headline" class="spread">
					<span>Examine indexes (${this.indexes.length})</span>
					<uui-button compact look="secondary" label="Refresh" @click=${this.firstLoad}>
						<uui-icon name="icon-refresh"></uui-icon> Refresh
					</uui-button>
				</div>

				${this.indexes.length === 0
					? html`<div class="empty">No Examine indexes are registered on this site.</div>`
					: html`
							<table>
								<thead>
									<tr>
										<th>Index</th>
										<th>Health</th>
										<th class="numeric">Documents</th>
										<th class="numeric">Fields</th>
										<th>Origin</th>
										<th></th>
									</tr>
								</thead>
								<tbody>
									${this.indexes.map((index) => this.#renderIndexRow(index))}
								</tbody>
							</table>
						`}
			</uui-box>
		`;
	}

	#renderIndexRow(index) {
		const isExpanded = this.expanded === index.name;

		return html`
			<tr>
				<td><strong>${index.name}</strong></td>
				<td>
					${index.isHealthy
						? html`<uui-tag color="positive">Healthy</uui-tag>`
						: html`
								<uui-tag color="danger">Unreadable</uui-tag>
								<div class="muted">${index.healthMessage}</div>
							`}
				</td>
				<td class="numeric">${index.documentCount.toLocaleString()}</td>
				<td class="numeric">${index.fieldCount.toLocaleString()}</td>
				<td class="muted">${index.isUmbracoIndex ? 'Umbraco' : 'This project'}</td>
				<td>
					<uui-button
						compact
						look="secondary"
						label=${isExpanded ? 'Hide fields' : 'Show fields'}
						@click=${() => (this.expanded = isExpanded ? '' : index.name)}>
						${isExpanded ? 'Hide fields' : 'Show fields'}
					</uui-button>
				</td>
			</tr>
			${isExpanded ? this.#renderFields(index) : nothing}
		`;
	}

	#renderFields(index) {
		const fields = (index.fields ?? []).filter((f) => this.showSystemFields || !f.isSystemField);

		return html`
			<tr>
				<td colspan="6" style="background: var(--uui-color-surface-alt)">
					<div class="spread" style="margin-bottom: var(--uui-size-space-3)">
						<span class="muted">
							Searchable fields carry free text. Sortable fields can be used as a sort level in a profile.
						</span>
						<uui-toggle
							label="Show Umbraco's internal fields"
							?checked=${this.showSystemFields}
							@change=${(e) => (this.showSystemFields = e.target.checked)}>
							Show internal fields
						</uui-toggle>
					</div>

					${fields.length === 0
						? html`<div class="empty">
								No fields found. An empty index reports nothing until content is published and indexed.
							</div>`
						: html`
								<table>
									<thead>
										<tr>
											<th>Field</th>
											<th>Type</th>
											<th>Searchable</th>
											<th>Sortable</th>
										</tr>
									</thead>
									<tbody>
										${fields.map(
											(f) => html`
												<tr>
													<td><code>${f.name}</code></td>
													<td class="muted">${f.type}</td>
													<td>${f.isSearchable ? html`<uui-tag color="positive">Yes</uui-tag>` : ''}</td>
													<td>${f.isSortable ? html`<uui-tag>Yes</uui-tag>` : ''}</td>
												</tr>
											`,
										)}
									</tbody>
								</table>
							`}
				</td>
			</tr>
		`;
	}
}

export default SearchIndexesElement;

customElements.define('imobisoft-search-indexes', SearchIndexesElement);
