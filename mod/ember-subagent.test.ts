import { describe, expect, test } from 'claude-code/testing';
import { displayWidth, resolveModelName } from './hooks/format';
import { getIcons, parseIconSet } from './hooks/icons';
import { gradientForText, PALETTE } from './hooks/palette';
import { composeRowSpans, type RowSpan, type SubagentRowInput } from './hooks/row';

const NERD = getIcons('nerd');
const UNICODE = getIcons('unicode');
const ASCII = getIcons('ascii');

function textOf(spans: readonly RowSpan[]): string {
  return spans.map((s) => s.text).join('');
}

function colorsOf(spans: readonly RowSpan[]): number[] {
  return spans.map((s) => s.color);
}

function makeTask(overrides: Partial<SubagentRowInput> = {}): SubagentRowInput {
  return {
    name: 'Explore',
    status: 'running',
    model: 'claude-haiku-4-5-20251001',
    contextWindowSize: 200_000,
    tokenCount: 36_000,
    description: 'locate the render call sites',
    ...overrides,
  };
}

function makeAgentRowProps(overrides: Record<string, unknown> = {}) {
  return {
    id: 't1',
    name: 'Explore',
    type: 'local_agent',
    kind: 'local_agent' as const,
    status: 'running' as const,
    taskStatus: 'running' as const,
    description: 'locate the render call sites',
    label: 'reading files',
    model: 'claude-haiku-4-5-20251001',
    effort: 'high' as const,
    contextWindowSize: 200_000,
    tokenCount: 36_000,
    columns: 100,
    isHidden: false,
    hasRow: true,
    rowIds: ['t1'],
    ...overrides,
  };
}

describe('resolveModelName', () => {
  test('resolves Claude families and strips date stamps and context tags', () => {
    expect(resolveModelName('claude-opus-5')).toBe('Opus 5');
    expect(resolveModelName('claude-opus-4-8')).toBe('Opus 4.8');
    expect(resolveModelName('claude-sonnet-4-6')).toBe('Sonnet 4.6');
    expect(resolveModelName('claude-haiku-4-5-20251001')).toBe('Haiku 4.5');
    expect(resolveModelName('claude-fable-5-1')).toBe('Fable 5.1');
    expect(resolveModelName('claude-opus-4-8[1m]')).toBe('Opus 4.8');
    expect(resolveModelName('claude-sonnet-4-6-20260101[1m]')).toBe('Sonnet 4.6');
    expect(resolveModelName('gpt-5')).toBe('gpt-5');
    expect(resolveModelName('claude-neptune-6[1m]')).toBe('claude-neptune-6');
    expect(resolveModelName('[1m]')).toBe('[1m]');
  });
});

describe('parseIconSet', () => {
  test('accepts nerd, unicode, ascii and defaults to nerd', () => {
    expect(parseIconSet('nerd')).toBe('nerd');
    expect(parseIconSet('UNICODE')).toBe('unicode');
    expect(parseIconSet('ascii')).toBe('ascii');
    expect(parseIconSet('bogus')).toBe('nerd');
    expect(parseIconSet(undefined)).toBe('nerd');
  });
});

describe('composeRowSpans', () => {
  test('running row uses full gradient, green marker, model and severity bar', () => {
    const spans = composeRowSpans(makeTask(), NERD, 100);
    const text = textOf(spans);

    expect(text).toContain('Explore');
    expect(text).toContain('Haiku 4.5');
    expect(text).toContain('18%');
    expect(text).toContain('36.0k');
    expect(colorsOf(spans)).toContain(PALETTE.branch);
    expect(colorsOf(spans)).toContain(gradientForText('Explore')[0]);
  });

  test('failed and killed rows keep full gradient with critical red marker', () => {
    const failed = composeRowSpans(makeTask({ status: 'failed', name: 'risky-task' }), NERD, 100);
    const killed = composeRowSpans(makeTask({ status: 'killed', name: 'risky-task' }), NERD, 100);

    expect(colorsOf(failed)).toContain(PALETTE.critical);
    expect(colorsOf(failed)).toContain(gradientForText('risky-task')[0]);
    expect(killed).toEqual(failed);
  });

  test('inactive rows (pending, paused, waiting, idle, completed) render entirely flat 240', () => {
    for (const status of ['pending', 'queued', 'paused', 'waiting', 'idle', 'completed', 'done']) {
      const spans = composeRowSpans(
        makeTask({
          status,
          name: 'code-review',
          model: 'claude-opus-5-20260101',
          effort: 'xhigh',
          contextWindowSize: 200_000,
          tokenCount: 140_000,
        }),
        NERD,
        100,
      );
      expect(textOf(spans)).toContain('70%');
      for (const color of colorsOf(spans)) {
        expect(color).toBe(PALETTE.unfilledBar);
      }
      for (const span of spans) {
        expect(span.bold).toBe(false);
      }
    }
  });

  test('queued and done markers differ and ascii falls back cleanly', () => {
    const queued = textOf(composeRowSpans(makeTask({ status: 'pending' }), NERD, 100));
    const done = textOf(composeRowSpans(makeTask({ status: 'completed' }), NERD, 100));
    expect(queued[0]).not.toBe(done[0]);
    expect(done.startsWith(NERD.done)).toBe(true);

    const asciiRunning = textOf(composeRowSpans(makeTask({ status: 'running' }), ASCII, 100));
    const asciiQueued = textOf(composeRowSpans(makeTask({ status: 'pending' }), ASCII, 100));
    expect(asciiRunning.startsWith('* ')).toBe(true);
    expect(asciiQueued.startsWith('. ')).toBe(true);
  });

  test('effort renders string verbatim, integer as token count, and omits when absent', () => {
    expect(textOf(composeRowSpans(makeTask({ effort: 'high' }), NERD, 100))).toContain('high');
    expect(textOf(composeRowSpans(makeTask({ effort: 50_000 }), NERD, 100))).toContain('50.0k');
    expect(textOf(composeRowSpans(makeTask({ effort: undefined }), NERD, 100))).not.toContain('high');
  });

  test('unnamed row promotes description and falls back to type when empty', () => {
    const promoted = textOf(
      composeRowSpans(
        makeTask({ name: undefined, type: 'local_agent', description: 'Probe model-opus-4-8' }),
        NERD,
        100,
      ),
    );
    expect(promoted.startsWith('● Probe model-opus-4-8')).toBe(true);
    expect(promoted).not.toContain('local_agent');

    const fallback = textOf(
      composeRowSpans(makeTask({ name: undefined, type: 'local_agent', description: '' }), NERD, 100),
    );
    expect(fallback.startsWith('● local_agent')).toBe(true);
  });

  test('sheds bar -> effort -> tokens -> model before cutting the headline', () => {
    const probe = makeTask({
      name: undefined,
      type: 'local_agent',
      description: 'Probe model-opus-4-8',
      model: 'claude-opus-4-8',
      effort: 'high',
      contextWindowSize: 200_000,
      tokenCount: 36_000,
    });

    const at67 = textOf(composeRowSpans(probe, NERD, 67));
    expect(at67).toContain('Probe model-opus-4-8');
    expect(at67).toContain('high');
    expect(at67).toContain('░░░░░░░░');
    expect(at67).toContain('36.0k');

    const at64 = textOf(composeRowSpans(probe, NERD, 64));
    expect(at64).toContain('Probe model-opus-4-8');
    expect(at64).toContain('high');
    expect(at64).toContain('░░░░ ');
    expect(at64).not.toContain('░░░░░░');

    const at57 = textOf(composeRowSpans(probe, NERD, 57));
    expect(at57).toContain('Probe model-opus-4-8');
    expect(at57).not.toContain('high');
    expect(at57).toContain('36.0k');

    const at51 = textOf(composeRowSpans(probe, NERD, 51));
    expect(at51).toContain('Probe model-opus-4-8');
    expect(at51).toContain('Opus 4.8');
    expect(at51).not.toContain('36.0k');

    const at40 = textOf(composeRowSpans(probe, NERD, 40));
    expect(at40).toContain('Probe model-opus-4-8');
    expect(at40).not.toContain('Opus');

    const at30 = textOf(composeRowSpans(probe, NERD, 30));
    expect(at30.startsWith('● Probe')).toBe(true);
    expect(displayWidth(at30)).toBeLessThanOrEqual(30);
  });

  test('drops tiny tail along with its separator and preserves emoji surrogate pairs', () => {
    const tight = textOf(
      composeRowSpans(
        makeTask({ name: 'code-review', model: undefined, description: '/code-review max --fix' }),
        NERD,
        32,
      ),
    );
    expect(tight).not.toContain('/co');
    expect(tight.endsWith('%')).toBe(true);
    expect(displayWidth(tight)).toBeLessThanOrEqual(32);

    const desc = 'emoji: \u{1F600}\u{1F601}\u{1F602} done';
    const emojiRow = textOf(composeRowSpans(makeTask({ description: desc }), UNICODE, 60));
    for (let i = 0; i < emojiRow.length; i++) {
      const code = emojiRow.charCodeAt(i);
      if (code >= 0xd800 && code <= 0xdbff) {
        const next = emojiRow.charCodeAt(i + 1);
        expect(next >= 0xdc00 && next <= 0xdfff).toBe(true);
      }
    }
  });
});

describe('AgentRow ui.render integration', () => {
  for (const surface of ['terminal', 'desktop', 'vscode', 'mobile'] as const) {
    test(`renders Ember subagent row on ${surface}`, async ($) => {
      const ui = await $.ui.mount({
        plugin: 'ember-subagent',
        surface,
        component: 'AgentRow',
        props: makeAgentRowProps(),
      });

      const texts = await ui.findAll({ type: 'Text' });
      expect(texts.length).toBeGreaterThan(0);
      expect(await ui.find({ text: 'Haiku 4.5' })).toBeDefined();
      expect(await ui.find({ text: '18%' })).toBeDefined();
      expect(await ui.find({ text: '36.0k' })).toBeDefined();
      await ui.unmount();
    });
  }

  test('respects icons option from userConfig', { options: { icons: 'ascii' } }, async ($) => {
    const ui = await $.ui.mount({
      plugin: 'ember-subagent',
      surface: 'terminal',
      component: 'AgentRow',
      props: makeAgentRowProps(),
    });

    expect(await ui.find({ text: '* ' })).toBeDefined();
    expect(await ui.find({ text: ' > ' })).toBeDefined();
    await ui.unmount();
  });

  test('passes hidden rows through to next(e)', async ($) => {
    const rendered = await $.ui.render({
      component: 'AgentRow',
      surface: 'terminal',
      requestId: 'hidden-1',
      props: makeAgentRowProps({ isHidden: true }),
    });
    expect((rendered as { type?: string }).type).not.toBe('Box');
  });

  test('uses viewport.columns when props.columns is absent', async ($) => {
    const ui = await $.ui.mount({
      plugin: 'ember-subagent',
      surface: 'terminal',
      component: 'AgentRow',
      viewport: { columns: 51, rows: 40 },
      props: makeAgentRowProps({
        name: undefined,
        description: 'Probe model-opus-4-8',
        model: 'claude-opus-4-8',
        effort: 'high',
        columns: undefined,
      }),
    });

    expect(await ui.find({ text: 'Opus 4.8' })).toBeDefined();
    const tokenMatches = await ui.findAll({ text: '36.0k' });
    expect(tokenMatches.length).toBe(0);
    await ui.unmount();
  });
});
