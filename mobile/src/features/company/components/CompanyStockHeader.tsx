import { View, Text } from 'react-native';
import { Building2 } from 'lucide-react-native';
import type { CompanyStock } from '../lib/stock';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';

type CompanyStockHeaderProps = {
  companyStock: CompanyStock;
  currentCompany: { name?: string | null } | null;
  /** Total vouchers already handed out across every worker. */
  distributedCount: number;
};

/**
 * Company-context header: whose stock this is, with pool / distributed / worker
 * counts.
 */
export function CompanyStockHeader({
  companyStock,
  currentCompany,
  distributedCount,
}: CompanyStockHeaderProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  return (
    <View style={{ gap: 12, marginBottom: 16 }}>
      <View
        style={{
          flexDirection: 'row',
          alignItems: 'center',
          gap: 10,
          paddingHorizontal: 14,
          paddingVertical: 12,
          borderRadius: 12,
          borderWidth: 1,
          borderColor: `${tokens.colors.primary}33`,
          backgroundColor: `${tokens.colors.primary}14`,
        }}
      >
        <Building2 size={18} color={tokens.colors.primary} />
        <Text
          allowFontScaling={false}
          numberOfLines={1}
          style={{
            flex: 1,
            fontSize: 15,
            fontFamily: 'Rajdhani-Bold',
            letterSpacing: 0.5,
            color: tokens.colors.text.primary,
          }}
        >
          {currentCompany?.name}
        </Text>
      </View>
      <View style={{ flexDirection: 'row', gap: 8 }}>
        <View
          style={{
            flex: 1,
            backgroundColor: tokens.colors.surfaceSunken,
            borderRadius: 8,
            padding: 10,
            borderWidth: 1,
            borderColor: tokens.colors.borderSubtle,
          }}
        >
          <Text
            allowFontScaling={false}
            style={{
              fontSize: 16,
              fontWeight: '800',
              color: tokens.colors.primary,
              textAlign: 'center',
            }}
          >
            {companyStock.pool.length}
          </Text>
          <Text
            allowFontScaling={false}
            style={{
              fontSize: 9,
              color: tokens.colors.text.muted,
              textAlign: 'center',
              marginTop: 2,
            }}
          >
            {t('codes.stock.poolShort')}
          </Text>
        </View>
        <View
          style={{
            flex: 1,
            backgroundColor: `${tokens.colors.primary}14`,
            borderRadius: 8,
            padding: 10,
            borderWidth: 1,
            borderColor: `${tokens.colors.primary}33`,
          }}
        >
          <Text
            allowFontScaling={false}
            style={{
              fontSize: 16,
              fontWeight: '800',
              color: tokens.colors.primary,
              textAlign: 'center',
            }}
          >
            {distributedCount}
          </Text>
          <Text
            allowFontScaling={false}
            style={{ fontSize: 9, color: tokens.colors.primary, textAlign: 'center', marginTop: 2 }}
          >
            {t('codes.stock.distributedShort')}
          </Text>
        </View>
        <View
          style={{
            flex: 1,
            backgroundColor: `${tokens.colors.accent}14`,
            borderRadius: 8,
            padding: 10,
            borderWidth: 1,
            borderColor: `${tokens.colors.accent}33`,
          }}
        >
          <Text
            allowFontScaling={false}
            style={{
              fontSize: 16,
              fontWeight: '800',
              color: tokens.colors.accent,
              textAlign: 'center',
            }}
          >
            {companyStock.workers.length}
          </Text>
          <Text
            allowFontScaling={false}
            style={{ fontSize: 9, color: tokens.colors.accent, textAlign: 'center', marginTop: 2 }}
          >
            {t('codes.stock.workersShort')}
          </Text>
        </View>
      </View>
    </View>
  );
}
