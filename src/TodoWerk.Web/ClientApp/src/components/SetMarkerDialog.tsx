import { useLayoutEffect, useState } from 'react';
import {
  Button,
  Caption1,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { sameMarker, validateMarker } from './validateMarker';

/**
 * A small grid of markers people reach for, bundled rather than fetched: an emoji picker is the
 * operating system's job, and a dataset dependency for twenty-four characters would be a
 * megabyte to save a paste. Anything not here is typed, pasted, or picked from the
 * system's own picker — the field takes whatever a Marker may be.
 */
const COMMON_MARKERS = [
  '🍞',
  '☕',
  '🔥',
  '⭐',
  '❗',
  '⏰',
  '📌',
  '📞',
  '✉️',
  '💰',
  '🏠',
  '🚗',
  '🛒',
  '🎁',
  '🩺',
  '✈️',
  '📚',
  '🐛',
  '🎯',
  '🧹',
  '💡',
  '🌱',
  '🎵',
  '🏷️',
];

const useStyles = makeStyles({
  surface: {
    maxWidth: '480px',
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
  },
  grid: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
  },
  choice: {
    minWidth: '40px',
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
  },
  field: {
    maxWidth: '160px',
  },
  marker: {
    fontSize: tokens.fontSizeBase500,
  },
  tag: {
    fontFamily: tokens.fontFamilyMonospace,
  },
});

interface SetMarkerDialogProps {
  open: boolean;
  /** The hashtag this rule is about, by its canonical spelling. */
  spelling: string;
  /** The marker it already carries, when this is a change rather than a first choice. */
  current: string | undefined;
  /** A refusal from the server, already worded for a reader — "🍞 is the marker for #bread". */
  error: string | undefined;
  saving: boolean;
  onSave: (marker: string) => void;
  onDismiss: () => void;
}

/**
 * Where a Marker Rule is written. It sets a rule and nothing else: no title moves until the person
 * applies their rules as a Change, previewed like any other (ADR-0014), and the dialog says so
 * rather than leaving them to wonder what just happened to their tasks.
 */
export function SetMarkerDialog({
  open,
  spelling,
  current,
  error,
  saving,
  onSave,
  onDismiss,
}: SetMarkerDialogProps) {
  const styles = useStyles();
  const [marker, setMarker] = useState(current ?? '');

  // Reset whenever the dialog opens on a different hashtag, so it never opens holding what was
  // typed into it about another one.
  useLayoutEffect(() => {
    if (open) setMarker(current ?? '');
  }, [open, current, spelling]);

  const problem = validateMarker(marker);
  const ready = marker !== '' && problem === undefined;

  return (
    <Dialog open={open} onOpenChange={(_event, data) => !data.open && onDismiss()}>
      <DialogSurface className={styles.surface}>
        <DialogBody>
          <DialogTitle>
            Marker for <span className={styles.tag}>#{spelling}</span>
          </DialogTitle>

          <DialogContent className={styles.section}>
            <Caption1>
              Tasks are not changed now. The marker goes on when you apply your markers, which you
              can preview and undo like any other change.
            </Caption1>

            <div className={styles.grid} role="group" aria-label="Common markers">
              {COMMON_MARKERS.map((choice) => (
                <Button
                  key={choice}
                  className={styles.choice}
                  appearance={sameMarker(choice, marker) ? 'primary' : 'secondary'}
                  aria-pressed={sameMarker(choice, marker)}
                  aria-label={`Use ${choice}`}
                  disabled={saving}
                  onClick={() => setMarker(choice)}
                >
                  {choice}
                </Button>
              ))}
            </div>

            <Field
              label="Or any other emoji"
              hint="One emoji. A flag, a keycap or a skin tone counts as one."
              validationState={problem === undefined ? undefined : 'error'}
              validationMessage={problem}
              className={styles.field}
            >
              <Input
                value={marker}
                onChange={(_event, data) => setMarker(data.value)}
                input={{ className: styles.marker }}
                disabled={saving}
              />
            </Field>

            {/* The server's own words, and only where the box has nothing to say — otherwise the
                same complaint would be made twice, the second time by code that knows less. */}
            {error && problem === undefined && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}
          </DialogContent>

          <DialogActions>
            <Button appearance="secondary" onClick={onDismiss} disabled={saving}>
              Cancel
            </Button>
            <Button appearance="primary" disabled={!ready || saving} onClick={() => onSave(marker)}>
              <Text>Save marker</Text>
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
