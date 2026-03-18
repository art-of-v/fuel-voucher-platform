import React, { useState, useEffect, useCallback } from 'react';
import { View, Text, Pressable, ScrollView, ActivityIndicator, StyleSheet, Alert } from 'react-native';
import { useRouter } from 'expo-router';
import { ChevronLeft, FileText, CheckCircle2, Circle } from 'lucide-react-native';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAvailableContracts, signContracts, getSignedContracts, Contract } from '../src/lib/api';
import { PageLayout } from '../src/components/page-layout';
import { useDesignTokens } from '../src/lib/design-tokens';
import { Haptics } from '../src/lib/haptics';
import { SignaturePad } from '../src/components/SignaturePad';

export default function ContractsScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const queryClient = useQueryClient();
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [signature, setSignature] = useState<string | null>(null);

  const { data: contracts, isLoading } = useQuery({
    queryKey: ['contracts'],
    queryFn: getAvailableContracts
  });

  const { data: signedContracts } = useQuery({
    queryKey: ['signedContracts'],
    queryFn: getSignedContracts
  });

  const signMutation = useMutation({
    mutationFn: ({ ids, sig }: { ids: string[], sig: string }) => signContracts(ids, sig),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['signedContracts'] });
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      Alert.alert('Успіх', 'Договори успішно підписано');
      router.back();
    },
    onError: (err: any) => {
      Alert.alert('Помилка', err.message || 'Не вдалося підписати договори');
    }
  });

  const isSigned = (id: string) => {
    return signedContracts?.some(sc => sc.contract.id === id);
  };

  const toggleSelect = (id: string) => {
    if (isSigned(id)) return;
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setSelectedIds(prev => 
      prev.includes(id) ? prev.filter(i => i !== id) : [...prev, id]
    );
  };

  const onSignatureCapture = React.useCallback((sig: string) => {
    setSignature(sig);
  }, []);

  const handleSign = () => {
    if (selectedIds.length === 0) {
      Alert.alert('Попередження', 'Виберіть принаймні один договір');
      return;
    }
    // In a real app, we'd capture the actual signature data. 
    // For now, we'll use a placeholder or the captured SVG data from SignaturePad.
    // The SignaturePad onCapture would set the state.
    if (!signature) {
      Alert.alert('Попередження', 'Будь ласка, залиште підпис');
      return;
    }

    signMutation.mutate({ ids: selectedIds, sig: signature });
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

  if (isLoading) {
    return (
      <View style={[styles.center, { backgroundColor: tokens.colors.background }]}>
        <ActivityIndicator size="large" color={tokens.colors.primary} />
      </View>
    );
  }

  return (
    <PageLayout header={Header}>
      <ScrollView style={{ flex: 1 }} contentContainerStyle={{ padding: 20 }}>
        <Text style={[styles.subtitle, { color: tokens.colors.text.dim }]}>ДОСТУПНІ ДОГОВОРИ</Text>
        
        <View style={{ gap: 12, marginBottom: 32 }}>
          {contracts?.map(contract => (
            <Pressable
              key={contract.id}
              onPress={() => toggleSelect(contract.id)}
              style={[
                styles.contractCard,
                { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
                selectedIds.includes(contract.id) && { borderColor: tokens.colors.primary, borderWidth: 1.5 },
                isSigned(contract.id) && { opacity: 0.6 }
              ]}
            >
              <FileText size={20} color={isSigned(contract.id) ? tokens.colors.text.dim : tokens.colors.primary} />
              <View style={{ flex: 1, marginLeft: 12 }}>
                <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 16 }}>
                  {contract.title}
                </Text>
                <Text style={{ color: tokens.colors.text.dim, fontFamily: 'Inter-Medium', fontSize: 12 }}>
                  Версія: {contract.version} {isSigned(contract.id) && '• ПІДПИСАНО'}
                </Text>
              </View>
              {!isSigned(contract.id) && (
                selectedIds.includes(contract.id) 
                  ? <CheckCircle2 size={24} color={tokens.colors.primary} />
                  : <Circle size={24} color={tokens.colors.borderLight} />
              )}
            </Pressable>
          ))}
        </View>

        {selectedIds.length > 0 && (
          <View>
            <Text style={[styles.subtitle, { color: tokens.colors.text.dim }]}>ВАШ ПІДПИС</Text>
            <SignaturePad onCapture={onSignatureCapture} />
            
            <Pressable
              onPress={handleSign}
              disabled={signMutation.isPending}
              style={[
                styles.signBtn,
                { backgroundColor: tokens.colors.primary },
                signMutation.isPending && { opacity: 0.5 }
              ]}
            >
              <Text style={[styles.signBtnText, { color: tokens.colors.isDark ? '#000' : '#FFF' }]}>
                {signMutation.isPending ? 'ПІДПИСАННЯ...' : `ПІДПИСАТИ (${selectedIds.length})`}
              </Text>
            </Pressable>
          </View>
        )}

        <View style={{ height: 100 }} />
      </ScrollView>
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
  subtitle: {
    fontFamily: 'Rajdhani-SemiBold',
    fontSize: 12,
    letterSpacing: 2,
    textTransform: 'uppercase',
    marginBottom: 16,
  },
  contractCard: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: 16,
    borderRadius: 2,
    borderWidth: 1,
  },
  signBtn: {
    marginTop: 24,
    width: '100%',
    paddingVertical: 18,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  signBtnText: {
    fontFamily: 'Inter-Black',
    fontSize: 16,
    letterSpacing: 1,
    textTransform: 'uppercase',
  },
});
