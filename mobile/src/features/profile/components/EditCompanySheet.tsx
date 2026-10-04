import React from 'react';
import { View } from 'react-native';

import { BottomSheet, Button, TextField } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';

export interface CompanyForm {
  name: string;
  edrpou: string;
  vatNumber: string;
  directorName: string;
  address: string;
  phone: string;
  email: string;
}

/**
 * The "edit company information" sheet.
 *
 * `form` is owned by the screen for the same reason as in `EditPersonalSheet`: a
 * React Native `Modal` does not mount its children while hidden, so state owned
 * here would be lost on every dismiss.
 */
export interface EditCompanySheetProps {
  visible: boolean;
  form: CompanyForm;
  onChange: (next: CompanyForm) => void;
  onClose: () => void;
  onSave: () => void;
  isSaving: boolean;
}

export function EditCompanySheet({
  visible,
  form,
  onChange,
  onClose,
  onSave,
  isSaving,
}: EditCompanySheetProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  return (
    <BottomSheet
      visible={visible}
      onClose={onClose}
      title={t('profile.editCompanyTitle')}
      footer={
        <Button
          label={t('common.save')}
          variant="primary"
          size="lg"
          fullWidth
          loading={isSaving}
          onPress={onSave}
        />
      }
    >
      <View style={{ gap: tokens.spacing.lg, paddingBottom: tokens.spacing.lg }}>
        <TextField
          label={t('profile.companyName')}
          value={form.name}
          onChangeText={(text) => onChange({ ...form, name: text })}
        />

        <View style={{ flexDirection: 'row', gap: tokens.spacing.md }}>
          <View style={{ flex: 1 }}>
            <TextField
              label={t('profile.edrpou')}
              value={form.edrpou}
              onChangeText={(text) => onChange({ ...form, edrpou: text })}
              keyboardType="numeric"
            />
          </View>
          <View style={{ flex: 1 }}>
            <TextField
              label={t('profile.vatNumber')}
              value={form.vatNumber}
              onChangeText={(text) => onChange({ ...form, vatNumber: text })}
              keyboardType="numeric"
            />
          </View>
        </View>

        <TextField
          label={t('profile.directorName')}
          value={form.directorName}
          onChangeText={(text) => onChange({ ...form, directorName: text })}
        />

        <TextField
          label={t('profile.companyAddress')}
          value={form.address}
          onChangeText={(text) => onChange({ ...form, address: text })}
        />
      </View>
    </BottomSheet>
  );
}
