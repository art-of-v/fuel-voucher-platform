import React from 'react';
import { ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { Search, X } from 'lucide-react-native';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Card, Chip, IconButton } from '../../../core/ui';
import { fuelLabel } from '../lib/fuelLabels';

interface Props {
  query: string;
  onQueryChange: (query: string) => void;
  /** Canonical fuel ids, in pump-column order — see `availableFuels`. */
  fuels: string[];
  selectedFuel: string;
  onSelectFuel: (fuel: string) => void;
}

/** How opaque each of the four fade stripes is, left to right. */
const FADE_STEPS = [0.28, 0.56, 0.8, 1];

/**
 * The floating search-and-fuel deck over the map's top edge.
 *
 * Two controls that belong together: the search narrows *which* stations exist,
 * the fuel chips change *what a litre costs*, and every card on the screen below
 * answers those two questions together. Grouping them in one `Card` also replaced
 * the old arrangement of a blurred field at `top: 20` and a separate chip row at
 * `top: 84`, both of which sat inside the band the header's corner targets own.
 *
 * A floating surface, so it carries `Card tone="elevated"` — shadow, no border.
 *
 * The chips carry no icon: every chip in the row is a fuel, so repeating the same
 * pump glyph costs width the grades actually need — the network sells nine of them,
 * and the row has never fitted a whole set beside a phone's screen.
 */
export function MapSearchDeck({ query, onQueryChange, fuels, selectedFuel, onSelectFuel }: Props) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);

  // The fade is only honest when there is something to fade into: it is drawn once the chips
  // are known to be wider than the row, so a network with three grades gets a plain row.
  const [rowWidth, setRowWidth] = React.useState(0);
  const [contentWidth, setContentWidth] = React.useState(0);
  const overflowing = contentWidth > rowWidth + 1;

  return (
    <Card tone="elevated" padding="none">
      <View
        style={{
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.sm,
          height: tokens.control.lg,
          paddingLeft: tokens.spacing.lg,
          paddingRight: tokens.spacing.xs,
        }}
      >
        <Search size={18} color={tokens.colors.text.muted} />
        <TextInput
          style={[
            styles.input,
            {
              color: tokens.colors.text.primary,
              fontFamily: tokens.fonts.body,
              fontSize: tokens.type.body.fontSize,
            },
          ]}
          placeholder={t('map.searchPlaceholder')}
          placeholderTextColor={tokens.colors.text.muted}
          accessibilityLabel={t('map.searchPlaceholder')}
          value={query}
          onChangeText={onQueryChange}
          autoCapitalize="none"
          autoCorrect={false}
          returnKeyType="search"
        />
        {query.length > 0 ? (
          <IconButton
            icon={<X />}
            onPress={() => onQueryChange('')}
            accessibilityLabel={t('map.clearSearch')}
            variant="plain"
            size="sm"
          />
        ) : null}
      </View>

      {fuels.length > 0 ? (
        <>
          <View
            style={{
              height: StyleSheet.hairlineWidth,
              backgroundColor: tokens.colors.borderSubtle,
            }}
          />
          <View style={styles.fuelRow}>
            <ScrollView
              testID="fuel-row"
              horizontal
              showsHorizontalScrollIndicator={false}
              keyboardShouldPersistTaps="handled"
              onLayout={(event) => setRowWidth(event.nativeEvent.layout.width)}
              onContentSizeChange={(width) => setContentWidth(width)}
              contentContainerStyle={{
                gap: tokens.spacing.sm,
                padding: tokens.spacing.md,
              }}
            >
              {fuels.map((fuel) => (
                <Chip
                  key={fuel}
                  label={fuelLabel(fuel)}
                  selected={fuel === selectedFuel}
                  onPress={() => onSelectFuel(fuel)}
                />
              ))}
            </ScrollView>

            {/* Four stripes of the card's own fill, each more opaque than the last: the
                chips dissolve into the edge of the deck instead of being sliced by it. A
                row that simply ends mid-chip reads as a layout bug, which is what it was
                reported as. No gradient primitive — `expo-linear-gradient` is native, and
                this screen ships over OTA. */}
            {overflowing ? (
              <View testID="fuel-fade" pointerEvents="none" style={styles.fuelFade}>
                {FADE_STEPS.map((opacity) => (
                  <View
                    key={opacity}
                    style={{
                      flex: 1,
                      opacity,
                      backgroundColor: tokens.colors.surfaceElevated,
                    }}
                  />
                ))}
              </View>
            ) : null}
          </View>
        </>
      ) : null}
    </Card>
  );
}

const styles = StyleSheet.create({
  input: {
    flex: 1,
    height: '100%',
    // Android adds its own vertical padding to a TextInput, which pushes the
    // placeholder off the row's centre line.
    paddingVertical: 0,
  },
  fuelRow: {
    position: 'relative',
  },
  fuelFade: {
    position: 'absolute',
    top: 0,
    right: 0,
    bottom: 0,
    width: 34,
    flexDirection: 'row',
  },
});
