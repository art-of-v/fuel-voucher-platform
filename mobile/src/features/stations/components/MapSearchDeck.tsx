import React from 'react';
import { ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { Fuel, Search, X } from 'lucide-react-native';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Card, Chip, IconButton } from '../../../core/ui';
import { fuelLabel } from '../lib/fuelLabels';

interface Props {
  query: string;
  onQueryChange: (query: string) => void;
  /** Canonical fuel ids, in the order the packages first list them. */
  fuels: string[];
  selectedFuel: string;
  onSelectFuel: (fuel: string) => void;
}

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
 */
export function MapSearchDeck({ query, onQueryChange, fuels, selectedFuel, onSelectFuel }: Props) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);

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
          <ScrollView
            horizontal
            showsHorizontalScrollIndicator={false}
            keyboardShouldPersistTaps="handled"
            contentContainerStyle={{
              gap: tokens.spacing.sm,
              padding: tokens.spacing.md,
            }}
          >
            {fuels.map((fuel) => (
              <Chip
                key={fuel}
                label={fuelLabel(fuel)}
                icon={<Fuel />}
                selected={fuel === selectedFuel}
                onPress={() => onSelectFuel(fuel)}
              />
            ))}
          </ScrollView>
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
});
