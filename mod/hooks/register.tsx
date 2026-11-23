import type { Register } from 'claude-code';
import { getIcons, parseIconSet } from './icons';
import { ansi256 } from './palette';
import { composeRowSpans, type RowSpan } from './row';

export const register: Register = (on, options) => {
  const icons = getIcons(parseIconSet(options.icons));

  on('ui.render', { component: 'AgentRow' }, async ($, e, next) => {
    if (e.props.isHidden) return next(e);

    const columns =
      typeof e.props.columns === 'number' && e.props.columns > 0
        ? e.props.columns
        : typeof e.viewport?.columns === 'number' && e.viewport.columns > 0
          ? e.viewport.columns
          : 80;

    let spans: RowSpan[];
    try {
      spans = composeRowSpans(
        {
          name: e.props.name,
          type: e.props.type,
          status: e.props.status ?? e.props.taskStatus,
          description: e.props.description,
          model: e.props.model,
          effort: e.props.effort,
          contextWindowSize: e.props.contextWindowSize,
          tokenCount: e.props.tokenCount,
        },
        icons,
        columns,
      );
    } catch {
      return next(e);
    }

    const { Box, Text } = $.ui.resolve(e);
    return (
      <Box flexDirection="row">
        <Text wrap="truncate">
          {spans.map((span) =>
            Text({
              color: ansi256(span.color),
              weight: span.bold ? 'bold' : undefined,
              children: [span.text],
            }),
          )}
        </Text>
      </Box>
    );
  });
};
