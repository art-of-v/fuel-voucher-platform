import { useState } from 'react';
import {
  View,
  Text,
  Pressable,
  TextInput,
  ScrollView,
  ActivityIndicator,
  StyleSheet,
  Modal,
} from 'react-native';
import { Redirect, useRouter } from 'expo-router';
import { User, Building2, Check, Plus, X, AlertTriangle } from 'lucide-react-native';
import { GridPageLayout, ScreenHeader, LoadingState, useContentInsets } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { useStore } from '../src/core/state/appStore';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import {
  useLegalEntities,
  legalEntityErrorKey,
} from '../src/features/company/hooks/useLegalEntities';
import { useToastStore } from '../src/core/feedback/toastStore';
import { Haptics } from '../src/core/utils/haptics';

type CompanyForm = {
  name: string;
  edrpou: string;
  vatNumber: string;
  directorName: string;
  address: string;
};

const EMPTY_FORM: CompanyForm = {
  name: '',
  edrpou: '',
  vatNumber: '',
  directorName: '',
  address: '',
};

export default function ContextsScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const contentInsets = useContentInsets();
  const { t } = useI18n();
  const { isAuthenticated, isLoading: authLoading } = useAuth();
  const showToast = useToastStore((s) => s.show);

  const currentLegalEntityId = useStore((s) => s.currentLegalEntityId);
  const setCurrentContext = useStore((s) => s.setCurrentContext);

  const [createVisible, setCreateVisible] = useState(false);
  const [form, setForm] = useState<CompanyForm>(EMPTY_FORM);

  const { companies, isLoading, hasError, refetch, createCompany, isCreating } =
    useLegalEntities({
      onCreated: () => {
        setForm(EMPTY_FORM);
        setCreateVisible(false);
        showToast({ kind: 'success', message: t('common.saved') });
      },
    });

  // Switch the active context and return to wherever we came from (the profile).
  const selectContext = (id: string | null) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    setCurrentContext(id);
    router.back();
  };

  const canSubmit = form.name.trim().length > 0 && form.edrpou.trim().length > 0;

  const submitCreate = async () => {
    if (!canSubmit || isCreating) return;
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
    try {
      await createCompany({
        name: form.name.trim(),
        edrpou: form.edrpou.trim(),
        vatNumber: form.vatNumber.trim() || undefined,
        directorName: form.directorName.trim() || undefined,
        address: form.address.trim() || undefined,
      });
    } catch (err) {
      showToast({ kind: 'danger', message: t(legalEntityErrorKey(err)) });
    }
  };

  const Header = <ScreenHeader title={t('context.title')} />;

  if (!isAuthenticated && !authLoading) {
    return <Redirect href="/landing" />;
  }

  if (isLoading) {
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
          paddingHorizontal: 20,
          paddingTop: 10,
          paddingBottom: contentInsets.bottom,
        }}
        keyboardShouldPersistTaps="handled"
      >
        <Text style={[styles.subtitle, { color: tokens.colors.text.dim }]}>
          {t('context.subtitle')}
        </Text>

        {/* Personal root — always present, always the default */}
        <ContextRow
          icon={<User size={20} color={tokens.colors.primary} />}
          title={t('context.personal')}
          subtitle={t('context.personalSubtitle')}
          active={currentLegalEntityId == null}
          tokens={tokens}
          activeLabel={t('context.active')}
          onPress={() => selectContext(null)}
        />

        {/* Companies */}
        <Text style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>
          {t('context.companies')}
        </Text>

        {hasError && (
          <View
            style={[
              styles.errorBanner,
              { backgroundColor: tokens.colors.card, borderColor: tokens.colors.error },
            ]}
          >
            <AlertTriangle size={16} color={tokens.colors.error} />
            <Text style={[styles.errorBannerText, { color: tokens.colors.error }]}>
              {t('context.loadError')}
            </Text>
            <Pressable
              onPress={() => refetch()}
              style={[styles.retryBtn, { borderColor: tokens.colors.error }]}
            >
              <Text style={{ color: tokens.colors.error, fontFamily: 'Inter-Black', fontSize: 11, letterSpacing: 0.8 }}>
                {t('common.retry')}
              </Text>
            </Pressable>
          </View>
        )}

        {companies.map((c) => (
          <ContextRow
            key={c.id}
            icon={<Building2 size={20} color={tokens.colors.primary} />}
            title={c.name}
            subtitle={c.edrpou ? `ЄДРПОУ ${c.edrpou}` : undefined}
            active={currentLegalEntityId === c.id}
            tokens={tokens}
            activeLabel={t('context.active')}
            onPress={() => selectContext(c.id)}
          />
        ))}

        {/* Add company */}
        <Pressable
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            setForm(EMPTY_FORM);
            setCreateVisible(true);
          }}
          style={[styles.addRow, { borderColor: tokens.colors.primary }]}
        >
          <Plus size={18} color={tokens.colors.primary} />
          <Text style={{ color: tokens.colors.primary, fontFamily: 'Inter-Black', fontSize: 13, letterSpacing: 0.8 }}>
            {t('context.addCompany')}
          </Text>
        </Pressable>
      </ScrollView>

      {/* Create-company sheet */}
      <Modal
        visible={createVisible}
        transparent
        animationType="slide"
        onRequestClose={() => setCreateVisible(false)}
      >
        <View style={[styles.modalOverlay, { backgroundColor: tokens.colors.overlay }]}>
          <View
            style={[
              styles.modalSheet,
              { backgroundColor: tokens.colors.background, borderColor: tokens.colors.borderLight },
            ]}
          >
            <View style={styles.modalHeader}>
              <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 20 }}>
                {t('context.create.title')}
              </Text>
              <Pressable onPress={() => setCreateVisible(false)} style={{ padding: 6 }}>
                <X size={22} color={tokens.colors.text.muted} />
              </Pressable>
            </View>

            <ScrollView
              style={{ maxHeight: 420 }}
              contentContainerStyle={{ gap: 14, paddingVertical: 4 }}
              keyboardShouldPersistTaps="handled"
            >
              <Field
                label={t('context.create.name')}
                value={form.name}
                onChangeText={(v) => setForm((f) => ({ ...f, name: v }))}
                tokens={tokens}
              />
              <Field
                label={t('context.create.edrpou')}
                value={form.edrpou}
                onChangeText={(v) => setForm((f) => ({ ...f, edrpou: v }))}
                keyboardType="number-pad"
                tokens={tokens}
              />
              <Field
                label={t('context.create.vatNumber')}
                value={form.vatNumber}
                onChangeText={(v) => setForm((f) => ({ ...f, vatNumber: v }))}
                keyboardType="number-pad"
                tokens={tokens}
              />
              <Field
                label={t('context.create.directorName')}
                value={form.directorName}
                onChangeText={(v) => setForm((f) => ({ ...f, directorName: v }))}
                tokens={tokens}
              />
              <Field
                label={t('context.create.address')}
                value={form.address}
                onChangeText={(v) => setForm((f) => ({ ...f, address: v }))}
                tokens={tokens}
              />
            </ScrollView>

            <Pressable
              disabled={!canSubmit || isCreating}
              onPress={submitCreate}
              style={[
                styles.confirmBtn,
                { backgroundColor: tokens.colors.primary },
                (!canSubmit || isCreating) && { opacity: 0.4 },
              ]}
            >
              {isCreating ? (
                <ActivityIndicator size="small" color={tokens.colors.text.onPrimary} />
              ) : (
                <Text style={{ color: tokens.colors.text.onPrimary, fontFamily: 'Inter-Black', fontSize: 13, letterSpacing: 1 }}>
                  {t('context.create.submit')}
                </Text>
              )}
            </Pressable>
          </View>
        </View>
      </Modal>
    </GridPageLayout>
  );
}

function ContextRow({
  icon,
  title,
  subtitle,
  active,
  activeLabel,
  onPress,
  tokens,
}: {
  icon: React.ReactNode;
  title: string;
  subtitle?: string;
  active: boolean;
  activeLabel: string;
  onPress: () => void;
  tokens: ReturnType<typeof useDesignTokens>;
}) {
  return (
    <Pressable
      onPress={onPress}
      style={[
        styles.row,
        {
          backgroundColor: active ? `${tokens.colors.primary}14` : tokens.colors.card,
          borderColor: active ? tokens.colors.primary : tokens.colors.borderLight,
        },
      ]}
    >
      <View
        style={[
          styles.rowIcon,
          { borderColor: tokens.colors.borderAccent, backgroundColor: tokens.colors.surfaceSunken },
        ]}
      >
        {icon}
      </View>
      <View style={{ flex: 1 }}>
        <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 16 }} numberOfLines={1}>
          {title}
        </Text>
        {subtitle ? (
          <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }} numberOfLines={1}>
            {subtitle}
          </Text>
        ) : null}
      </View>
      {active ? (
        <View style={styles.activeTag}>
          <Check size={16} color={tokens.colors.primary} />
          <Text style={{ color: tokens.colors.primary, fontFamily: 'Inter-Black', fontSize: 10, letterSpacing: 0.8 }}>
            {activeLabel.toUpperCase()}
          </Text>
        </View>
      ) : null}
    </Pressable>
  );
}

function Field({
  label,
  value,
  onChangeText,
  keyboardType,
  tokens,
}: {
  label: string;
  value: string;
  onChangeText: (v: string) => void;
  keyboardType?: 'default' | 'number-pad';
  tokens: ReturnType<typeof useDesignTokens>;
}) {
  return (
    <View style={{ gap: 6 }}>
      <Text style={[styles.fieldLabel, { color: tokens.colors.text.dim }]}>{label}</Text>
      <TextInput
        value={value}
        onChangeText={onChangeText}
        keyboardType={keyboardType ?? 'default'}
        autoCapitalize="none"
        placeholderTextColor={tokens.colors.text.dim}
        style={[
          styles.input,
          {
            backgroundColor: tokens.colors.background,
            color: tokens.colors.text.primary,
            borderColor: tokens.colors.borderLight,
          },
        ]}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  subtitle: {
    fontFamily: 'Inter-Medium',
    fontSize: 13,
    marginBottom: 18,
  },
  sectionLabel: {
    fontFamily: 'Rajdhani-SemiBold',
    fontSize: 12,
    letterSpacing: 3,
    textTransform: 'uppercase',
    marginTop: 10,
    marginBottom: 12,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 14,
    padding: 14,
    borderRadius: 12,
    borderWidth: 1,
    marginBottom: 12,
  },
  rowIcon: {
    width: 44,
    height: 44,
    borderRadius: 10,
    borderWidth: 1.5,
    alignItems: 'center',
    justifyContent: 'center',
  },
  activeTag: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
  },
  addRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 14,
    borderRadius: 12,
    borderWidth: 1,
    borderStyle: 'dashed',
    marginTop: 4,
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingHorizontal: 14,
    paddingVertical: 12,
    borderRadius: 10,
    borderWidth: 1,
    marginBottom: 12,
  },
  errorBannerText: {
    flex: 1,
    fontFamily: 'Inter-Bold',
    fontSize: 13,
  },
  retryBtn: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 8,
    borderWidth: 1,
  },
  modalOverlay: {
    flex: 1,
    justifyContent: 'flex-end',
  },
  modalSheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    borderWidth: 1,
    padding: 20,
    paddingBottom: 40,
    gap: 16,
  },
  modalHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  fieldLabel: {
    fontFamily: 'Rajdhani-SemiBold',
    fontSize: 11,
    letterSpacing: 1.5,
    textTransform: 'uppercase',
  },
  input: {
    borderRadius: 10,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontFamily: 'Inter-Bold',
    fontSize: 14,
    borderWidth: 1,
  },
  confirmBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 16,
    borderRadius: 12,
  },
});
