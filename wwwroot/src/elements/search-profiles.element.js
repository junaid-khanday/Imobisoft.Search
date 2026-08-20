import { html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { ImobisoftSearchElement } from './imobisoft-search-element.js';
import { sharedStyles } from '../ui/styles.js';
import './search-rules-editor.element.js';

/**
 * The main dashboard: a list of search profiles on the left, the rule editor for the selected one on
 * the right.
 *
 * The draft is held here rather than in the editor so that a profile can be edited across all five
 * rule tabs and saved once, and so leaving a profile with unsaved changes can be caught.
 */
export class SearchProfilesElement extends ImobisoftSearchElement {
	static styles = [sharedStyles];

	static properties = {
		...ImobisoftSearchElement.properties,
		profiles: { type: Array, state: true },
		catalog: { type: Object, state: true },
		draft: { type: Object, state: true },
		saving: { type: Boolean, state: true },
	};

	/** JSON of the profile as last loaded, used to tell whether there is anything to save. */
	#pristine = '';

	constructor() {
		super();
		this.profiles = [];
		this.catalog = undefined;
		this.draft = undefined;
		this.saving = false;
	}

	async firstLoad() {
		this.loading = true;

		const [profiles, catalog] = await Promise.all([
			this.call(this.api.getProfiles()),
			this.call(this.api.getCatalog()),
		]);

		this.profiles = profiles ?? [];
		this.catalog = catalog;
		this.loading = false;

		const defaultProfile = this.profiles.find((p) => p.isDefault) ?? this.profiles[0];

		if (defaultProfile) {
			this.#select(defaultProfile);
		}
	}

	get isDirty() {
		return !!this.draft && JSON.stringify(this.draft) !== this.#pristine;
	}

	render() {
		if (this.loading) {
			return html`<uui-loader></uui-loader>`;
		}

		return html`
			${this.error ? html`<div class="error-banner">${this.error}</div>` : nothing}

			<div class="split">
				<uui-box headline="Profiles">
					${this.#renderProfileList()}
					<div style="margin-top: var(--uui-size-space-4)">
						<uui-button look="primary" label="New profile" @click=${this.#createProfile}>
							<uui-icon name="icon-add"></uui-icon> New profile
						</uui-button>
					</div>
				</uui-box>

				${this.draft ? this.#renderEditor() : html`<uui-box><div class="empty">Select a profile.</div></uui-box>`}
			</div>
		`;
	}

	#renderProfileList() {
		if (!this.profiles.length) {
			return html`<div class="empty">No profiles yet.</div>`;
		}

		return html`
			<uui-ref-list>
				${this.profiles.map(
					(profile) => html`
						<uui-ref-node
							name=${profile.name}
							detail=${profile.alias}
							?selected=${this.draft?.key === profile.key}
							@open=${() => this.#select(profile)}
							@click=${() => this.#select(profile)}>
							<uui-icon slot="icon" name="icon-search"></uui-icon>
							<div slot="tag" class="row-tight">
								${profile.isDefault ? html`<uui-tag color="positive">Default</uui-tag>` : nothing}
								${profile.enabled === false ? html`<uui-tag color="danger">Disabled</uui-tag>` : nothing}
							</div>
						</uui-ref-node>
					`,
				)}
			</uui-ref-list>
		`;
	}

	#renderEditor() {
		const draft = this.draft;
		const isNew = !this.profiles.some((p) => p.key === draft.key);

		return html`
			<div class="stack">
				<uui-box>
					<div slot="headline" class="row-tight">
						${draft.name || 'Untitled profile'}
						${draft.isDefault ? html`<uui-tag color="positive">Default</uui-tag>` : nothing}
						${this.isDirty ? html`<uui-tag color="warning">Unsaved changes</uui-tag>` : nothing}
					</div>

					<div class="grid-2">
						<div class="field">
							<label>Name</label>
							<uui-input
								.value=${draft.name ?? ''}
								placeholder="Site search"
								@change=${(e) => this.#patch({ name: e.target.value })}></uui-input>
						</div>
						<div class="field">
							<label>Alias</label>
							<span class="muted">
								How site code asks for this profile. Letters, digits, dashes and underscores.
							</span>
							<uui-input
								.value=${draft.alias ?? ''}
								placeholder="site-search"
								?readonly=${!isNew && draft.alias === 'default'}
								@change=${(e) => this.#patch({ alias: e.target.value })}></uui-input>
						</div>
					</div>

					<uui-toggle
						label="Enabled"
						?checked=${draft.enabled !== false}
						@change=${(e) => this.#patch({ enabled: e.target.checked })}>
						Enabled
						<div class="muted">A disabled profile returns no results rather than failing.</div>
					</uui-toggle>

					<div class="row" style="margin-top: var(--uui-size-space-5)">
						<uui-button
							look="primary"
							color="positive"
							label="Save"
							?disabled=${!this.isDirty || this.saving}
							state=${this.saving ? 'waiting' : nothing}
							@click=${this.#save}>
							Save
						</uui-button>
						<uui-button
							look="secondary"
							label="Discard changes"
							?disabled=${!this.isDirty}
							@click=${() => this.#select(this.profiles.find((p) => p.key === draft.key) ?? draft)}>
							Discard
						</uui-button>
						${!isNew && !draft.isDefault
							? html`
									<uui-button look="secondary" label="Make default" @click=${this.#setDefault}>
										Make default
									</uui-button>
								`
							: nothing}
						${!isNew && this.profiles.length > 1
							? html`
									<uui-button look="secondary" color="danger" label="Delete" @click=${this.#delete}>
										Delete
									</uui-button>
								`
							: nothing}
					</div>
				</uui-box>

				<imobisoft-search-rules-editor
					.rules=${draft.rules}
					.catalog=${this.catalog}
					@rules-change=${(e) => this.#patch({ rules: e.detail })}
					@picker-unavailable=${(e) => this.notify('warning', e.detail)}></imobisoft-search-rules-editor>
			</div>
		`;
	}

	#select(profile) {
		// Clone so edits never mutate the list behind the editor's back.
		this.draft = structuredClone(profile);
		this.#pristine = JSON.stringify(this.draft);
	}

	#patch(patch) {
		this.draft = { ...this.draft, ...patch };
	}

	#createProfile() {
		this.#select({
			key: crypto.randomUUID(),
			alias: '',
			name: '',
			isDefault: false,
			enabled: true,
			rules: this.#emptyRuleSet(),
		});

		// A new profile has nothing saved yet, so it must read as dirty straight away.
		this.#pristine = '';
	}

	async #save() {
		const draft = this.draft;

		if (!draft.alias?.trim()) {
			this.notify('warning', 'Give the profile an alias before saving.');
			return;
		}

		this.saving = true;

		const isNew = !this.profiles.some((p) => p.key === draft.key);
		const saved = await this.call(
			isNew ? this.api.createProfile(draft) : this.api.updateProfile(draft.key, draft),
		);

		this.saving = false;

		if (!saved) {
			return;
		}

		this.profiles = await this.call(this.api.getProfiles()).then((p) => p ?? this.profiles);
		this.#select(this.profiles.find((p) => p.key === saved.key) ?? saved);
		this.notify('positive', `Saved "${saved.name}".`);
	}

	async #setDefault() {
		const saved = await this.call(this.api.setDefaultProfile(this.draft.key));
		if (!saved) return;

		this.profiles = (await this.call(this.api.getProfiles())) ?? this.profiles;
		this.#select(this.profiles.find((p) => p.key === saved.key) ?? saved);
		this.notify('positive', `"${saved.name}" is now the default profile.`);
	}

	async #delete() {
		const name = this.draft.name || this.draft.alias;

		if (!confirm(`Delete the search profile "${name}"? This cannot be undone.`)) {
			return;
		}

		const { error } = await this.api.deleteProfile(this.draft.key);

		if (error) {
			this.notify('warning', error);
			return;
		}

		this.profiles = (await this.call(this.api.getProfiles())) ?? [];
		const next = this.profiles.find((p) => p.isDefault) ?? this.profiles[0];

		this.draft = undefined;
		if (next) this.#select(next);

		this.notify('positive', `Deleted "${name}".`);
	}

	/** Mirrors the server-side defaults, so a new profile starts life searching everything. */
	#emptyRuleSet() {
		return {
			sources: {
				indexes: [],
				indexTypes: [],
				includeContentTypes: [],
				excludeContentTypes: [],
				includeMediaTypes: [],
				excludeMediaTypes: [],
				rootNodeKeys: [],
				excludedNodeKeys: [],
				excludeDescendantsOfExcludedNodes: true,
				cultures: [],
				respectNaviHide: true,
				excludeProtected: true,
				publishedOnly: true,
			},
			matching: {
				fields: [],
				defaultOperator: 'or',
				fuzziness: 0.8,
				minimumQueryLength: 2,
				minimumScore: 0,
				stopWords: [],
				synonyms: {},
				allTermsMustMatch: false,
			},
			ranking: {
				sortBy: [],
				contentTypeBoosts: {},
				bestBets: [],
				blockedTerms: [],
				recency: { enabled: false, field: 'updateDate', halfLifeDays: 90, weight: 0.5 },
			},
			results: {
				pageSize: 10,
				maxResults: 500,
				returnFields: [],
				groupByContentType: false,
				deduplicateByField: '',
				highlight: { enabled: false, field: '', snippetLength: 200, startTag: '<mark>', endTag: '</mark>' },
				facets: [],
			},
		};
	}
}

export default SearchProfilesElement;

customElements.define('imobisoft-search-profiles', SearchProfilesElement);
