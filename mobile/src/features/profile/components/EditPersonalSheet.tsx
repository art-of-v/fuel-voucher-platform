import React, { useState } from 'react';
import { Keyboard, Platform, Pressable, View } from 'react-native';
import DateTimePicker from '@react-native-community/datetimepicker';
import { Calendar } from 'lucide-react-native';

import { BottomSheet, Button, FieldShell, Text, TextField } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';

/**
 * Birthdate picker bounds. An empty field anchors on 1990 rather than today,
 * because a spinner that opens on the current date makes the user scroll back
 * three decades before they reach a plausible year of birth.
 */
const BIRTHDATE_ANCHOR = new Date(1990, 0, 1);
const BIRTHDATE_MIN = new Date(1900, 0, 1);
/** Stable so the wheel is not handed a new upper bound on every re-render. */
const BIRTHDATE_MAX = new Date();

/** Date -> the DD.MM.YYYY form the profile form and API adapter both expect. */
const formatDateToDisplay = (date: Date) =>
  [
    String(date.getDate()).padStart(2, '0'),
    String(date.getMonth() + 1).padStart(2, '0'),
    date.getFullYear(),
  ].join('.');

/**
 * Accepts `DD.MM.YYYY`, `YYYY-MM-DD`, or anything `new Date()` understands, and
 * falls back to the anchor rather than an Invalid Date. An unparseable string
 * reaching `DateTimePicker` would otherwise render as an empty wheel.
 */
function parseSafeDate(dateStr: string): Date {
  if (!dateStr) return BIRTHDATE_ANCHOR;
  const trimmed = dateStr.trim();

  if (trimmed.includes('.')) {
    const parts = trimmed.split('.');
    if (parts.length === 3) {
      const d = parseInt(parts[0], 10);
      const m = parseInt(parts[1], 10);
      const y = parseInt(parts[2], 10);
      if (!isNaN(d) && !isNaN(m) && !isNaN(y)) {
        const dt = new Date(y, m - 1, d);
        if (!isNaN(dt.getTime())) return dt;
      }
    }
  }

  if (trimmed.includes('-')) {
    const parts = trimmed.split('-');
    if (parts.length === 3) {
      const y = parseInt(parts[0], 10);
      const m = parseInt(parts[1], 10);
      const d = parseInt(parts[2], 10);
      if (!isNaN(d) && !isNaN(m) && !isNaN(y)) {
        const dt = new Date(y, m - 1, d);
        if (!isNaN(dt.getTime())) return dt;
      }
    }
  }

  const d = new Date(trimmed);
  return isNaN(d.getTime()) ? BIRTHDATE_ANCHOR : d;
}

export interface PersonalForm {
  firstName: string;
  lastName: string;
  birthdate: string;
}

/**
 * The "edit personal information" sheet.
 *
 * **The form state lives in the screen, deliberately.** `BottomSheet` is a React
 * Native `Modal`, and a `Modal` does not mount its children while `visible` is
 * false (verified — see the note in `app/profile.tsx`). Form state owned here
 * would therefore be discarded every time the sheet closed, so a user who typed a
 * name, dismissed the sheet and came back would find the field empty. Passing
 * `form` and `onChange` down keeps those edits.
 *
 * The picker's own open/closed state *does* live here, because it is always false
 * on open and reset on close anyway — there is nothing to preserve.
 */
export interface EditPersonalSheetProps {
  visible: boolean;
  form: PersonalForm;
  onChange: (next: PersonalForm) => void;
  onClose: () => void;
  onSave: () => void;
  isSaving: boolean;
}

export function EditPersonalSheet({
  visible,
  form,
  onChange,
  onClose,
  onSave,
  isSaving,
}: EditPersonalSheetProps) {
  const tokens = useDesignTokens();
  const { t, language } = useI18n();
  const [showDatePicker, setShowDatePicker] = useState(false);
  const [tempDate, setTempDate] = useState(BIRTHDATE_ANCHOR);

  const setBirthdate = (date: Date) => onChange({ ...form, birthdate: formatDateToDisplay(date) });

  return (
    <BottomSheet
      visible={visible}
      onClose={() => {
        onClose();
        // The picker lives inside this sheet, so it has to close with it —
        // otherwise re-opening the sheet reveals a stale spinner.
        setShowDatePicker(false);
      }}
      title={t('profile.editPersonalTitle')}
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
        <View style={{ flexDirection: 'row', gap: tokens.spacing.md }}>
          <View style={{ flex: 1 }}>
            <TextField
              label={t('profile.firstName')}
              value={form.firstName}
              onChangeText={(text) => onChange({ ...form, firstName: text })}
              autoCapitalize="words"
            />
          </View>
          <View style={{ flex: 1 }}>
            <TextField
              label={t('profile.lastName')}
              value={form.lastName}
              onChangeText={(text) => onChange({ ...form, lastName: text })}
              autoCapitalize="words"
            />
          </View>
        </View>

        <FieldShell
          label={t('profile.birthdate')}
          trailing={<Calendar size={18} color={tokens.colors.text.muted} />}
          onPress={() => {
            Keyboard.dismiss();
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            if (showDatePicker) {
              setShowDatePicker(false);
              return;
            }
            setTempDate(parseSafeDate(form.birthdate));
            setShowDatePicker(true);
          }}
        >
          <Text
            style={{
              color: form.birthdate ? tokens.colors.text.primary : tokens.colors.text.muted,
              fontSize: 15,
            }}
          >
            {form.birthdate || 'ДД.ММ.РРРР'}
          </Text>
        </FieldShell>

        {/*
          The picker renders inside the sheet, not beside it. BottomSheet is
          itself a Modal, and iOS refuses to present a second Modal over one
          that is already showing — so the old root-level <Modal> never
          appeared and the tap looked dead. Inline reveal has no such limit.
          Android is unaffected either way: its picker is a native dialog
          owned by the Activity, so it opens above the sheet regardless of
          where it sits in the tree.
        */}
        {showDatePicker &&
          (Platform.OS === 'ios' ? (
            <View
              style={{
                borderRadius: tokens.radius.md,
                borderWidth: 1,
                borderColor: tokens.colors.border,
                backgroundColor: tokens.colors.surfaceSunken,
                overflow: 'hidden',
              }}
            >
              <View
                style={{
                  flexDirection: 'row',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  paddingHorizontal: tokens.spacing.lg,
                  paddingVertical: tokens.spacing.md,
                  borderBottomWidth: 1,
                  borderBottomColor: tokens.colors.borderSubtle,
                }}
              >
                <Pressable onPress={() => setShowDatePicker(false)} hitSlop={12}>
                  <Text role="bodyStrong" tone="muted">
                    {t('common.cancel')}
                  </Text>
                </Pressable>
                <Pressable
                  onPress={() => {
                    setBirthdate(tempDate);
                    setShowDatePicker(false);
                    Haptics.selectionAsync();
                  }}
                  hitSlop={12}
                >
                  <Text role="bodyStrong" tone="accent">
                    {t('common.done')}
                  </Text>
                </Pressable>
              </View>
              <DateTimePicker
                value={tempDate}
                mode="date"
                display="spinner"
                locale={language}
                minimumDate={BIRTHDATE_MIN}
                maximumDate={BIRTHDATE_MAX}
                textColor={tokens.colors.text.primary}
                onChange={(_, date) => {
                  if (date) setTempDate(date);
                }}
              />
            </View>
          ) : (
            <DateTimePicker
              value={tempDate}
              mode="date"
              display="default"
              minimumDate={BIRTHDATE_MIN}
              maximumDate={BIRTHDATE_MAX}
              onChange={(event, date) => {
                setShowDatePicker(false);
                if (event.type === 'dismissed' || !date) return;
                setBirthdate(date);
                Haptics.selectionAsync();
              }}
            />
          ))}
      </View>
    </BottomSheet>
  );
}
