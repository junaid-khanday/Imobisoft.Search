import { html, nothing } from '@umbraco-cms/backoffice/external/lit';

/**
 * Small render helpers shared by the rule editors.
 *
 * These are plain functions returning templates rather than custom elements: the rules editor needs
 * dozens of inputs and every one of them writes straight back into the profile object, so a
 * component boundary per input would add ceremony without adding anything.
 */

/** A labelled form row with optional help text. */
export function field(label, description, control) {
	return html`
		<div class="field">
			<label>${label}</label>
			${description ? html`<span class="muted">${description}</span>` : nothing}
			${control}
		</div>
	`;
}

export function textInput(value, onChange, { placeholder = '', type = 'text' } = {}) {
	return html`
		<uui-input
			type=${type}
			.value=${value ?? ''}
			placeholder=${placeholder}
			@change=${(e) => onChange(e.target.value)}></uui-input>
	`;
}

export function numberInput(value, onChange, { min, max, step = 1 } = {}) {
	return html`
		<uui-input
			type="number"
			.value=${value ?? 0}
			min=${min ?? nothing}
			max=${max ?? nothing}
			step=${step}
			@change=${(e) => onChange(toNumber(e.target.value, value))}></uui-input>
	`;
}

export function toggle(label, checked, onChange, description) {
	return html`
		<uui-toggle
			label=${label}
			?checked=${!!checked}
			@change=${(e) => onChange(e.target.checked)}>
			${label}${description ? html`<div class="muted">${description}</div>` : nothing}
		</uui-toggle>
	`;
}

/**
 * A dropdown.
 *
 * Deliberately a native `select` rather than `uui-select`. The native `change` event from inside
 * `uui-select`'s shadow DOM is retargeted to the host before the host has updated its own `value`,
 * so `event.target.value` reads as `undefined` and the chosen value is silently lost on save. A
 * native element has no such gap.
 *
 * @param {Array<{value: string, name: string}>} options
 */
export function select(value, options, onChange, { width } = {}) {
	return html`
		<select
			class="imob-select"
			style=${width ? `width: ${width}` : nothing}
			@change=${(e) => onChange(e.target.value)}>
			${options.map(
				(option) => html`
					<option value=${option.value} ?selected=${option.value === value}>${option.name}</option>
				`,
			)}
		</select>
	`;
}

/**
 * A scrollable list of checkboxes. Used wherever "empty means everything" applies, so the caller
 * decides what an empty selection means rather than this control.
 * @param {Array<{value: string, name: string, hint?: string}>} options
 * @param {string[]} selected
 */
export function checkList(options, selected, onChange, { emptyMessage = 'Nothing to choose from.' } = {}) {
	if (!options.length) {
		return html`<div class="empty">${emptyMessage}</div>`;
	}

	const isSelected = (value) => selected.some((s) => equals(s, value));

	return html`
		<div class="check-list">
			${options.map(
				(option) => html`
					<uui-checkbox
						label=${option.name}
						?checked=${isSelected(option.value)}
						@change=${(e) => onChange(toggleValue(selected, option.value, e.target.checked))}>
						${option.name}
						${option.hint ? html`<span class="muted"> — ${option.hint}</span>` : nothing}
					</uui-checkbox>
				`,
			)}
		</div>
	`;
}

/**
 * Free-text chips. The value is committed on Enter or blur so a half-typed word is never saved.
 */
export function tagInput(values, onChange, { placeholder = 'Type and press Enter' } = {}) {
	const commit = (input) => {
		const raw = input.value?.trim();
		if (!raw) return;

		// Accept a pasted comma separated list as well as one word at a time.
		const additions = raw
			.split(',')
			.map((v) => v.trim())
			.filter((v) => v && !values.some((existing) => equals(existing, v)));

		if (additions.length) {
			onChange([...values, ...additions]);
		}

		input.value = '';
	};

	return html`
		<div class="stack">
			<div class="chips">
				${values.length
					? values.map(
							(value, index) => html`
								<span class="chip">
									${value}
									<button
										type="button"
										title="Remove"
										@click=${() => onChange(values.filter((_, i) => i !== index))}>
										×
									</button>
								</span>
							`,
						)
					: html`<span class="muted">None</span>`}
			</div>
			<uui-input
				placeholder=${placeholder}
				@keydown=${(e) => {
					if (e.key === 'Enter') {
						e.preventDefault();
						commit(e.target);
					}
				}}
				@blur=${(e) => commit(e.target)}></uui-input>
		</div>
	`;
}

/** Chips with no text entry, for values added through a picker. */
export function chipList(values, onRemove, renderLabel = (v) => v) {
	if (!values.length) {
		return html`<span class="muted">None</span>`;
	}

	return html`
		<div class="chips">
			${values.map(
				(value, index) => html`
					<span class="chip">
						${renderLabel(value)}
						<button type="button" title="Remove" @click=${() => onRemove(index)}>×</button>
					</span>
				`,
			)}
		</div>
	`;
}

/** A list of rows with add and remove, used for field weights, sort levels, best bets and facets. */
export function repeater(items, renderRow, { onAdd, onRemove, addLabel = 'Add', emptyMessage = 'None yet.' }) {
	return html`
		<div class="stack">
			${items.length
				? items.map(
						(item, index) => html`
							<div class="repeater-row">
								${renderRow(item, index)}
								<uui-button
									compact
									look="secondary"
									color="danger"
									label="Remove"
									title="Remove"
									@click=${() => onRemove(index)}>
									<uui-icon name="icon-trash"></uui-icon>
								</uui-button>
							</div>
						`,
					)
				: html`<div class="empty">${emptyMessage}</div>`}
			<div>
				<uui-button look="secondary" label=${addLabel} @click=${onAdd}>
					<uui-icon name="icon-add"></uui-icon> ${addLabel}
				</uui-button>
			</div>
		</div>
	`;
}

/** Key/value rows, used for content type boosts and synonyms. */
export function mapEditor(map, renderValue, { onChange, keyPlaceholder = 'Key', addLabel = 'Add' }) {
	const entries = Object.entries(map ?? {});

	const rename = (oldKey, newKey) => {
		if (!newKey || newKey === oldKey) return;

		const next = {};
		for (const [k, v] of entries) {
			next[k === oldKey ? newKey : k] = v;
		}
		onChange(next);
	};

	return html`
		<div class="stack">
			${entries.length
				? entries.map(
						([key, value]) => html`
							<div class="repeater-row">
								<uui-input
									class="grow"
									placeholder=${keyPlaceholder}
									.value=${key}
									@change=${(e) => rename(key, e.target.value.trim())}></uui-input>
								${renderValue(key, value, (newValue) => onChange({ ...map, [key]: newValue }))}
								<uui-button
									compact
									look="secondary"
									color="danger"
									label="Remove"
									@click=${() => {
										const next = { ...map };
										delete next[key];
										onChange(next);
									}}>
									<uui-icon name="icon-trash"></uui-icon>
								</uui-button>
							</div>
						`,
					)
				: html`<div class="empty">None yet.</div>`}
			<div>
				<uui-button
					look="secondary"
					label=${addLabel}
					@click=${() => onChange({ ...map, '': mapDefaultValue(map) })}>
					<uui-icon name="icon-add"></uui-icon> ${addLabel}
				</uui-button>
			</div>
		</div>
	`;
}

function mapDefaultValue(map) {
	const first = Object.values(map ?? {})[0];
	return Array.isArray(first) ? [] : typeof first === 'number' ? 1 : '';
}

export function toggleValue(list, value, include) {
	return include ? [...list, value] : list.filter((v) => !equals(v, value));
}

export function equals(a, b) {
	return String(a).toLowerCase() === String(b).toLowerCase();
}

export function toNumber(raw, fallback) {
	const parsed = Number(raw);
	return Number.isFinite(parsed) ? parsed : fallback;
}
