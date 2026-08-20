import { html, svg, nothing } from '@umbraco-cms/backoffice/external/lit';

/**
 * Daily search volume as a stacked column chart: searches that found something, and searches that
 * found nothing.
 *
 * Part-to-whole over time, so the total height reads as traffic and the failures sit on top where
 * they are impossible to miss. Two series, so a legend is always present and every column carries a
 * hover tooltip with the numbers — identity and value are never colour alone. The tables underneath
 * are the table view.
 *
 * Both colour pairs were validated for colour-vision deficiency separation and contrast against
 * their own surface; the dark steps are chosen for the dark surface rather than flipped from light.
 */

/** Widest a column is allowed to get; past this the band's leftover stays as air. */
const MAX_COLUMN_WIDTH = 24;

/** Gap in the surface colour that separates the two stacked segments. */
const SEGMENT_GAP = 2;

/** Radius of the rounded data-end. The baseline end stays square. */
const CAP_RADIUS = 4;

const CHART_HEIGHT = 180;
const AXIS_GUTTER = 44;
const TOP_PADDING = 12;

export const volumeChartStyles = `
	.chart {
		--imob-series-found: #3544b1;
		--imob-series-empty: #d42054;
		position: relative;
	}

	@media (prefers-color-scheme: dark) {
		.chart {
			--imob-series-found: #7a86e0;
			--imob-series-empty: #e85d84;
		}
	}

	.chart svg { display: block; width: 100%; height: auto; }
	.chart .axis { stroke: var(--uui-color-divider-standalone); stroke-width: 1; }
	.chart .tick-label { fill: var(--uui-color-text-alt); font-size: 11px; }
	.chart .column-hit { fill: transparent; cursor: pointer; }
	.chart .column-hit:hover + .column-marks { opacity: 0.85; }

	.chart-legend {
		display: flex;
		gap: var(--uui-size-space-4);
		align-items: center;
		margin-bottom: var(--uui-size-space-3);
		font-size: var(--uui-type-small-size);
		color: var(--uui-color-text-alt);
	}

	.chart-legend span { display: inline-flex; align-items: center; gap: 6px; }
	.chart-legend i { width: 10px; height: 10px; border-radius: 2px; display: inline-block; }

	.chart-tooltip {
		position: absolute;
		pointer-events: none;
		background: var(--uui-color-surface);
		border: 1px solid var(--uui-color-border);
		border-radius: var(--uui-border-radius);
		box-shadow: var(--uui-shadow-depth-2);
		padding: var(--uui-size-space-2) var(--uui-size-space-3);
		font-size: var(--uui-type-small-size);
		white-space: nowrap;
		z-index: 2;
		transform: translate(-50%, -100%);
	}

	.chart-tooltip strong { display: block; margin-bottom: 2px; }
	.chart-tooltip .swatch { display: inline-block; width: 8px; height: 8px; border-radius: 2px; margin-right: 4px; }
`;

/**
 * @param {Array<{date: string, searchCount: number, zeroResultCount: number}>} volume
 * @param {{hovered: number, onHover: (index: number) => void}} interaction
 */
export function renderVolumeChart(volume, interaction) {
	if (!volume?.length) {
		return html`<div class="empty">No searches recorded in this period.</div>`;
	}

	const width = 720;
	const plotWidth = width - AXIS_GUTTER;
	const band = plotWidth / volume.length;
	const columnWidth = Math.min(MAX_COLUMN_WIDTH, Math.max(3, band * 0.65));

	const peak = Math.max(...volume.map((d) => d.searchCount), 1);
	const ceiling = niceCeiling(peak);
	const scale = (value) => (value / ceiling) * (CHART_HEIGHT - TOP_PADDING);

	const hovered = volume[interaction.hovered];

	return html`
		<div class="chart">
			<div class="chart-legend">
				<span><i style="background: var(--imob-series-found)"></i> Found results</span>
				<span><i style="background: var(--imob-series-empty)"></i> Found nothing</span>
			</div>

			<svg viewBox="0 0 ${width} ${CHART_HEIGHT + 26}" role="img"
				aria-label="Daily search volume, split into searches that found results and searches that found nothing.">
				${renderYAxis(ceiling, width, scale)}
				${volume.map((day, index) => renderColumn(day, index, band, columnWidth, scale, interaction))}
				<line class="axis" x1=${AXIS_GUTTER} y1=${CHART_HEIGHT} x2=${width} y2=${CHART_HEIGHT}></line>
				${renderXLabels(volume, band)}
			</svg>

			${hovered ? renderTooltip(hovered, interaction.hovered, band) : nothing}
		</div>
	`;
}

function renderYAxis(ceiling, width, scale) {
	// Three ticks is enough to read magnitude without turning the plot into a grid.
	const values = [0, Math.round(ceiling / 2), ceiling];

	return svg`
		${values.map((value) => {
			const y = CHART_HEIGHT - scale(value);
			return svg`
				<line class="axis" x1=${AXIS_GUTTER} y1=${y} x2=${width} y2=${y} opacity=${value === 0 ? 1 : 0.4}></line>
				<text class="tick-label" x=${AXIS_GUTTER - 8} y=${y + 4} text-anchor="end">${value.toLocaleString()}</text>
			`;
		})}
	`;
}

function renderColumn(day, index, band, columnWidth, scale, interaction) {
	const x = AXIS_GUTTER + index * band + (band - columnWidth) / 2;

	const emptyHeight = scale(day.zeroResultCount);
	const foundHeight = scale(day.searchCount - day.zeroResultCount);

	// The failure segment sits on top, so it always owns the rounded data-end when present.
	const hasEmpty = day.zeroResultCount > 0;
	const hasFound = day.searchCount - day.zeroResultCount > 0;

	const foundY = CHART_HEIGHT - foundHeight;
	const emptyY = foundY - emptyHeight - (hasFound && hasEmpty ? SEGMENT_GAP : 0);

	return svg`
		<g>
			<rect class="column-hit"
				x=${AXIS_GUTTER + index * band} y=${TOP_PADDING}
				width=${band} height=${CHART_HEIGHT - TOP_PADDING}
				@mouseenter=${() => interaction.onHover(index)}
				@mouseleave=${() => interaction.onHover(-1)}></rect>
			<g class="column-marks" style="pointer-events: none">
				${hasFound
					? svg`<path d=${segmentPath(x, foundY, columnWidth, foundHeight, !hasEmpty)}
							fill="var(--imob-series-found)"></path>`
					: nothing}
				${hasEmpty
					? svg`<path d=${segmentPath(x, emptyY, columnWidth, emptyHeight, true)}
							fill="var(--imob-series-empty)"></path>`
					: nothing}
			</g>
		</g>
	`;
}

function renderXLabels(volume, band) {
	// Label roughly six points, so dates never collide however wide the window is.
	const step = Math.max(1, Math.ceil(volume.length / 6));

	return svg`
		${volume.map((day, index) =>
			index % step === 0
				? svg`<text class="tick-label" x=${AXIS_GUTTER + index * band + band / 2} y=${CHART_HEIGHT + 18}
						text-anchor="middle">${shortDate(day.date)}</text>`
				: nothing,
		)}
	`;
}

function renderTooltip(day, index, band) {
	const found = day.searchCount - day.zeroResultCount;
	const left = AXIS_GUTTER + index * band + band / 2;

	return html`
		<div class="chart-tooltip" style="left: ${(left / 720) * 100}%; top: -4px">
			<strong>${longDate(day.date)}</strong>
			<div><i class="swatch" style="background: var(--imob-series-found)"></i>${found.toLocaleString()} found results</div>
			<div><i class="swatch" style="background: var(--imob-series-empty)"></i>${day.zeroResultCount.toLocaleString()} found nothing</div>
		</div>
	`;
}

/**
 * A rectangle with an optional rounded top. The baseline end stays square so every column sits flat
 * on the axis.
 */
function segmentPath(x, y, width, height, roundTop) {
	if (height <= 0) {
		return '';
	}

	if (!roundTop) {
		return `M${x},${y}h${width}v${height}h${-width}Z`;
	}

	const r = Math.min(CAP_RADIUS, width / 2, height);

	return `M${x},${y + r}a${r},${r} 0 0 1 ${r},${-r}h${width - 2 * r}a${r},${r} 0 0 1 ${r},${r}v${height - r}h${-width}Z`;
}

/** Rounds the axis maximum up to a clean number, so the ticks read 0 / 25 / 50 rather than 0 / 23 / 47. */
function niceCeiling(peak) {
	if (peak <= 5) return 5;

	const magnitude = 10 ** Math.floor(Math.log10(peak));
	return Math.ceil(peak / (magnitude / 2)) * (magnitude / 2);
}

function shortDate(iso) {
	return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

function longDate(iso) {
	return new Date(iso).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
}
