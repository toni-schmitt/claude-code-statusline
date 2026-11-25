/**
 * ANSI-256 palette matching Ember.Core.Render.Palette and Gradient.
 * Uses straight ANSI-256 indices via Claude Code's `ansi256(n)` colour syntax
 * so subagent rows match the main Ember status line exactly.
 */
export const PALETTE = {
  gradientStop1: 230,
  gradientStop2: 223,
  gradientStop3: 216,
  gradientStop4: 209,
  gradientStop5: 202,
  gradientStop6: 166,

  separator: 175,
  label: 245,
  branch: 150,
  notice: 222,
  warn: 180,
  critical: 203,
  unfilledBar: 240,
} as const;

export const GRADIENT_RAMP = [
  PALETTE.gradientStop1,
  PALETTE.gradientStop2,
  PALETTE.gradientStop3,
  PALETTE.gradientStop4,
  PALETTE.gradientStop5,
  PALETTE.gradientStop6,
] as const;

export function ansi256(code: number): `ansi256(${number})` {
  return `ansi256(${code})`;
}

export interface SeverityStyle {
  readonly color: number;
  readonly bold: boolean;
}

/** Severity thresholds matching Ember.Core.Render.Palette.Severity. */
export function severity(usedPercentage: number): SeverityStyle {
  if (usedPercentage < 60) return { color: PALETTE.gradientStop2, bold: false };
  if (usedPercentage < 80) return { color: PALETTE.notice, bold: false };
  if (usedPercentage < 95) return { color: PALETTE.warn, bold: false };
  return { color: PALETTE.critical, bold: true };
}

/**
 * Six-stop ember gradient across a string, one colour per Unicode code point.
 * Interpolated in integer arithmetic (round-half-up) so the first glyph always
 * lands on stop 0 and the last always lands on stop 5, matching Gradient.cs.
 */
export function gradientForText(text: string): number[] {
  const chars = Array.from(text);
  const len = chars.length;
  if (len === 0) return [];
  if (len === 1) return [GRADIENT_RAMP[0]];

  const denom = len - 1;
  const colors = new Array<number>(len);
  for (let i = 0; i < len; i++) {
    const rampIndex = Math.floor((2 * i * 5 + denom) / (2 * denom));
    colors[i] = GRADIENT_RAMP[rampIndex] ?? PALETTE.gradientStop6;
  }
  return colors;
}
