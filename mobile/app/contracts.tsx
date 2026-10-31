import React, { useEffect, useMemo, useState } from 'react';
import { View, ScrollView, Alert } from 'react-native';
import { useRouter } from 'expo-router';
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
import {
  AvailableContractsList,
  ContractReaderModal,
  ContractTabs,
  NETWORK_NAME,
  SignedContractsList,
  SigningSheet,
  type ContractCard,
  type Tab,
} from '../src/features/contracts/components';

export default function ContractsScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
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

  const handleSignRequest = (station: Station) => {
    setSelectedStation(station);
    // Only this brand's contracts: a contract belongs to a brand, and
    // the card the user pressed is the brand they are signing with.
    const forBrand = (contracts || []).filter((c) => c.brandId === station.id && !c.expired);
    setSigningContract(forBrand.length === 1 ? forBrand[0] : null);
  };

  const Header = <ScreenHeader title={t('contracts.title')} />;

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
        <ContractTabs activeTab={activeTab} onChange={setActiveTab} />

        {activeTab === 'AVAILABLE' ? (
          <View style={{ marginTop: tokens.spacing.xl, gap: tokens.spacing.lg }}>
            <AvailableContractsList
              stations={stations}
              isSigned={isProviderSigned}
              onSign={handleSignRequest}
            />
          </View>
        ) : (
          <View style={{ marginTop: tokens.spacing.xl, gap: tokens.spacing.lg }}>
            <SignedContractsList
              contracts={signedContracts}
              onOpen={(sc) => setReadingCard(cardOfSignature(sc))}
            />
          </View>
        )}
      </ScrollView>

      {/* Contract Reader Modal */}
      <ContractReaderModal card={readingCard} onClose={() => setReadingCard(null)} />

      {/* Signing Sheet Modal */}
      <SigningSheet
        station={selectedStation}
        contract={signingContract}
        contracts={brandContracts}
        signature={signature}
        isPending={signMutation.isPending}
        onSelectContract={setSigningContract}
        onCapture={setSignature}
        onClose={() => {
          setSelectedStation(null);
          setSignature(null);
          setSigningContract(null);
        }}
        onReadFull={() => {
          if (signingContract) setReadingCard(cardOfContract(signingContract));
        }}
        onSubmit={handleSign}
      />
    </GridPageLayout>
  );
}
