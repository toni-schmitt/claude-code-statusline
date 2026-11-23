import type { BarChars } from './icons';
import { PALETTE, severity } from './palette';

export interface BarCell {
  readonly glyph: string;
  readonly color: number;
  readonly bold: boolean;
}

export function roundHalfUp(x: number): number {
  return Math.floor(x + 0.5 + 1e-9);
}

/** Integer percent, no decimal, half-up rounded. Matches Ember.Core.Render.Format.Percent. */
export function formatPercent(pct: number): string {
  const safe = Number.isFinite(pct) ? pct : 0;
  return `${roundHalfUp(safe)}%`;
}

/**
 * Compact token count: "36.0k", "175.1k", "1.2M", "0".
 * Matches Ember.Core.Render.Format.TokenCount.
 */
export function formatTokenCount(count: number): string {
  const n = Number.isFinite(count) ? Math.max(0, Math.trunc(count)) : 0;
  if (n < 1000) return `${n}`;
  if (n < 1_000_000) return `${(n / 1000).toFixed(1)}k`;
  return `${(n / 1_000_000).toFixed(1)}M`;
}

/** Half-cell severity usage meter. Matches Ember.Core.Render.Meter.Render. */
export function renderMeter(usedPercentage: number, cells: number, chars: BarChars): BarCell[] {
  const safe = Number.isFinite(usedPercentage) ? Math.max(0, usedPercentage) : 0;
  const perHalf = 100 / (2 * cells);
  const maxHalves = 2 * cells;

  const halves =
    safe >= 100 ? maxHalves : Math.min(maxHalves - 1, roundHalfUp(safe / perHalf));

  const occupied = Math.floor((halves + 1) / 2);
  const hasHalf = halves % 2 === 1;
  const perCell = 100 / cells;

  const result = new Array<BarCell>(cells);
  for (let i = 0; i < occupied; i++) {
    const outermost = i === occupied - 1 && hasHalf;
    const style = severity((i + 1) * perCell);
    result[i] = {
      glyph: outermost ? chars.half : chars.full,
      color: style.color,
      bold: style.bold,
    };
  }
  for (let i = occupied; i < cells; i++) {
    result[i] = {
      glyph: chars.empty,
      color: PALETTE.unfilledBar,
      bold: false,
    };
  }
  return result;
}

const MODEL_FAMILIES = new Set(['opus', 'sonnet', 'haiku', 'fable']);

/**
 * Best-effort model-ID -> display-name mapping matching Ember.Core.Data.ModelNames.Resolve.
 * Strips Claude Code's optional bracketed context-window tag (e.g. `[1m]`) before parsing.
 */
export function resolveModelName(modelId: string): string {
  const tagIdx = modelId.indexOf('[');
  const id = tagIdx < 0 ? modelId : modelId.slice(0, tagIdx);

  const parts = id.split('-').filter((p) => p.length > 0);
  let family: string | null = null;
  const version: string[] = [];

  for (const part of parts) {
    const lower = part.toLowerCase();
    if (MODEL_FAMILIES.has(lower)) {
      family = lower.charAt(0).toUpperCase() + lower.slice(1);
      continue;
    }
    if (part.length === 8 && isAllAsciiDigits(part)) continue;
    if (lower === 'claude') continue;
    if (isAllAsciiDigits(part)) version.push(part);
  }

  if (family === null) return id.length > 0 ? id : modelId;
  return version.length === 0 ? family : `${family} ${version.join('.')}`;
}

function isAllAsciiDigits(s: string): boolean {
  if (s.length === 0) return false;
  for (let i = 0; i < s.length; i++) {
    const code = s.charCodeAt(i);
    if (code < 48 || code > 57) return false;
  }
  return true;
}

/** Terminal display width: 2 for East Asian wide characters, 1 for everything else. Matches AnsiBuilder.cs. */
export function displayWidth(text: string): number {
  let width = 0;
  for (const ch of text) {
    const cp = ch.codePointAt(0) ?? 0;
    width += isWide(cp) ? 2 : 1;
  }
  return width;
}

/** Longest prefix of `text` fitting within `maxWidth` columns, never splitting a surrogate pair. */
export function truncateToWidth(text: string, maxWidth: number): string {
  if (maxWidth <= 0) return '';
  let width = 0;
  let end = 0;
  for (const ch of text) {
    const cp = ch.codePointAt(0) ?? 0;
    const cw = isWide(cp) ? 2 : 1;
    if (width + cw > maxWidth) break;
    width += cw;
    end += ch.length;
  }
  return text.slice(0, end);
}

function isWide(cp: number): boolean {
  return (
    (cp >= 0x1100 && cp <= 0x115f) ||
    (cp >= 0x2e80 && cp <= 0x303e) ||
    (cp >= 0x3041 && cp <= 0x33bf) ||
    (cp >= 0x3400 && cp <= 0x4dbf) ||
    (cp >= 0x4e00 && cp <= 0x9fff) ||
    (cp >= 0xa000 && cp <= 0xa4cf) ||
    (cp >= 0xac00 && cp <= 0xd7af) ||
    (cp >= 0xf900 && cp <= 0xfaff) ||
    (cp >= 0xfe30 && cp <= 0xfe6f) ||
    (cp >= 0xff01 && cp <= 0xff60) ||
    (cp >= 0xffe0 && cp <= 0xffe6) ||
    (cp >= 0x20000 && cp <= 0x2fa1f)
  );
}
