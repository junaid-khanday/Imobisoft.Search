import { css } from '@umbraco-cms/backoffice/external/lit';

/**
 * Styles shared by every dashboard in the package. Everything is expressed in Umbraco's own design
 * tokens so the package inherits the backoffice theme rather than fighting it.
 */
export const sharedStyles = css`
	:host {
		display: block;
		padding: var(--uui-size-layout-1);
		box-sizing: border-box;
	}

	.stack {
		display: flex;
		flex-direction: column;
		gap: var(--uui-size-space-4);
	}

	.row {
		display: flex;
		align-items: center;
		gap: var(--uui-size-space-3);
		flex-wrap: wrap;
	}

	.row-tight {
		display: flex;
		align-items: center;
		gap: var(--uui-size-space-2);
	}

	.spread {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: var(--uui-size-space-3);
	}

	.grow {
		flex: 1;
		min-width: 0;
	}

	.muted {
		color: var(--uui-color-text-alt);
		font-size: var(--uui-type-small-size);
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: var(--uui-size-space-1);
		margin-bottom: var(--uui-size-space-5);
	}

	.field > label {
		font-weight: 700;
	}

	.field .muted {
		margin-bottom: var(--uui-size-space-2);
	}

	.grid-2 {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
		gap: 0 var(--uui-size-space-5);
	}

	.check-list {
		display: flex;
		flex-direction: column;
		gap: var(--uui-size-space-2);
		max-height: 260px;
		overflow-y: auto;
		border: 1px solid var(--uui-color-border);
		border-radius: var(--uui-border-radius);
		padding: var(--uui-size-space-3);
	}

	.chips {
		display: flex;
		flex-wrap: wrap;
		gap: var(--uui-size-space-2);
		align-items: center;
	}

	.chip {
		display: inline-flex;
		align-items: center;
		gap: var(--uui-size-space-1);
		background: var(--uui-color-surface-alt);
		border: 1px solid var(--uui-color-border);
		border-radius: var(--uui-border-radius);
		padding: 2px var(--uui-size-space-2);
		font-size: var(--uui-type-small-size);
	}

	.chip button {
		border: none;
		background: none;
		cursor: pointer;
		color: var(--uui-color-text-alt);
		font-size: 1rem;
		line-height: 1;
		padding: 0;
	}

	.chip button:hover {
		color: var(--uui-color-danger);
	}

	.repeater-row {
		display: flex;
		align-items: center;
		gap: var(--uui-size-space-2);
		padding: var(--uui-size-space-2) 0;
		border-bottom: 1px solid var(--uui-color-divider);
	}

	.repeater-row:last-of-type {
		border-bottom: none;
	}

	.empty {
		padding: var(--uui-size-space-5);
		text-align: center;
		color: var(--uui-color-text-alt);
		border: 1px dashed var(--uui-color-border);
		border-radius: var(--uui-border-radius);
	}

	.error-banner {
		background: var(--uui-color-danger);
		color: var(--uui-color-selected-contrast);
		padding: var(--uui-size-space-3) var(--uui-size-space-4);
		border-radius: var(--uui-border-radius);
		margin-bottom: var(--uui-size-space-4);
	}

	.split {
		display: grid;
		grid-template-columns: minmax(220px, 300px) 1fr;
		gap: var(--uui-size-layout-1);
		align-items: start;
	}

	@media (max-width: 900px) {
		.split {
			grid-template-columns: 1fr;
		}
	}

	table {
		width: 100%;
		border-collapse: collapse;
	}

	th,
	td {
		text-align: left;
		padding: var(--uui-size-space-3);
		border-bottom: 1px solid var(--uui-color-divider);
		vertical-align: top;
	}

	th {
		font-weight: 700;
		white-space: nowrap;
	}

	.numeric {
		text-align: right;
		font-variant-numeric: tabular-nums;
	}

	/* Native dropdowns, styled to sit alongside Umbraco's own inputs. See select() in controls.js
	   for why these are not uui-select. */
	.imob-select {
		font: inherit;
		color: var(--uui-color-text);
		background: var(--uui-color-surface);
		border: 1px solid var(--uui-color-border);
		border-radius: var(--uui-border-radius);
		padding: var(--uui-size-space-2) var(--uui-size-space-3);
		min-height: var(--uui-size-11, 36px);
		max-width: 100%;
	}

	.imob-select:hover {
		border-color: var(--uui-color-border-emphasis);
	}

	.imob-select:focus-visible {
		outline: 2px solid var(--uui-color-focus);
		outline-offset: 1px;
	}

	code {
		font-family: var(--uui-font-monospace, monospace);
		font-size: var(--uui-type-small-size);
		background: var(--uui-color-surface-alt);
		padding: 1px 4px;
		border-radius: 3px;
		word-break: break-all;
	}
`;
