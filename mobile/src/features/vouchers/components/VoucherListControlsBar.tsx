import { View } from 'react-native';
import { Chip } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import {
  applyListControls,
  availableFacets,
  countActiveFilters,
  toggleSort,
  type ListControls,
  type VoucherSortKey,
} from '../lib/listControls';
import type { Voucher } from '../../../core/types/api';

type VoucherListControlsBarProps = {
  vouchers: Voucher[];
  controls: ListControls;
  onChange: (controls: ListControls) => void;
  /** Label prefix for the "N shown" affordance. */
  countLabel?: string;
};

/**
 * Sort and filter controls for a flat voucher list (#161 H0).
 *
 * A worker holds a handful of vouchers, so the list stays flat - but once there are more than a
 * couple, "which of mine expires next" needs a control rather than a scroll. The same bar serves the
 * owner-side lists: the issue asks for the primitives to be reused, and sharing the component is how
 * that actually happens.
 *
 * Purely presentational - all of the logic lives in `listControls.ts`, which is where the tests are.
 */
export function VoucherListControlsBar({
  vouchers,
  controls,
  onChange,
  countLabel,
}: VoucherListControlsBarProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  const facets = availableFacets(vouchers);
  const visible = applyListControls(vouchers, controls);
  const activeFilters = countActiveFilters(controls.filters);
  const hidden = vouchers.length - visible.length;

  const setFilter = (patch: Partial<ListControls['filters']>) =>
    onChange({ ...controls, filters: { ...controls.filters, ...patch } });

  const sortKeys: VoucherSortKey[] = ['expiry', 'provider', 'fuel', 'liters'];
  const sortLabels: Record<VoucherSortKey, string> = {
    expiry: t('voucherList.sort.expiry'),
    provider: t('voucherList.sort.provider'),
    fuel: t('voucherList.sort.fuel'),
    liters: t('voucherList.sort.liters'),
  };

  return (
    <View style={{ gap: 8, paddingVertical: 8 }}>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 8 }}>
        {sortKeys.map((key) => (
          <Chip
            key={key}
            testID={`sort-${key}`}
            label={`${sortLabels[key]}${
              controls.sortKey === key ? (controls.sortDirection === 'asc' ? ' ↑' : ' ↓') : ''
            }`}
            selected={controls.sortKey === key}
            onPress={() => onChange(toggleSort(controls, key))}
          />
        ))}
      </View>

      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 8 }}>
        <Chip
          testID="filter-not-expired"
          label={t('voucherList.filter.notExpired')}
          selected={controls.filters.hideExpired}
          onPress={() => setFilter({ hideExpired: !controls.filters.hideExpired })}
        />

        {facets.providers.map((provider) => (
          <Chip
            key={`provider-${provider}`}
            testID={`filter-provider-${provider}`}
            label={provider}
            count={
              controls.filters.provider === provider
                ? vouchers.filter((v) => v.provider === provider).length
                : undefined
            }
            selected={controls.filters.provider === provider}
            onPress={() =>
              setFilter({ provider: controls.filters.provider === provider ? undefined : provider })
            }
          />
        ))}

        {facets.fuels.map((fuel) => (
          <Chip
            key={`fuel-${fuel}`}
            testID={`filter-fuel-${fuel}`}
            label={fuel}
            selected={controls.filters.fuel === fuel}
            onPress={() => setFilter({ fuel: controls.filters.fuel === fuel ? undefined : fuel })}
          />
        ))}
      </View>

      {/* The count is the whole point of a filter: without it a worker cannot tell a filtered list
          from an empty one. */}
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12 }}>
        <Chip
          testID="list-count"
          label={`${countLabel ?? t('voucherList.count')} ${visible.length}/${vouchers.length}`}
          disabled
        />
        {activeFilters > 0 && (
          <Chip
            testID="clear-filters"
            label={`${t('voucherList.clear')} (${activeFilters})`}
            onPress={() =>
              onChange({
                ...controls,
                filters: {
                  ...controls.filters,
                  provider: undefined,
                  fuel: undefined,
                  status: undefined,
                  hideExpired: false,
                },
              })
            }
          />
        )}
      </View>

      {hidden > 0 && <Chip label={`${hidden} ${t('voucherList.hidden')}`} disabled />}

      {/* Spacer so the bar does not butt against the list; keeps the visual rhythm of the screen. */}
      <View style={{ height: tokens.spacing.xs }} />
    </View>
  );
}
