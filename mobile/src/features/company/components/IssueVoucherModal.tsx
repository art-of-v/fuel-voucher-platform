import type { Dispatch, SetStateAction } from 'react';
import { ActivityIndicator, Modal, Pressable, ScrollView, Text, View } from 'react-native';
import { Check, CheckSquare, ChevronRight, Square, Ticket, Users, X } from 'lucide-react-native';
import { Haptics } from '../../../core/utils/haptics';
import type { useCompany } from '../hooks/useCompany';
import type { CompanyMemberDto } from '../types';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { styles } from './styles';
import { formatExpirationDate } from '../../../core/utils/formatters';

type Data = ReturnType<typeof useCompany>;

/**
 * What the gift modal is currently pointed at.
 *
 * The roster names a worker and the modal fills up with the whole company pool. The
 * hub's orders branch has no worker to name — an undistributed voucher belongs to the
 * company until somebody holds it — so it opens the same modal on a set of fuel and
 * asks for the recipient inside it (`workerUserId` is null until one is picked, and
 * `label` is the display name the modal's "to …" line shows once it has one).
 */
export interface GiftTarget {
  workerUserId: string | null;
  label: string;
}

type IssueVoucherModalProps = Pick<
  Data,
  'members' | 'giftable' | 'giftGroups' | 'gift' | 'isGifting'
> & {
  allGiftableSelected: boolean;
  giftTarget: GiftTarget | null;
  /** A setter, not a plain callback: picking a recipient fills the target in place. */
  setGiftTarget: Dispatch<SetStateAction<GiftTarget | null>>;
  selected: Set<string>;
  toggleSelectAll: () => void;
  toggleSelected: (id: string) => void;
  memberName: (m: CompanyMemberDto) => string;
};

/** The issue-fuel sheet: pick vouchers, then confirm. */
export function IssueVoucherModal(props: IssueVoucherModalProps) {
  const {
    members,
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
    memberName,
  } = props;
  const tokens = useDesignTokens();
  const { t } = useI18n();
  // Fuel in hand is not enough to issue: the modal also needs a recipient. The orders
  // branch opens it without one, so the sheet asks for it above the voucher list.
  const needsRecipient = !!giftTarget && !giftTarget.workerUserId;
  const canGift = !!giftTarget?.workerUserId && selected.size > 0 && !isGifting;
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
                {/* A modal naming nobody would read as a bug, so while the recipient is
                    still unchosen the line names the fuel's own state instead. */}
                {needsRecipient
                  ? t('company.hub.undistributedShort')
                  : t('company.gift.subtitle', giftTarget?.label ?? '')}
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
            {/* The orders branch opens the modal on fuel, which has no recipient yet,
                so it is asked for here. Once picked the row disappears and the modal
                is the roster's modal again — same list, same selection, same mutation. */}
            {needsRecipient && (
              <View style={{ gap: 8 }}>
                <Text style={[styles.groupHeader, { color: tokens.colors.text.dim }]}>
                  {t('company.members.section')}
                </Text>
                {members.length === 0 ? (
                  <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>
                    {t('company.members.empty')}
                  </Text>
                ) : (
                  members.map((m) => (
                    <Pressable
                      key={m.id}
                      onPress={() => {
                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                        setGiftTarget((prev) =>
                          prev
                            ? { ...prev, workerUserId: m.workerUserId, label: memberName(m) }
                            : prev,
                        );
                      }}
                      style={[
                        styles.voucherPick,
                        {
                          borderColor: tokens.colors.borderLight,
                          backgroundColor: tokens.colors.card,
                        },
                      ]}
                    >
                      <Users size={16} color={tokens.colors.primary} />
                      <View style={{ flex: 1 }}>
                        <Text
                          style={[styles.rowTitle, { color: tokens.colors.text.primary }]}
                          numberOfLines={1}
                        >
                          {memberName(m)}
                        </Text>
                      </View>
                      <ChevronRight size={16} color={tokens.colors.text.muted} />
                    </Pressable>
                  ))
                )}
              </View>
            )}

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
            disabled={!canGift}
            onPress={() => {
              if (!giftTarget?.workerUserId) return;
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
              gift(giftTarget.workerUserId, [...selected]);
            }}
            style={[
              styles.confirmBtn,
              { backgroundColor: tokens.colors.primary },
              !canGift && { opacity: 0.4 },
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
