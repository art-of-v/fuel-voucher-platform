import React, { useEffect, useMemo, useState } from 'react';
import { View, Text, Pressable, ScrollView, StyleSheet, Alert, Modal } from 'react-native';
import { useRouter } from 'expo-router';
import { FileText, CheckCircle2, Eye, PenTool, X, Landmark } from 'lucide-react-native';
import { formatExpirationDate } from '../src/core/utils/formatters';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getAvailableContracts,
  getSignedContracts,
} from '../src/features/contracts/api/getContracts';
import { signContract } from '../src/features/contracts/api/signContract';
import { getLegalProfile } from '../src/features/profile/api/updateLegalProfile';
import { getStations } from '../src/features/stations/api/getStations';
import type { Station, Contract, UserContract } from '../src/core/types/api';
import { GridPageLayout, LoadingState, ScreenHeader } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { SignaturePad } from '../src/components/SignaturePad';

type Tab = 'AVAILABLE' | 'SIGNED';

/**
 * What the reader shows about a contract.
 *
 * The reader is where the user looks before they sign, so what it shows has to be true: the
 * facts (which brand, which company, which version of the terms, the term it runs for) AND
 * the text of the terms itself. The text lives once per provider and is versioned, so the
 * server sends the version's text with the contract - `text` is null when that version has no
 * text yet, and absent when the card is about something else (a signature, which carries no
 * text of its own).
 */
type ContractCard = {
  title: string;
  facts: { label: string; value: string }[];
  /** The text the customer reads before signing. Null: the version has no text. */
  text?: string | null;
};

/** The name the network is shown under when a contract's brand does not resolve. */
const NETWORK_NAME = 'FuelFlow Network';

export default function ContractsScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();

  // Built where `tokens` is in scope rather than at module level. The values below
  // are all theme-independent, but a themed colour added here later would silently
  // freeze at the default theme instead of following a switch.
  const styles = useMemo(
    () =>
      StyleSheet.create({
        tabContainer: {
          flexDirection: 'row',
          padding: tokens.spacing.xs,
          borderRadius: tokens.radius.md,
          borderWidth: 1,
          gap: tokens.spacing.xs,
        },
        tab: {
          flex: 1,
          paddingVertical: tokens.spacing.fine10,
          alignItems: 'center',
          justifyContent: 'center',
        },
        tabText: {
          fontFamily: 'Inter-Black',
          fontSize: tokens.type.caption.fontSize,
          letterSpacing: 1,
        },
        sectionLabel: {
          fontFamily: 'Rajdhani-SemiBold',
          fontSize: tokens.type.caption.fontSize,
          letterSpacing: 2,
          marginBottom: tokens.spacing.sm,
        },
        providerCard: {
          padding: tokens.spacing.lg,
          borderRadius: tokens.radius.lg,
          borderWidth: 1,
        },
        providerInfo: {
          flexDirection: 'row',
          alignItems: 'center',
        },
        providerIcon: {
          width: 44,
          height: 44,
          borderRadius: tokens.radius.full,
          borderWidth: 1.5,
          alignItems: 'center',
          justifyContent: 'center',
          marginRight: tokens.spacing.lg,
        },
        actionBtn: {
          width: 40,
          height: 40,
          borderRadius: tokens.radius.xl,
          alignItems: 'center',
          justifyContent: 'center',
        },
        emptyState: {
          alignItems: 'center',
          justifyContent: 'center',
          paddingVertical: tokens.spacing.emptyStateClearance,
        },
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
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const [activeTab, setActiveTab] = useState<Tab>('AVAILABLE');
  const [readingCard, setReadingCard] = useState<ContractCard | null>(null);
  const [selectedStation, setSelectedStation] = useState<Station | null>(null);
  const [signature, setSignature] = useState<string | null>(null);
  const [signingContract, setSigningContract] = useState<Contract | null>(null);

  // Queries
  const { data: stations, isLoading: loadingStations } = useQuery({
    queryKey: ['stations'],
    queryFn: getStations,
  });

  const { data: contracts, isLoading: loadingContracts } = useQuery({
    queryKey: ['contracts'],
    queryFn: getAvailableContracts,
  });

  const { data: signedContracts, isLoading: loadingSigned } = useQuery({
    queryKey: ['signedContracts'],
    queryFn: getSignedContracts,
  });

  // Safety check: ensure company profile exists
  useEffect(() => {
    getLegalProfile()
      .then((company) => {
        if (!company) {
          Alert.alert(t('contracts.needProfile'), t('contracts.needProfileDesc'));
          router.replace('/profile');
        }
      })
      .catch(console.error);
  }, [router, t]);

  const isLoading = loadingStations || loadingContracts || loadingSigned;

  // Mutations
  const signMutation = useMutation({
    // One contract per call, and the body the server binds: `{ contractId, signatureData }`.
    // The client used to post an array of ids and a station, which bound nothing.
    mutationFn: ({ contractId, sig }: { contractId: string; sig: string }) =>
      signContract(contractId, sig),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['signedContracts'] });
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      Alert.alert(t('contracts.success'), t('contracts.signedSuccess'));
      setSigningContract(null);
      setSelectedStation(null);
      setSignature(null);
      setActiveTab('SIGNED');
    },
    onError: (err: any) => {
      Alert.alert(t('contracts.error'), err.message || t('contracts.signFailed'));
    },
  });

  // Helpers
  // The brand travels by id, so that is what a screen matches on: two brands may share a name.
  const isProviderSigned = (brandId: string) =>
    signedContracts?.some((sc) => sc.brandId === brandId) ?? false;

  // The contracts of the brand whose card was pressed. A contract belongs to a brand, so the
  // sheet offers THAT brand's contracts - not every contract the user happens to hold, which
  // is how a signature could end up on a document for a different provider. An expired
  // contract is not offered either: the server refuses to sign one, so offering it would
  // only produce a rejection after the user had drawn their signature.
  const brandContracts = useMemo(
    () =>
      selectedStation
        ? (contracts || []).filter((c) => c.brandId === selectedStation.id && !c.expired)
        : ([] as Contract[]),
    [contracts, selectedStation],
  );

  // Two contracts of one brand are told apart by their term, so the option carries it.
  const contractLabel = (c: Contract) =>
    `${c.brandName ?? NETWORK_NAME} · ${formatExpirationDate(c.expiresAt)}`;

  const cardOfContract = (c: Contract): ContractCard => ({
    title: c.brandName ?? NETWORK_NAME,
    facts: [
      { label: t('contracts.company'), value: c.legalEntityName ?? '-' },
      { label: t('contracts.createdAt'), value: formatExpirationDate(c.createdAt) },
      { label: t('contracts.expiresAt'), value: formatExpirationDate(c.expiresAt) },
      { label: t('contracts.termsVersion'), value: String(c.termsVersion ?? 1) },
    ],
    // The text the customer is asked to read before signing. It is the provider's text,
    // versioned, and the contract says which version it is against - so this is the words
    // that will be signed, not a summary of them.
    text: c.termsText ?? null,
  });

  const cardOfSignature = (sc: UserContract): ContractCard => ({
    title: sc.brandName ?? NETWORK_NAME,
    facts: [
      { label: t('contracts.signedAt'), value: formatExpirationDate(sc.signedAt) },
      {
        label: t('contracts.expiresAt'),
        value: sc.expiresAt ? formatExpirationDate(sc.expiresAt) : '-',
      },
    ],
  });

  const handleSign = () => {
    if (!selectedStation || !signingContract || !signature) {
      Alert.alert(t('contracts.warning'), t('contracts.signValidation'));
      return;
    }

    signMutation.mutate({ contractId: signingContract.id, sig: signature });
  };

  const Header = <ScreenHeader title={t('contracts.title')} />;

  const TabSwitch = (
    <View
      style={[
        styles.tabContainer,
        { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
      ]}
    >
      {(['AVAILABLE', 'SIGNED'] as Tab[]).map((tab) => (
        <Pressable
          key={tab}
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            setActiveTab(tab);
          }}
          style={[
            styles.tab,
            activeTab === tab && {
              backgroundColor: tokens.colors.primary,
              borderRadius: tokens.radius.md,
            },
          ]}
        >
          <Text
            style={[
              styles.tabText,
              { color: activeTab === tab ? tokens.colors.text.onPrimary : tokens.colors.text.dim },
            ]}
          >
            {tab === 'AVAILABLE' ? t('contracts.available') : t('contracts.signed')}
          </Text>
        </Pressable>
      ))}
    </View>
  );

  if (isLoading) {
    // Inside `PageLayout` so the header, background and safe areas survive the
    // load. `disableScroll` because `LoadingState fullScreen` is `flex: 1` and
    // needs a fixed-height parent to centre itself in.
    return (
      <GridPageLayout header={Header} disableScroll>
        <LoadingState fullScreen />
      </GridPageLayout>
    );
  }

  return (
    <GridPageLayout header={Header} disableScroll>
      <ScrollView
        style={{ flex: 1 }}
        contentContainerStyle={{
          paddingHorizontal: tokens.spacing.xl,
          paddingTop: tokens.spacing.fine10,
        }}
      >
        {TabSwitch}

        {activeTab === 'AVAILABLE' ? (
          <View style={{ marginTop: tokens.spacing.xl, gap: tokens.spacing.lg }}>
            <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>
              {t('contracts.selectBrand')}
            </Text>
            {stations?.map((station) => {
              const signed = isProviderSigned(station.id);
              return (
                <View
                  key={station.id}
                  style={[
                    styles.providerCard,
                    { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
                  ]}
                >
                  <View style={styles.providerInfo}>
                    <View
                      style={[
                        styles.providerIcon,
                        { backgroundColor: `${station.color}22`, borderColor: station.color },
                      ]}
                    >
                      <Landmark size={20} color={station.color} />
                    </View>
                    <View style={{ flex: 1 }}>
                      <Text
                        style={{
                          color: tokens.colors.text.primary,
                          fontFamily: 'Rajdhani-Bold',
                          fontSize: tokens.type.heading.fontSize,
                        }}
                      >
                        {station.name}
                      </Text>
                      <Text
                        style={{
                          color: tokens.colors.text.dim,
                          fontSize: tokens.type.caption.fontSize,
                        }}
                      >
                        {signed ? t('contracts.signedStatus') : t('contracts.needsSignature')}
                      </Text>
                    </View>
                    {signed ? (
                      <CheckCircle2 size={24} color={tokens.colors.primary} />
                    ) : (
                      <Pressable
                        onPress={() => {
                          Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                          setSelectedStation(station);
                          // Only this brand's contracts: a contract belongs to a brand, and
                          // the card the user pressed is the brand they are signing with.
                          const forBrand = (contracts || []).filter(
                            (c) => c.brandId === station.id && !c.expired,
                          );
                          setSigningContract(forBrand.length === 1 ? forBrand[0] : null);
                        }}
                        style={[styles.actionBtn, { backgroundColor: tokens.colors.primary }]}
                      >
                        <PenTool size={16} color={tokens.colors.text.onPrimary} />
                      </Pressable>
                    )}
                  </View>
                </View>
              );
            })}
          </View>
        ) : (
          <View style={{ marginTop: tokens.spacing.xl, gap: tokens.spacing.lg }}>
            {signedContracts?.length === 0 ? (
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
              signedContracts?.map((sc) => (
                <Pressable
                  key={sc.id}
                  onPress={() => setReadingCard(cardOfSignature(sc))}
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
          </View>
        )}
      </ScrollView>

      {/* Contract Reader Modal */}
      <Modal visible={!!readingCard} animationType="fade" transparent testID="reader-modal">
        <View style={[styles.modalOverlay, { backgroundColor: tokens.colors.overlay }]}>
          <View style={[styles.modalContent, { backgroundColor: tokens.colors.card }]}>
            <View style={styles.modalHeader}>
              <Text style={[styles.modalTitle, { color: tokens.colors.primary }]}>
                {readingCard?.title}
              </Text>
              <Pressable testID="close-reader-x" onPress={() => setReadingCard(null)}>
                <X size={24} color={tokens.colors.text.dim} />
              </Pressable>
            </View>
            <ScrollView style={styles.contractTextScroll}>
              {/* The text of the terms, when the card has one. This is what the customer
                  signs, so it is shown in full and above the facts: the facts say which
                  agreement this is, the text says what it says. */}
              {readingCard?.text !== undefined &&
                (readingCard.text ? (
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
                    {readingCard.text}
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
              {readingCard?.facts.map((fact) => (
                <View
                  key={fact.label}
                  style={[styles.factRow, { marginBottom: tokens.spacing.sm }]}
                >
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
              onPress={() => setReadingCard(null)}
              style={[styles.modalCloseBtn, { backgroundColor: tokens.colors.primary }]}
            >
              <Text style={{ color: tokens.colors.text.onPrimary, fontFamily: 'Inter-Black' }}>
                {t('contracts.close')}
              </Text>
            </Pressable>
          </View>
        </View>
      </Modal>

      {/* Signing Sheet Modal */}
      <Modal visible={!!selectedStation} animationType="slide" transparent>
        <View style={[styles.signingOverlay, { backgroundColor: tokens.colors.overlay }]}>
          <View style={[styles.signingSheet, { backgroundColor: tokens.colors.background }]}>
            <View style={[styles.sheetHeader, { borderBottomColor: tokens.colors.borderLight }]}>
              <View style={{ flex: 1, paddingRight: tokens.spacing.md }}>
                <Text
                  style={[styles.sheetTitle, { color: tokens.colors.primary }]}
                  numberOfLines={1}
                >
                  {signingContract ? contractLabel(signingContract) : t('contracts.signingTitle')}
                </Text>
                <Text
                  style={{ color: tokens.colors.text.dim, marginTop: tokens.spacing.xxs }}
                  numberOfLines={1}
                >
                  {t('contracts.brand')}: {selectedStation?.name}
                </Text>
              </View>
              <Pressable
                testID="close-signing-x"
                onPress={() => {
                  setSelectedStation(null);
                  setSignature(null);
                  setSigningContract(null);
                }}
              >
                <X size={24} color={tokens.colors.text.dim} />
              </Pressable>
            </View>

            <ScrollView
              style={styles.sheetScroll}
              contentContainerStyle={styles.sheetScrollContent}
            >
              {brandContracts.length > 1 && (
                <View style={{ marginBottom: tokens.spacing.lg, gap: tokens.spacing.sm }}>
                  {brandContracts.map((c) => {
                    const selected = signingContract?.id === c.id;
                    return (
                      <Pressable
                        key={c.id}
                        onPress={() => {
                          Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                          setSigningContract(c);
                        }}
                        style={[
                          styles.contractOption,
                          {
                            borderColor: selected
                              ? tokens.colors.primary
                              : tokens.colors.borderLight,
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

              {signingContract && (
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
                    {contractLabel(signingContract)}
                  </Text>
                  <Text style={[styles.summaryProvider, { color: tokens.colors.text.dim }]}>
                    {t('contracts.brand')}: {selectedStation?.name}
                  </Text>
                </View>
              )}

              {brandContracts.length === 0 && (
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
                onPress={() => signingContract && setReadingCard(cardOfContract(signingContract))}
                disabled={!signingContract}
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
              <SignaturePad onCapture={setSignature} />

              <Pressable
                testID="sign-submit"
                onPress={handleSign}
                disabled={signMutation.isPending || !signature || !signingContract}
                style={[
                  styles.signSubmitBtn,
                  { backgroundColor: tokens.colors.primary },
                  (signMutation.isPending || !signature || !signingContract) && { opacity: 0.5 },
                ]}
              >
                <Text
                  style={{
                    color: tokens.colors.text.onPrimary,
                    fontFamily: 'Inter-Black',
                    fontSize: tokens.type.numericSmall.fontSize,
                  }}
                >
                  {signMutation.isPending ? t('contracts.signing') : t('contracts.signAndConfirm')}
                </Text>
              </Pressable>
            </View>
          </View>
        </View>
      </Modal>
    </GridPageLayout>
  );
}
