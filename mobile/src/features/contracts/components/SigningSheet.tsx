import { useMemo } from 'react';
import { View, Text, Pressable, ScrollView, StyleSheet, Modal } from 'react-native';
import { Eye, X } from 'lucide-react-native';
import { formatExpirationDate } from '../../../core/utils/formatters';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { SignaturePad } from '../../../components/SignaturePad';
import { NETWORK_NAME } from './contractCard';
import type { Station, Contract } from '../../../core/types/api';

type SigningSheetProps = {
  /** The brand being signed with; null closes the sheet. */
  station: Station | null;
  /** The contract that will be signed, once one is chosen. */
  contract: Contract | null;
  /** The contracts of that brand that are still in term. */
  contracts: Contract[];
  /** The captured signature, or null while the pad is blank. */
  signature: string | null;
  /** Whether a signature is in flight. */
  isPending: boolean;
  onSelectContract: (contract: Contract) => void;
  onCapture: (signature: string | null) => void;
  onClose: () => void;
  onReadFull: () => void;
  onSubmit: () => void;
};

// Two contracts of one brand are told apart by their term, so the option carries it.
const contractLabel = (c: Contract) =>
  `${c.brandName ?? NETWORK_NAME} · ${formatExpirationDate(c.expiresAt)}`;

/** The sheet a signature is drawn in: pick the document, read it, sign it. */
export function SigningSheet({
  station,
  contract,
  contracts,
  signature,
  isPending,
  onSelectContract,
  onCapture,
  onClose,
  onReadFull,
  onSubmit,
}: SigningSheetProps) {
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
        signingOverlay: {
          flex: 1,
          // Scrim colour is `tokens.colors.overlay`, applied at the call site. This
          // screen previously used two *different* black alphas for its two modals.
          justifyContent: 'flex-end',
        },
        signingSheet: {
          borderTopLeftRadius: 20,
          borderTopRightRadius: 20,
          height: '90%',
        },
        sheetHeader: {
          flexDirection: 'row',
          justifyContent: 'space-between',
          padding: tokens.spacing.xl,
          borderBottomWidth: 1,
        },
        sheetTitle: {
          fontFamily: 'Rajdhani-Bold',
          fontSize: tokens.type.numeric.fontSize,
        },
        readFullBtn: {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.md,
          padding: tokens.spacing.lg,
          borderWidth: 1,
          borderRadius: tokens.radius.lg,
          borderStyle: 'dashed',
        },
        signSubmitBtn: {
          marginTop: tokens.spacing['3xl'],
          paddingVertical: tokens.spacing.fine18,
          borderRadius: tokens.radius.fine12,
          alignItems: 'center',
          justifyContent: 'center',
        },
        sheetScroll: {
          maxHeight: 180,
          flexShrink: 1,
        },
        sheetScrollContent: {
          padding: tokens.spacing.xl,
        },
        signatureSection: {
          paddingHorizontal: tokens.spacing.xl,
          paddingBottom: tokens.spacing['2xl'],
        },
        contractSummary: {
          borderWidth: 1,
          borderRadius: tokens.radius.lg,
          padding: tokens.spacing.lg,
          marginBottom: tokens.spacing.lg,
        },
        summaryTitle: {
          fontFamily: 'Rajdhani-Bold',
          fontSize: tokens.type.numericSmall.fontSize,
        },
        summaryProvider: {
          fontSize: tokens.type.caption.fontSize,
          marginTop: tokens.spacing.xs,
        },
        contractOption: {
          borderWidth: 1,
          borderRadius: tokens.radius.lg,
          paddingVertical: tokens.spacing.md,
          paddingHorizontal: tokens.spacing.fine14,
        },
        contractOptionText: {
          fontFamily: 'Rajdhani-SemiBold',
          fontSize: tokens.type.numericSmall.fontSize,
        },
      }),
    [tokens],
  );

  return (
    <Modal visible={!!station} animationType="slide" transparent>
      <View style={[styles.signingOverlay, { backgroundColor: tokens.colors.overlay }]}>
        <View style={[styles.signingSheet, { backgroundColor: tokens.colors.background }]}>
          <View style={[styles.sheetHeader, { borderBottomColor: tokens.colors.borderLight }]}>
            <View style={{ flex: 1, paddingRight: tokens.spacing.md }}>
              <Text style={[styles.sheetTitle, { color: tokens.colors.primary }]} numberOfLines={1}>
                {contract ? contractLabel(contract) : t('contracts.signingTitle')}
              </Text>
              <Text
                style={{ color: tokens.colors.text.dim, marginTop: tokens.spacing.xxs }}
                numberOfLines={1}
              >
                {t('contracts.brand')}: {station?.name}
              </Text>
            </View>
            <Pressable
              testID="close-signing-x"
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                onClose();
              }}
              hitSlop={10}
              accessibilityRole="button"
              accessibilityLabel={t('common.close')}
            >
              <X size={24} color={tokens.colors.text.dim} />
            </Pressable>
          </View>

          <ScrollView style={styles.sheetScroll} contentContainerStyle={styles.sheetScrollContent}>
            {contracts.length > 1 && (
              <View style={{ marginBottom: tokens.spacing.lg, gap: tokens.spacing.sm }}>
                {contracts.map((c) => {
                  const selected = contract?.id === c.id;
                  return (
                    <Pressable
                      key={c.id}
                      onPress={() => {
                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                        onSelectContract(c);
                      }}
                      style={[
                        styles.contractOption,
                        {
                          borderColor: selected ? tokens.colors.primary : tokens.colors.borderLight,
                        },
                        selected && { backgroundColor: `${tokens.colors.primary}14` },
                      ]}
                    >
                      <Text
                        style={[
                          styles.contractOptionText,
                          {
                            color: selected ? tokens.colors.primary : tokens.colors.text.primary,
                          },
                        ]}
                        numberOfLines={2}
                      >
                        {contractLabel(c)}
                      </Text>
                    </Pressable>
                  );
                })}
              </View>
            )}

            {contract && (
              <View
                style={[
                  styles.contractSummary,
                  { borderColor: tokens.colors.borderLight, backgroundColor: tokens.colors.card },
                ]}
              >
                <Text
                  style={[styles.summaryTitle, { color: tokens.colors.text.primary }]}
                  numberOfLines={2}
                >
                  {contractLabel(contract)}
                </Text>
                <Text style={[styles.summaryProvider, { color: tokens.colors.text.dim }]}>
                  {t('contracts.brand')}: {station?.name}
                </Text>
              </View>
            )}

            {contracts.length === 0 && (
              <Text style={{ color: tokens.colors.text.dim, marginTop: tokens.spacing.lg }}>
                {t('contracts.noContractForBrand')}
              </Text>
            )}

            <Text
              style={[
                styles.sectionLabel,
                { color: tokens.colors.text.dim, marginTop: tokens.spacing.lg },
              ]}
            >
              {t('contracts.reviewText')}
            </Text>
            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                onReadFull();
              }}
              disabled={!contract}
              style={[styles.readFullBtn, { borderColor: tokens.colors.primary }]}
            >
              <Eye size={16} color={tokens.colors.primary} />
              <Text style={{ color: tokens.colors.primary, fontFamily: 'Rajdhani-Bold' }}>
                {t('contracts.readFull')}
              </Text>
            </Pressable>
          </ScrollView>

          <View style={styles.signatureSection}>
            <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>
              {t('contracts.yourSignature')}
            </Text>
            <SignaturePad onCapture={onCapture} />

            <Pressable
              testID="sign-submit"
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                onSubmit();
              }}
              disabled={isPending || !signature || !contract}
              style={[
                styles.signSubmitBtn,
                { backgroundColor: tokens.colors.primary },
                (isPending || !signature || !contract) && { opacity: 0.5 },
              ]}
            >
              <Text
                style={{
                  color: tokens.colors.text.onPrimary,
                  fontFamily: 'Inter-Black',
                  fontSize: tokens.type.numericSmall.fontSize,
                }}
              >
                {isPending ? t('contracts.signing') : t('contracts.signAndConfirm')}
              </Text>
            </Pressable>
          </View>
        </View>
      </View>
    </Modal>
  );
}
