import React, { useState } from 'react';
import { View } from 'react-native';

import { BottomSheet, Button, TextField } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import {
  edrpouErrorKey,
  isValidEdrpou,
  isValidVat,
  vatErrorKey,
} from '../../../core/validation/legalEntityIdentifier';

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

  // Same rule the server enforces on the scoped PUT (#128), mirrored so the reason appears on the
  // field (#265). A first-ever run shows no error: an empty EDRPOU is not "malformed", it is
  // "not filled in yet", and saying otherwise before a keystroke is noise.
  const [edrpouTouched, setEdrpouTouched] = useState(false);
  const [vatTouched, setVatTouched] = useState(false);
  const [wasVisible, setWasVisible] = useState(visible);

  // A sheet that has been closed and reopened starts from the field's own state, not from what the
  // person typed last time - otherwise a form they corrected and abandoned reopens still red.
  // Adjusted during render rather than in an effect: this is React's own answer to "sync state with
  // a prop that changed", and `set-state-in-effect` is a lint error here for good reason - an effect
  // would first paint the stale state.
  if (visible !== wasVisible) {
    setWasVisible(visible);
    if (!visible) {
      setEdrpouTouched(false);
      setVatTouched(false);
    }
  }

  const edrpouError = edrpouTouched ? edrpouErrorKey(form.edrpou) : null;
  const vatError = vatTouched ? vatErrorKey(form.vatNumber) : null;
  const identifiersValid = isValidEdrpou(form.edrpou) && isValidVat(form.vatNumber);

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
          // The server validates this on every write, so a save with a malformed EDRPOU cannot
          // succeed whatever the client does - it just fails with a message this screen used to
          // swallow. Disabled instead, with the reason on the field.
          disabled={!identifiersValid}
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
              onBlur={() => setEdrpouTouched(true)}
              error={edrpouError ? t(edrpouError) : undefined}
              keyboardType="numeric"
            />
          </View>
          <View style={{ flex: 1 }}>
            <TextField
              label={t('profile.vatNumber')}
              value={form.vatNumber}
              onChangeText={(text) => onChange({ ...form, vatNumber: text })}
              onBlur={() => setVatTouched(true)}
              error={vatError ? t(vatError) : undefined}
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
