import {
  displayWidth,
  formatPercent,
  formatTokenCount,
  renderMeter,
  resolveModelName,
  truncateToWidth,
} from './format';
import type { SubagentIcons } from './icons';
import { gradientForText, PALETTE, severity } from './palette';

export interface SubagentRowInput {
  readonly name?: string;
  readonly type?: string;
  readonly status?: string;
  readonly description?: string;
  readonly model?: string;
  readonly effort?: string | number;
  readonly contextWindowSize?: number;
  readonly tokenCount?: number;
}

export interface RowSpan {
  readonly text: string;
  readonly color: number;
  readonly bold: boolean;
}

interface RowTier {
  readonly showEffort: boolean;
  readonly cells: number;
  readonly showTokens: boolean;
  readonly showModel: boolean;
}

const FULL_TIER: RowTier = { showEffort: true, cells: 10, showTokens: true, showModel: true };

/**
 * Width-degradation ladder matching Ember.Subagent.Row.Tiers:
 * bar halves to 5 cells -> effort -> token count -> model.
 */
const FALLBACK_TIERS: readonly RowTier[] = [
  { showEffort: true, cells: 5, showTokens: true, showModel: true },
  { showEffort: false, cells: 5, showTokens: true, showModel: true },
  { showEffort: false, cells: 5, showTokens: false, showModel: true },
  { showEffort: false, cells: 5, showTokens: false, showModel: false },
];

const HEADLINE_FLOOR = 20;
const TAIL_FLOOR = 8;

type NormalizedStatus = 'pending' | 'running' | 'completed' | 'failed' | 'killed' | 'paused' | 'unknown';

function normalizeStatus(status: string | undefined): NormalizedStatus {
  switch (status?.toLowerCase()) {
    case 'pending':
    case 'queued':
    case 'waiting':
    case 'idle':
      return 'pending';
    case 'running':
      return 'running';
    case 'completed':
    case 'done':
      return 'completed';
    case 'failed':
      return 'failed';
    case 'killed':
      return 'killed';
    case 'paused':
      return 'paused';
    default:
      return 'unknown';
  }
}

class SpanBuilder {
  private readonly spans: RowSpan[] = [];
  width = 0;

  push(text: string, color: number, bold = false): void {
    if (text.length === 0) return;
    const prev = this.spans[this.spans.length - 1];
    if (prev && prev.color === color && prev.bold === bold) {
      this.spans[this.spans.length - 1] = {
        text: prev.text + text,
        color,
        bold,
      };
    } else {
      this.spans.push({ text, color, bold });
    }
    this.width += displayWidth(text);
  }

  pushGradient(text: string): void {
    const chars = Array.from(text);
    const colors = gradientForText(text);
    for (let i = 0; i < chars.length; i++) {
      const ch = chars[i];
      if (ch !== undefined) {
        this.push(ch, colors[i] ?? PALETTE.gradientStop1, false);
      }
    }
  }

  append(other: SpanBuilder): void {
    for (const span of other.spans) {
      this.push(span.text, span.color, span.bold);
    }
  }

  build(): RowSpan[] {
    return this.spans;
  }
}

function markerFor(status: NormalizedStatus, icons: SubagentIcons): { glyph: string; color: number } {
  switch (status) {
    case 'running':
      return { glyph: icons.runningMarker, color: PALETTE.branch };
    case 'failed':
    case 'killed':
      return { glyph: icons.failedMarker, color: PALETTE.critical };
    case 'completed':
      return { glyph: icons.done, color: PALETTE.unfilledBar };
    default:
      return { glyph: icons.queuedMarker, color: PALETTE.unfilledBar };
  }
}

function describeEffort(effort: string | number | undefined): string | null {
  if (typeof effort === 'string') return effort.length > 0 ? effort : null;
  if (typeof effort === 'number' && Number.isFinite(effort) && Number.isInteger(effort)) {
    return formatTokenCount(effort);
  }
  return null;
}

function composeMetrics(
  task: SubagentRowInput,
  tier: RowTier,
  icons: SubagentIcons,
  active: boolean,
  labelColor: number,
  sepColor: number,
): SpanBuilder {
  const b = new SpanBuilder();

  if (tier.showModel && task.model && task.model.length > 0) {
    b.push(` ${icons.model} `, labelColor);
    b.push(resolveModelName(task.model), active ? PALETTE.gradientStop2 : PALETTE.unfilledBar);

    const effortText = tier.showEffort ? describeEffort(task.effort) : null;
    if (effortText && effortText.length > 0) {
      b.push(` ${icons.effort} `, labelColor);
      b.push(effortText, active ? PALETTE.gradientStop6 : PALETTE.unfilledBar);
    }
  }

  b.push(` ${icons.separator} `, sepColor);
  b.push('ctx ', labelColor);

  const hasWindow =
    typeof task.contextWindowSize === 'number' &&
    task.contextWindowSize > 0 &&
    typeof task.tokenCount === 'number' &&
    Number.isFinite(task.tokenCount);
  const rawPct = hasWindow ? (100 * task.tokenCount!) / task.contextWindowSize! : 0;
  const pct = Math.min(999, Math.max(0, rawPct));

  const cells = renderMeter(pct, tier.cells, icons.bar);
  for (const cell of cells) {
    if (active) {
      b.push(cell.glyph, cell.color, cell.bold);
    } else {
      b.push(cell.glyph, PALETTE.unfilledBar, false);
    }
  }

  const pctStyle = severity(pct);
  b.push(
    ` ${formatPercent(pct)}`,
    active ? pctStyle.color : PALETTE.unfilledBar,
    active && pctStyle.bold,
  );

  if (tier.showTokens) {
    b.push(` ${formatTokenCount(task.tokenCount ?? 0)}`, labelColor);
  }

  return b;
}

/**
 * Composes one Ember subagent row as styled colour spans fitting within `columns`.
 * Mirrors Ember.Subagent.Row.Compose: active (running/failed/killed) rows keep the
 * full ember gradient and severity colours; inactive (queued/pending/idle/waiting/
 * paused/done) rows render entirely in colour 240 so idle work recedes.
 */
export function composeRowSpans(
  task: SubagentRowInput,
  icons: SubagentIcons,
  columns: number,
): RowSpan[] {
  const b = new SpanBuilder();
  const status = normalizeStatus(task.status);
  const active = status === 'running' || status === 'failed' || status === 'killed';
  const labelColor = active ? PALETTE.label : PALETTE.unfilledBar;
  const sepColor = active ? PALETTE.separator : PALETTE.unfilledBar;

  const marker = markerFor(status, icons);
  b.push(`${marker.glyph} `, marker.color);

  const description = task.description ?? '';
  let headline: string;
  let tail: string;
  if (task.name && task.name.length > 0) {
    headline = task.name;
    tail = description;
  } else {
    headline = description.length > 0 ? description : (task.type ?? '');
    tail = '';
  }

  const floor = Math.min(displayWidth(headline), HEADLINE_FLOOR);
  let metrics = composeMetrics(task, FULL_TIER, icons, active, labelColor, sepColor);
  for (const tier of FALLBACK_TIERS) {
    if (columns - b.width - metrics.width >= floor) break;
    metrics = composeMetrics(task, tier, icons, active, labelColor, sepColor);
  }

  headline = truncateToWidth(headline, Math.max(0, columns - b.width - metrics.width));
  if (active) {
    b.pushGradient(headline);
  } else {
    b.push(headline, PALETTE.unfilledBar);
  }
  b.append(metrics);

  const separator = ` ${icons.separator} `;
  const tailRoom = columns - b.width - displayWidth(separator);
  if (tailRoom < Math.min(displayWidth(tail), TAIL_FLOOR)) return b.build();

  tail = truncateToWidth(tail, Math.max(0, tailRoom));
  if (tail.length === 0) return b.build();

  b.push(separator, sepColor);
  b.push(tail, PALETTE.unfilledBar);
  return b.build();
}
