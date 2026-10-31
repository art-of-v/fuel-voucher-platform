import { useMemo } from 'react';
import { View, Text, Pressable, StyleSheet } from 'react-native';
import { FileText, Eye } from 'lucide-react-native';
import { formatExpirationDate } from '../../../core/utils/formatters';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { NETWORK_NAME } from './contractCard';
import type { UserContract } from '../../../core/types/api';

type SignedContractsListProps = {
  contracts?: UserContract[];
  onOpen: (contract: UserContract) => void;
};

/** The signatures the customer already holds, each readable in full. */
export function SignedContractsList({ contracts, onOpen }: SignedContractsListProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  // Built where `tokens` is in scope rather than at module level, so a themed value added
  // here later follows a theme switch instead of freezing at the default theme.
  const styles = useMemo(
    () =>
      StyleSheet.create({
        providerCard: {
          padding: tokens.spacing.lg,
          borderRadius: tokens.radius.lg,
          borderWidth: 1,
        },
        providerInfo: {
          flexDirection: 'row',
          alignItems: 'center',
        },
        emptyState: {
          alignItems: 'center',
          justifyContent: 'center',
          paddingVertical: tokens.spacing.emptyStateClearance,
        },
      }),
    [tokens],
  );

  return (
    <>
      {contracts?.length === 0 ? (
        <View style={styles.emptyState}>
          <FileText size={48} color={tokens.colors.borderLight} />
          <Text
            style={{
              color: tokens.colors.text.dim,
              marginTop: tokens.spacing.lg,
              textAlign: 'center',
            }}
          >
            {t('contracts.noSignedContracts')}
          </Text>
        </View>
      ) : (
        contracts?.map((sc) => (
          <Pressable
            key={sc.id}
            onPress={() => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
              onOpen(sc);
            }}
            style={[
              styles.providerCard,
              { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
            ]}
          >
            <View style={styles.providerInfo}>
              <FileText size={24} color={tokens.colors.primary} />
              <View style={{ flex: 1, marginLeft: tokens.spacing.md }}>
                <Text
                  style={{
                    color: tokens.colors.text.primary,
                    fontFamily: 'Rajdhani-Bold',
                    fontSize: tokens.type.numericSmall.fontSize,
                  }}
                >
                  {sc.brandName ?? NETWORK_NAME}
                </Text>
                <Text
                  style={{
                    color: tokens.colors.text.dim,
                    fontSize: tokens.type.caption.fontSize,
                  }}
                >
                  {t('contracts.company')}: {sc.legalEntityName ?? '-'}
                </Text>
                <Text
                  style={{
                    color: tokens.colors.text.dim,
                    fontSize: tokens.type.caption.fontSize,
                  }}
                >
                  {t('contracts.signedAt')}: {formatExpirationDate(sc.signedAt)}
                </Text>
              </View>
              <Eye size={20} color={tokens.colors.primary} />
            </View>
          </Pressable>
        ))
      )}
    </>
  );
}
