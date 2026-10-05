import { ActivityIndicator, Modal, Pressable, ScrollView, Text, View } from 'react-native';
import { Check, CheckSquare, Square, Ticket, X } from 'lucide-react-native';
import { Haptics } from '../../../core/utils/haptics';
import type { useCompany } from '../hooks/useCompany';
import type { CompanyMemberDto } from '../types';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { styles } from './styles';
import { formatExpirationDate } from '../../../core/utils/formatters';

type Data = ReturnType<typeof useCompany>;

type IssueVoucherModalProps = Pick<Data, 'giftable' | 'giftGroups' | 'gift' | 'isGifting'> & {
  allGiftableSelected: boolean;
  giftTarget: CompanyMemberDto | null;
  setGiftTarget: (target: CompanyMemberDto | null) => void;
  selected: Set<string>;
  toggleSelectAll: () => void;
  toggleSelected: (id: string) => void;
  workerLabel: string;
};

/** The issue-fuel sheet: pick vouchers, then confirm. */
export function IssueVoucherModal(props: IssueVoucherModalProps) {
  const {
    giftable,
    giftGroups,
    gift,
    isGifting,
    allGiftableSelected,
    giftTarget,
    setGiftTarget,
    selected,
    toggleSelectAll,
    toggleSelected,
    workerLabel,
  } = props;
  const tokens = useDesignTokens();
  const { t } = useI18n();
  return (
    <Modal
      visible={!!giftTarget}
      transparent
      animationType="slide"
      onRequestClose={() => setGiftTarget(null)}
    >
      <View style={[styles.modalOverlay, { backgroundColor: tokens.colors.overlay }]}>
        <View
          style={[
            styles.modalSheet,
            { backgroundColor: tokens.colors.background, borderColor: tokens.colors.borderLight },
          ]}
        >
          <View style={styles.modalHeader}>
            <View style={{ flex: 1 }}>
              <Text
                style={{
                  color: tokens.colors.text.primary,
                  fontFamily: 'Rajdhani-Bold',
                  fontSize: 20,
                }}
              >
                {t('company.gift.title')}
              </Text>
              <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>
                {t('company.gift.subtitle', workerLabel)}
              </Text>
            </View>
            <Pressable onPress={() => setGiftTarget(null)} style={{ padding: 6 }}>
              <X size={22} color={tokens.colors.text.muted} />
            </Pressable>
          </View>

          <ScrollView
            style={{ maxHeight: 360 }}
            contentContainerStyle={{ gap: 10, paddingVertical: 4 }}
          >
            {giftable.length === 0 ? (
              <Text
                style={[styles.emptyText, { color: tokens.colors.text.dim, paddingVertical: 20 }]}
              >
                {t('company.gift.empty')}
              </Text>
            ) : (
              <>
                <Pressable
                  onPress={toggleSelectAll}
                  style={[
                    styles.voucherPick,
                    {
                      borderColor: tokens.colors.borderLight,
                      backgroundColor: tokens.colors.card,
                    },
                  ]}
                >
                  {allGiftableSelected ? (
                    <CheckSquare size={20} color={tokens.colors.primary} />
                  ) : (
                    <Square size={20} color={tokens.colors.primary} />
                  )}
                  <Text
                    style={{
                      color: tokens.colors.primary,
                      fontFamily: 'Inter-Black',
                      fontSize: 12,
                      letterSpacing: 1,
                    }}
                  >
                    {allGiftableSelected ? t('company.gift.clear') : t('company.gift.selectAll')}
                  </Text>
                </Pressable>
                {giftGroups.map((group) => (
                  <View key={group.provider} style={{ gap: 10 }}>
                    <Text style={[styles.groupHeader, { color: tokens.colors.text.dim }]}>
                      {group.provider}
                    </Text>
                    {group.items.map((v) => {
                      const isSel = selected.has(v.id);
                      return (
                        <Pressable
                          key={v.id}
                          onPress={() => toggleSelected(v.id)}
                          style={[
                            styles.voucherPick,
                            {
                              borderColor: isSel
                                ? tokens.colors.primary
                                : tokens.colors.borderLight,
                              backgroundColor: isSel
                                ? `${tokens.colors.primary}14`
                                : tokens.colors.card,
                            },
                          ]}
                        >
                          <View
                            style={[
                              styles.checkbox,
                              {
                                borderColor: isSel
                                  ? tokens.colors.primary
                                  : tokens.colors.borderLight,
                                backgroundColor: isSel ? tokens.colors.primary : 'transparent',
                              },
                            ]}
                          >
                            {isSel && (
                              <Check
                                size={14}
                                color={tokens.colors.text.onPrimary}
                                strokeWidth={3}
                              />
                            )}
                          </View>
                          <View style={{ flex: 1 }}>
                            <Text
                              style={{
                                color: tokens.colors.text.primary,
                                fontFamily: 'Rajdhani-Bold',
                                fontSize: 15,
                              }}
                              numberOfLines={1}
                            >
                              {v.provider} · {v.amount} {v.unit || t('common.liter')}
                            </Text>
                            <Text
                              style={{ color: tokens.colors.text.dim, fontSize: 11 }}
                              numberOfLines={1}
                            >
                              {v.fuelName || v.fuelType}
                              {v.expirationDate
                                ? ` · ${t('codes.expires')}: ${formatExpirationDate(v.expirationDate)}`
                                : ''}
                            </Text>
                          </View>
                        </Pressable>
                      );
                    })}
                  </View>
                ))}
              </>
            )}
          </ScrollView>

          <Pressable
            disabled={selected.size === 0 || isGifting}
            onPress={() => {
              if (!giftTarget) return;
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
              gift(giftTarget.workerUserId, [...selected]);
            }}
            style={[
              styles.confirmBtn,
              { backgroundColor: tokens.colors.primary },
              (selected.size === 0 || isGifting) && { opacity: 0.4 },
            ]}
          >
            {isGifting ? (
              <ActivityIndicator size="small" color={tokens.colors.text.onPrimary} />
            ) : (
              <>
                <Ticket size={18} color={tokens.colors.text.onPrimary} />
                <Text
                  style={{
                    color: tokens.colors.text.onPrimary,
                    fontFamily: 'Inter-Black',
                    fontSize: 13,
                    letterSpacing: 1,
                  }}
                >
                  {selected.size === 0
                    ? t('company.gift.confirmZero')
                    : t('company.gift.confirm', String(selected.size))}
                </Text>
              </>
            )}
          </Pressable>
        </View>
      </View>
    </Modal>
  );
}
