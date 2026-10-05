import { View, Text } from 'react-native';
import { Briefcase } from 'lucide-react-native';
import type { Voucher } from '../../../core/types/api';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';

type WorkerFuelHeaderProps = {
  currentCompany: { name?: string | null } | null;
  /** Everything this company issued to the signed-in worker. */
  workerIssued: Voucher[];
  workerUsedCount: number;
  workerLeftCount: number;
  workerLitersLeft: number;
};

/**
 * Worker-context header (multi-company epic #103 S5): the fuel this company issued
 * to me — issued / used / remaining, then a flat list. No pool, no other workers, no
 * orders: those belong to the employer, not to me.
 */
export function WorkerFuelHeader({
  currentCompany,
  workerIssued,
  workerUsedCount,
  workerLeftCount,
  workerLitersLeft,
}: WorkerFuelHeaderProps) {
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
          borderColor: `${tokens.colors.accent}33`,
          backgroundColor: `${tokens.colors.accent}14`,
        }}
      >
        <Briefcase size={18} color={tokens.colors.accent} />
        <View style={{ flex: 1 }}>
          <Text
            allowFontScaling={false}
            numberOfLines={1}
            style={{
              fontSize: 15,
              fontFamily: 'Rajdhani-Bold',
              letterSpacing: 0.5,
              color: tokens.colors.text.primary,
            }}
          >
            {currentCompany?.name}
          </Text>
          <Text
            allowFontScaling={false}
            numberOfLines={1}
            style={{ fontSize: 11, fontFamily: 'Inter-Medium', color: tokens.colors.text.dim }}
          >
            {t('codes.stock.workerIssuedBy')}
          </Text>
        </View>
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
              color: tokens.colors.text.primary,
              textAlign: 'center',
            }}
          >
            {workerIssued.length}
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
            {t('codes.stock.issuedShort')}
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
            {workerLeftCount}
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
            {t('codes.stock.leftShort')}
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
            {workerUsedCount}
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
            {t('codes.stock.usedShort')}
          </Text>
        </View>
      </View>
      {workerLitersLeft > 0 && (
        <Text
          allowFontScaling={false}
          style={{ fontSize: 12, fontFamily: 'Inter', color: tokens.colors.text.dim }}
        >
          {workerLitersLeft} {t('common.liter')} · {t('codes.stock.workerNoBuying')}
        </Text>
      )}
    </View>
  );
}
