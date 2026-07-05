import React, { useState, useEffect, useMemo } from 'react';
import { View, Text, Pressable, ScrollView, ActivityIndicator, StyleSheet, Alert, Modal, useWindowDimensions, Platform } from 'react-native';
import { useRouter } from 'expo-router';
import { ChevronLeft, FileText, CheckCircle2, Circle, Eye, PenTool, X, Landmark } from 'lucide-react-native';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAvailableContracts, signContracts, getSignedContracts, getLegalProfile, getStations, Contract, Station, UserContract } from '../src/lib/api';
import { PageLayout } from '../src/components/page-layout';
import { useDesignTokens } from '../src/lib/design-tokens';
import { Haptics } from '../src/lib/haptics';
import { SignaturePad } from '../src/components/SignaturePad';

type Tab = 'AVAILABLE' | 'SIGNED';

export default function ContractsScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const queryClient = useQueryClient();
  const { width } = useWindowDimensions();
  
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
    getLegalProfile().then((data: any) => {
      if (!data.company) {
        Alert.alert('Потрібен профіль', 'Будь ласка, заповніть профіль компанії перед підписанням договорів.');
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
      Alert.alert('Успіх', 'Договір успішно підписано');
      setSigningContract(null);
      setSelectedStation(null);
      setSignature(null);
      setActiveTab('SIGNED');
    },
    onError: (err: any) => {
      Alert.alert('Помилка', err.message || 'Не вдалося підписати договір');
    }
  });

  // Helpers
  const isProviderSigned = (stationId: string) => {
    return signedContracts?.some(sc => sc.station?.id === stationId);
  };

  const handleSign = () => {
    if (!selectedStation || !signingContract || !signature) {
      Alert.alert('Попередження', 'Будь ласка, переконайтеся що вибрано станцію, контракт та залишено підпис');
      return;
    }

    signMutation.mutate({ 
      contractIds: [signingContract.id], 
      sig: signature, 
      stationId: selectedStation.id 
    });
  };

  const Header = (
    <View style={styles.header}>
      <Pressable onPress={() => router.back()} style={styles.backBtn}>
        <ChevronLeft size={24} color={tokens.colors.primary} />
      </Pressable>
      <Text style={[styles.title, { color: tokens.colors.primary }]}>ДОГОВОРИ</Text>
      <View style={{ width: 24 }} />
    </View>
  );

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
            { color: activeTab === tab ? (tokens.colors.isDark ? '#000' : '#FFF') : tokens.colors.text.dim }
          ]}>
            {tab === 'AVAILABLE' ? 'ДОСТУПНІ' : 'ПІДПИСАНІ'}
          </Text>
        </Pressable>
      ))}
    </View>
  );

  if (isLoading) {
    return (
      <View style={[styles.center, { backgroundColor: tokens.colors.background }]}>
        <ActivityIndicator size="large" color={tokens.colors.primary} />
      </View>
    );
  }

  return (
    <PageLayout header={Header}>
      <ScrollView style={{ flex: 1 }} contentContainerStyle={{ paddingHorizontal: 20, paddingTop: 10 }}>
        {TabSwitch}

        {activeTab === 'AVAILABLE' ? (
          <View style={{ marginTop: 20, gap: 16 }}>
             <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>ВИБЕРІТЬ ПРОВАЙДЕРА ДЛЯ ПІДПИСАННЯ</Text>
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
                       <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>{signed ? 'ДОГОВІР ПІДПИСАНО' : 'ПОТРЕБУЄ ПІДПИСАННЯ'}</Text>
                     </View>
                     {signed ? (
                        <CheckCircle2 size={24} color={tokens.colors.primary} />
                     ) : (
                        <Pressable 
                          onPress={() => {
                            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                            setSelectedStation(station);
                            setSigningContract(contracts?.[0] || null);
                          }}
                          style={[styles.actionBtn, { backgroundColor: tokens.colors.primary }]}
                        >
                          <PenTool size={16} color={tokens.colors.isDark ? '#000' : '#FFF'} />
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
                <Text style={{ color: tokens.colors.text.dim, marginTop: 16, textAlign: 'center' }}>У вас поки немає підписаних договорів</Text>
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
                      <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>Провайдер: {sc.station?.name || 'FuelFlow Network'}</Text>
                      <Text style={{ color: tokens.colors.text.dim, fontSize: 11 }}>Підписано: {new Date(sc.signedAt).toLocaleDateString()}</Text>
                    </View>
                    <Eye size={20} color={tokens.colors.primary} />
                  </View>
                </Pressable>
              ))
            )}
          </View>
        )}

        <View style={{ height: 100 }} />
      </ScrollView>

      {/* Contract Reader Modal */}
      <Modal visible={!!readingContract} animationType="fade" transparent>
        <View style={styles.modalOverlay}>
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
                <Text style={{ color: tokens.colors.isDark ? '#000' : '#FFF', fontFamily: 'Inter-Black' }}>ЗАКРИТИ</Text>
              </Pressable>
           </View>
        </View>
      </Modal>

      {/* Signing Sheet Modal */}
      <Modal visible={!!selectedStation} animationType="slide" transparent>
        <View style={styles.signingOverlay}>
           <View style={[styles.signingSheet, { backgroundColor: tokens.colors.background }]}>
              <View style={styles.sheetHeader}>
                <View>
                  <Text style={[styles.sheetTitle, { color: tokens.colors.primary }]}>ПІДПИСАННЯ ДОГОВОРУ</Text>
                  <Text style={{ color: tokens.colors.text.dim }}>Провайдер: {selectedStation?.name}</Text>
                </View>
                <Pressable onPress={() => { setSelectedStation(null); setSignature(null); }}>
                  <X size={24} color={tokens.colors.text.dim} />
                </Pressable>
              </View>

              <ScrollView>
                 <View style={{ padding: 20 }}>
                    <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>ОЗНАЙОМТЕСЯ З ТЕКСТОМ</Text>
                    <Pressable 
                      onPress={() => setReadingContract(signingContract)}
                      style={[styles.readFullBtn, { borderColor: tokens.colors.primary }]}
                    >
                      <Eye size={16} color={tokens.colors.primary} />
                      <Text style={{ color: tokens.colors.primary, fontFamily: 'Rajdhani-Bold' }}>ЧИТАТИ ПОВНИЙ ТЕКСТ</Text>
                    </Pressable>

                    <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim, marginTop: 24 }]}>ВАШ ПІДПИС</Text>
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
                      <Text style={{ color: tokens.colors.isDark ? '#000' : '#FFF', fontFamily: 'Inter-Black', fontSize: 16 }}>
                        {signMutation.isPending ? 'ПІДПИСАННЯ...' : 'ПІДПИСАТИ ТА ПІДТВЕРДИТИ'}
                      </Text>
                    </Pressable>
                 </View>
              </ScrollView>
           </View>
        </View>
      </Modal>
    </PageLayout>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingBottom: 16,
  },
  backBtn: {
    padding: 8,
  },
  title: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 24,
    textTransform: 'uppercase',
  },
  center: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
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
    backgroundColor: 'rgba(0,0,0,0.8)',
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
    backgroundColor: 'rgba(0,0,0,0.6)',
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
    borderBottomColor: 'rgba(255,255,255,0.05)',
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
  }
});
