import { useMemo } from 'react';
import { View, Text, Pressable, ScrollView, StyleSheet, Modal } from 'react-native';
import { X } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import type { ContractCard } from './contractCard';

type ContractReaderModalProps = {
  /** The card being read; null closes the reader. */
  card: ContractCard | null;
  onClose: () => void;
};

/** The reader: what the customer sees before they sign, and can re-read after. */
export function ContractReaderModal({ card, onClose }: ContractReaderModalProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  // Built where `tokens` is in scope rather than at module level, so a themed value added
  // here later follows a theme switch instead of freezing at the default theme.
  const styles = useMemo(
    () =>
      StyleSheet.create({
        modalOverlay: {
          flex: 1,
          // Scrim colour is `tokens.colors.overlay`, applied at the call site.
          justifyContent: 'center',
          padding: tokens.spacing.xl,
        },
        modalContent: {
          borderRadius: tokens.radius.lg,
          padding: tokens.spacing['2xl'],
          maxHeight: '80%',
        },
        modalHeader: {
          flexDirection: 'row',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: tokens.spacing.xl,
        },
        modalTitle: {
          fontFamily: 'Rajdhani-Bold',
          fontSize: tokens.type.heading.fontSize,
          flex: 1,
          marginRight: tokens.spacing.lg,
        },
        contractTextScroll: {
          marginBottom: tokens.spacing['2xl'],
        },
        // The terms themselves: a document, so it is set as one - readable line height, not
        // the label-sized type the facts use.
        contractText: {
          fontFamily: 'Rajdhani-SemiBold',
          fontSize: tokens.type.caption.fontSize,
          lineHeight: tokens.type.caption.fontSize * 1.6,
        },
        modalCloseBtn: {
          paddingVertical: tokens.spacing.lg,
          borderRadius: tokens.radius.md,
          alignItems: 'center',
        },
        factRow: {
          flexDirection: 'row',
          justifyContent: 'space-between',
          gap: tokens.spacing.md,
        },
        factLabel: {
          fontFamily: 'Rajdhani-SemiBold',
          fontSize: tokens.type.caption.fontSize,
        },
        factValue: {
          fontFamily: 'Rajdhani-SemiBold',
          fontSize: tokens.type.numericSmall.fontSize,
          flexShrink: 1,
          textAlign: 'right',
        },
      }),
    [tokens],
  );

  const close = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    onClose();
  };

  return (
    <Modal visible={!!card} animationType="fade" transparent testID="reader-modal">
      <View style={[styles.modalOverlay, { backgroundColor: tokens.colors.overlay }]}>
        <View style={[styles.modalContent, { backgroundColor: tokens.colors.card }]}>
          <View style={styles.modalHeader}>
            <Text style={[styles.modalTitle, { color: tokens.colors.primary }]}>{card?.title}</Text>
            <Pressable
              testID="close-reader-x"
              onPress={close}
              hitSlop={10}
              accessibilityRole="button"
              accessibilityLabel={t('common.close')}
            >
              <X size={24} color={tokens.colors.text.dim} />
            </Pressable>
          </View>
          <ScrollView style={styles.contractTextScroll}>
            {/* The text of the terms, when the card has one. This is what the customer
                signs, so it is shown in full and above the facts: the facts say which
                agreement this is, the text says what it says. */}
            {card?.text !== undefined &&
              (card.text ? (
                <Text
                  testID="contract-text"
                  style={[
                    styles.contractText,
                    {
                      color: tokens.colors.text.primary,
                      marginBottom: tokens.spacing.lg,
                    },
                  ]}
                >
                  {card.text}
                </Text>
              ) : (
                <Text
                  testID="contract-text-missing"
                  style={[
                    styles.contractText,
                    {
                      color: tokens.colors.text.dim,
                      marginBottom: tokens.spacing.lg,
                    },
                  ]}
                >
                  {t('contracts.noTextPublished')}
                </Text>
              ))}
            {card?.facts.map((fact) => (
              <View key={fact.label} style={[styles.factRow, { marginBottom: tokens.spacing.sm }]}>
                <Text style={[styles.factLabel, { color: tokens.colors.text.dim }]}>
                  {fact.label}
                </Text>
                <Text style={[styles.factValue, { color: tokens.colors.text.primary }]}>
                  {fact.value}
                </Text>
              </View>
            ))}
          </ScrollView>
          <Pressable
            testID="close-reader-button"
            onPress={close}
            style={[styles.modalCloseBtn, { backgroundColor: tokens.colors.primary }]}
          >
            <Text style={{ color: tokens.colors.text.onPrimary, fontFamily: 'Inter-Black' }}>
              {t('contracts.close')}
            </Text>
          </Pressable>
        </View>
      </View>
    </Modal>
  );
}
