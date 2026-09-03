import React, { useState, useEffect } from 'react';
import { View, Text, Pressable, ScrollView, StyleSheet, Alert, Modal } from 'react-native';
import { useRouter } from 'expo-router';
import { FileText, CheckCircle2, Eye, PenTool, X, Landmark } from 'lucide-react-native';
import { formatExpirationDate } from '../src/core/utils/formatters';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAvailableContracts, getSignedContracts } from '../src/features/contracts/api/getContracts';
import { signContracts } from '../src/features/contracts/api/signContract';
import { getLegalProfile } from '../src/features/profile/api/updateLegalProfile';
import { getStations } from '../src/features/stations/api/getStations';
import type { Station, Contract } from '../src/core/types/api';
import { PageLayout } from '../src/components/page-layout';
import { LoadingState, ScreenHeader } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { SignaturePad } from '../src/components/SignaturePad';

type Tab = 'AVAILABLE' | 'SIGNED';

export default function ContractsScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const [activeTab, setActiveTab] = useState<Tab>('AVAILABLE');
  const [readingContract, setReadingContract] = useState<Contract | null>(null);
  const [selectedStation, setSelectedStation] = useState<Station | null>(null);
  const [signature, setSignature] = useState<string | null>(null);
  const [signingContract, setSigningContract] = useState<Contract | null>(null);

  // Queries
  const { data: stations, isLoading: loadingStations } = useQuery({
    queryKey: ['stations'],
    queryFn: getStations
  });

  const { data: contracts, isLoading: loadingContracts } = useQuery({
    queryKey: ['contracts'],
    queryFn: getAvailableContracts
  });

  const { data: signedContracts, isLoading: loadingSigned } = useQuery({
    queryKey: ['signedContracts'],
    queryFn: getSignedContracts
  });

  // Safety check: ensure company profile exists
  useEffect(() => {
    getLegalProfile().then((company) => {
      if (!company) {
        Alert.alert(t('contracts.needProfile'), t('contracts.needProfileDesc'));
        router.replace('/profile');
      }
    }).catch(console.error);
  }, []);

  const isLoading = loadingStations || loadingContracts || loadingSigned;

  // Mutations
  const signMutation = useMutation({
    mutationFn: ({ contractIds, sig, stationId }: { contractIds: string[], sig: string, stationId?: string }) => 
      signContracts(contractIds, sig, stationId),
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
    }
  });

  // Helpers
  const isProviderSigned = (stationId: string) => {
    return signedContracts?.some(sc => sc.station?.id === stationId);
  };

  const handleSign = () => {
    if (!selectedStation || !signingContract || !signature) {
      Alert.alert(t('contracts.warning'), t('contracts.signValidation'));
      return;
    }

    signMutation.mutate({ 
      contractIds: [signingContract.id], 
      sig: signature, 
      stationId: selectedStation.id 
    });
  };

  const Header = <ScreenHeader title={t('contracts.title')} />;

  const TabSwitch = (
    <View style={[styles.tabContainer, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
      {(['AVAILABLE', 'SIGNED'] as Tab[]).map((tab) => (
        <Pressable
          key={tab}
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            setActiveTab(tab);
          }}
          style={[
            styles.tab,
            activeTab === tab && { backgroundColor: tokens.colors.primary, borderRadius: 2 }
          ]}
        >
          <Text style={[
            styles.tabText,
            { color: activeTab === tab ? (tokens.colors.text.onPrimary) : tokens.colors.text.dim }
          ]}>
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
      <PageLayout header={Header} disableScroll>
        <LoadingState fullScreen />
      </PageLayout>
    );
  }

  return (
    <PageLayout header={Header} disableScroll>
      <ScrollView style={{ flex: 1 }} contentContainerStyle={{ paddingHorizontal: 20, paddingTop: 10 }}>
        {TabSwitch}

        {activeTab === 'AVAILABLE' ? (
          <View style={{ marginTop: 20, gap: 16 }}>
             <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>{t('contracts.selectProvider')}</Text>
             {stations?.map(station => {
               const signed = isProviderSigned(station.id);
               return (
                 <View 
                   key={station.id} 
                   style={[styles.providerCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}
                 >
                   <View style={styles.providerInfo}>
                     <View style={[styles.providerIcon, { backgroundColor: `${station.color}22`, borderColor: station.color }]}>
                        <Landmark size={20} color={station.color} />
                     </View>
                     <View style={{ flex: 1 }}>
                       <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 18 }}>{station.name}</Text>
                       <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>{signed ? t('contracts.signedStatus') : t('contracts.needsSignature')}</Text>
                     </View>
                     {signed ? (
                        <CheckCircle2 size={24} color={tokens.colors.primary} />
                     ) : (
                        <Pressable 
                          onPress={() => {
                            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                            setSelectedStation(station);
                            const available = contracts || [];
                            setSigningContract(available.length === 1 ? available[0] : null);
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
          <View style={{ marginTop: 20, gap: 16 }}>
            {signedContracts?.length === 0 ? (
              <View style={styles.emptyState}>
                <FileText size={48} color={tokens.colors.borderLight} />
                <Text style={{ color: tokens.colors.text.dim, marginTop: 16, textAlign: 'center' }}>{t('contracts.noSignedContracts')}</Text>
              </View>
            ) : (
              signedContracts?.map(sc => (
                <Pressable
                  key={sc.id}
                  onPress={() => setReadingContract(sc.contract)}
                  style={[styles.providerCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}
                >
                  <View style={styles.providerInfo}>
                    <FileText size={24} color={tokens.colors.primary} />
                    <View style={{ flex: 1, marginLeft: 12 }}>
                      <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 16 }}>{sc.contract.title}</Text>
                      <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>{t('contracts.provider')}: {sc.station?.name || 'FuelFlow Network'}</Text>
                      <Text style={{ color: tokens.colors.text.dim, fontSize: 11 }}>{t('contracts.signedAt')}: {formatExpirationDate(sc.signedAt)}</Text>
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
      <Modal visible={!!readingContract} animationType="fade" transparent>
        <View style={[styles.modalOverlay, { backgroundColor: tokens.colors.overlay }]}>
           <View style={[styles.modalContent, { backgroundColor: tokens.colors.card }]}>
              <View style={styles.modalHeader}>
                <Text style={[styles.modalTitle, { color: tokens.colors.primary }]}>{readingContract?.title}</Text>
                <Pressable onPress={() => setReadingContract(null)}>
                  <X size={24} color={tokens.colors.text.dim} />
                </Pressable>
              </View>
              <ScrollView style={styles.contractTextScroll}>
                <Text style={{ color: tokens.colors.text.primary, lineHeight: 22, fontSize: 14 }}>{readingContract?.content}</Text>
              </ScrollView>
              <Pressable 
                onPress={() => setReadingContract(null)}
                style={[styles.modalCloseBtn, { backgroundColor: tokens.colors.primary }]}
              >
                <Text style={{ color: tokens.colors.text.onPrimary, fontFamily: 'Inter-Black' }}>{t('contracts.close')}</Text>
              </Pressable>
           </View>
        </View>
      </Modal>

      {/* Signing Sheet Modal */}
      <Modal visible={!!selectedStation} animationType="slide" transparent>
        <View style={[styles.signingOverlay, { backgroundColor: tokens.colors.overlay }]}>
           <View style={[styles.signingSheet, { backgroundColor: tokens.colors.background }]}>
              <View style={[styles.sheetHeader, { borderBottomColor: tokens.colors.borderLight }]}>
                <View style={{ flex: 1, paddingRight: 12 }}>
                  <Text style={[styles.sheetTitle, { color: tokens.colors.primary }]} numberOfLines={1}>
                    {signingContract ? signingContract.title : t('contracts.signingTitle')}
                  </Text>
                  <Text style={{ color: tokens.colors.text.dim, marginTop: 2 }} numberOfLines={1}>
                    {t('contracts.provider')}: {selectedStation?.name}
                  </Text>
                </View>
                <Pressable onPress={() => { setSelectedStation(null); setSignature(null); setSigningContract(null); }}>
                  <X size={24} color={tokens.colors.text.dim} />
                </Pressable>
              </View>

              <ScrollView style={styles.sheetScroll} contentContainerStyle={styles.sheetScrollContent}>
                 {contracts && contracts.length > 1 && (
                    <View style={{ marginBottom: 16, gap: 8 }}>
                       {contracts.map((c) => {
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
                               { borderColor: selected ? tokens.colors.primary : tokens.colors.borderLight },
                               selected && { backgroundColor: `${tokens.colors.primary}14` }
                             ]}
                           >
                             <Text
                               style={[styles.contractOptionText, { color: selected ? tokens.colors.primary : tokens.colors.text.primary }]}
                               numberOfLines={2}
                             >
                               {c.title}
                             </Text>
                           </Pressable>
                         );
                       })}
                    </View>
                 )}

                 {signingContract && (
                    <View style={[styles.contractSummary, { borderColor: tokens.colors.borderLight, backgroundColor: tokens.colors.card }]}>
                       <Text style={[styles.summaryTitle, { color: tokens.colors.text.primary }]} numberOfLines={2}>
                          {signingContract.title}
                       </Text>
                       <Text style={[styles.summaryProvider, { color: tokens.colors.text.dim }]}>
                          {t('contracts.provider')}: {selectedStation?.name}
                       </Text>
                    </View>
                 )}

                 <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim, marginTop: 16 }]}>{t('contracts.reviewText')}</Text>
                 <Pressable 
                   onPress={() => setReadingContract(signingContract)}
                   style={[styles.readFullBtn, { borderColor: tokens.colors.primary }]}
                 >
                   <Eye size={16} color={tokens.colors.primary} />
                   <Text style={{ color: tokens.colors.primary, fontFamily: 'Rajdhani-Bold' }}>{t('contracts.readFull')}</Text>
                 </Pressable>
              </ScrollView>

              <View style={styles.signatureSection}>
                 <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>{t('contracts.yourSignature')}</Text>
                 <SignaturePad onCapture={setSignature} />

                 <Pressable
                   onPress={handleSign}
                   disabled={signMutation.isPending || !signature}
                   style={[
                     styles.signSubmitBtn, 
                     { backgroundColor: tokens.colors.primary },
                     (signMutation.isPending || !signature) && { opacity: 0.5 }
                   ]}
                 >
                   <Text style={{ color: tokens.colors.text.onPrimary, fontFamily: 'Inter-Black', fontSize: 16 }}>
                     {signMutation.isPending ? t('contracts.signing') : t('contracts.signAndConfirm')}
                   </Text>
                 </Pressable>
              </View>
           </View>
        </View>
      </Modal>
    </PageLayout>
  );
}

const styles = StyleSheet.create({
  tabContainer: {
    flexDirection: 'row',
    padding: 4,
    borderRadius: 4,
    borderWidth: 1,
    gap: 4,
  },
  tab: {
    flex: 1,
    paddingVertical: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  tabText: {
    fontFamily: 'Inter-Black',
    fontSize: 12,
    letterSpacing: 1,
  },
  sectionLabel: {
    fontFamily: 'Rajdhani-SemiBold',
    fontSize: 10,
    letterSpacing: 2,
    marginBottom: 8,
  },
  providerCard: {
    padding: 16,
    borderRadius: 2,
    borderWidth: 1,
  },
  providerInfo: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  providerIcon: {
    width: 44,
    height: 44,
    borderRadius: 22,
    borderWidth: 1.5,
    alignItems: 'center',
    justifyContent: 'center',
    marginRight: 16,
  },
  actionBtn: {
    width: 40,
    height: 40,
    borderRadius: 20,
    alignItems: 'center',
    justifyContent: 'center',
  },
  emptyState: {
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 60,
  },
  modalOverlay: {
    flex: 1,
    // Scrim colour is `tokens.colors.overlay`, applied at the call site.
    justifyContent: 'center',
    padding: 20,
  },
  modalContent: {
    borderRadius: 4,
    padding: 24,
    maxHeight: '80%',
  },
  modalHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 20,
  },
  modalTitle: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 20,
    flex: 1,
    marginRight: 16,
  },
  contractTextScroll: {
    marginBottom: 24,
  },
  modalCloseBtn: {
    paddingVertical: 16,
    borderRadius: 2,
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
    padding: 20,
    borderBottomWidth: 1,
  },
  sheetTitle: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 22,
  },
  readFullBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    padding: 16,
    borderWidth: 1,
    borderRadius: 4,
    borderStyle: 'dashed',
  },
  signSubmitBtn: {
    marginTop: 32,
    paddingVertical: 18,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  sheetScroll: {
    maxHeight: 180,
    flexShrink: 1,
  },
  sheetScrollContent: {
    padding: 20,
  },
  signatureSection: {
    paddingHorizontal: 20,
    paddingBottom: 24,
  },
  contractSummary: {
    borderWidth: 1,
    borderRadius: 4,
    padding: 16,
    marginBottom: 16,
  },
  summaryTitle: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 16,
  },
  summaryProvider: {
    fontSize: 12,
    marginTop: 4,
  },
  contractOption: {
    borderWidth: 1,
    borderRadius: 4,
    paddingVertical: 12,
    paddingHorizontal: 14,
  },
  contractOptionText: {
    fontFamily: 'Rajdhani-SemiBold',
    fontSize: 14,
  }
});
