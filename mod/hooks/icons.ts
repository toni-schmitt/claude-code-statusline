export type IconSet = 'nerd' | 'unicode' | 'ascii';

export interface BarChars {
  readonly full: string;
  readonly half: string;
  readonly empty: string;
}

export interface SubagentIcons {
  readonly set: IconSet;
  readonly model: string;
  readonly effort: string;
  readonly done: string;
  readonly separator: string;
  readonly runningMarker: string;
  readonly failedMarker: string;
  readonly queuedMarker: string;
  readonly bar: BarChars;
}

const ICON_SETS: Record<IconSet, SubagentIcons> = {
  nerd: {
    set: 'nerd',
    model: '\uf2db',
    effort: '\uf0e7',
    done: '\uf00c',
    separator: '❯',
    runningMarker: '●',
    failedMarker: '●',
    queuedMarker: '○',
    bar: { full: '█', half: '▌', empty: '░' },
  },
  unicode: {
    set: 'unicode',
    model: '◆',
    effort: '▲',
    done: '✔',
    separator: '❯',
    runningMarker: '●',
    failedMarker: '●',
    queuedMarker: '○',
    bar: { full: '█', half: '▌', empty: '░' },
  },
  ascii: {
    set: 'ascii',
    model: '*',
    effort: '^',
    done: 'x',
    separator: '>',
    runningMarker: '*',
    failedMarker: '*',
    queuedMarker: '.',
    bar: { full: '#', half: '+', empty: '-' },
  },
};

/** Parses the configured icon set, falling back to `nerd` for unrecognised values. */
export function parseIconSet(value: unknown): IconSet {
  if (typeof value !== 'string') return 'nerd';
  switch (value.trim().toLowerCase()) {
    case 'nerd':
      return 'nerd';
    case 'unicode':
      return 'unicode';
    case 'ascii':
      return 'ascii';
    default:
      return 'nerd';
  }
}

export function getIcons(set: IconSet): SubagentIcons {
  return ICON_SETS[set];
}
