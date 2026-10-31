import { useMemo } from 'react';
import { Text, StyleSheet } from 'react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { BrandCard } from './BrandCard';
import type { Station } from '../../../core/types/api';

type AvailableContractsListProps = {
  stations?: Station[];
  /** Whether a brand already has a signed contract, by brand id. */
  isSigned: (brandId: string) => boolean;
  onSign: (station: Station) => void;
};

/** Every brand the network offers, each with the state of its contract. */
export function AvailableContractsList({
  stations,
  isSigned,
  onSign,
}: AvailableContractsListProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  // Built where `tokens` is in scope rather than at module level, so a themed value added
  // here later follows a theme switch instead of freezing at the default theme.
  const styles = useMemo(
    () =>
      StyleSheet.create({
        sectionLabel: {
          fontFamily: 'Rajdhani-SemiBold',
          fontSize: tokens.type.caption.fontSize,
          letterSpacing: 2,
          marginBottom: tokens.spacing.sm,
        },
      }),
    [tokens],
  );

  return (
    <>
      <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>
        {t('contracts.selectBrand')}
      </Text>
      {stations?.map((station) => {
        const signed = isSigned(station.id);
        return <BrandCard key={station.id} station={station} signed={signed} onSign={onSign} />;
      })}
    </>
  );
}
