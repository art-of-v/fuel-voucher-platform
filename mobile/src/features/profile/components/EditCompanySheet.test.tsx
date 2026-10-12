import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react-native';

import { EditCompanySheet } from './EditCompanySheet';

jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: (selector?: (s: any) => unknown) => {
    const state = { t: (key: string) => key, language: 'uk', setLanguage: jest.fn() };
    return selector ? selector(state) : state;
  },
}));

const VALID_FORM = {
  name: 'Паливна',
  edrpou: '12345678',
  vatNumber: '',
  directorName: 'Директор',
  address: 'Київ',
  phone: '',
  email: '',
};

function renderSheet(overrides: Partial<typeof VALID_FORM> = {}, props: Record<string, unknown> = {}) {
  return render(
    <EditCompanySheet
      visible
      form={{ ...VALID_FORM, ...overrides }}
      onChange={jest.fn()}
      onClose={jest.fn()}
      onSave={jest.fn()}
      isSaving={false}
      {...props}
    />,
  );
}

const saveButton = () => screen.getByRole('button', { name: 'common.save' });

/**
 * The EDRPOU is a globally unique column: a bad one is permanent until somebody finds it, which is
 * why the server refuses to store it (#128). This sheet is one of the two places a person types it,
 * and before this the only feedback was a save that failed with a message the client threw away.
 *
 * So the sheet says why, on the field, from the first blur - and the Save button does not offer a
 * submission the server is going to reject. Both halves are asserted here; the rule itself is
 * covered by `core/validation/legalEntityIdentifier.test.ts`, and the server's copy by
 * `LegalEntityIdentifierTests`.
 */
describe('EditCompanySheet — identifier validation', () => {
  it('says nothing about an untouched field, valid or not', () => {
    // An empty EDRPOU is "not filled in yet", not "malformed". Telling somebody their EDRPOU is
    // wrong before they have typed it is noise that teaches people to ignore the field.
    renderSheet({ edrpou: '123' });

    expect(screen.queryByText('company.edrpouInvalidFormat')).toBeNull();
  });

  it('names the problem on the field once it has been left', () => {
    renderSheet({ edrpou: '1234567890456565' });

    fireEvent(screen.getByDisplayValue('1234567890456565'), 'blur');

    // The value from #128: 16 digits, obviously not an EDRPOU.
    expect(screen.getByText('company.edrpouInvalidFormat')).toBeTruthy();
  });

  it('drops the complaint once the value the screen holds is valid', () => {
    // Once a field has been visited the message tracks the value: a person who fixes one digit of a
    // nine-digit number must not be left staring at a stale complaint. The value has to be handed
    // back in, because the screen owns the form - see the suite comment in EditPersonalSheet.
    const sheet = (form: typeof VALID_FORM) => (
      <EditCompanySheet
        visible
        form={form}
        onChange={jest.fn()}
        onClose={jest.fn()}
        onSave={jest.fn()}
        isSaving={false}
      />
    );

    const { rerender } = render(sheet({ ...VALID_FORM, edrpou: '123456789' }));

    fireEvent(screen.getByDisplayValue('123456789'), 'blur');
    expect(screen.getByText('company.edrpouInvalidFormat')).toBeTruthy();

    rerender(sheet({ ...VALID_FORM, edrpou: '12345678' }));
    expect(screen.queryByText('company.edrpouInvalidFormat')).toBeNull();
  });

  it('will not submit a malformed EDRPOU, because the server will not accept it', () => {
    renderSheet({ edrpou: '1234567890' });

    expect(saveButton().props.accessibilityState.disabled).toBe(true);
  });

  it('will submit eight digits', () => {
    renderSheet();

    expect(saveButton().props.accessibilityState.disabled).toBe(false);
  });

  it('will not submit an EDRPOU with a space inside it', () => {
    // A paste from a document with a grouped number. LegalEntityIdentifier rejects it server-side,
    // so accepting it here would only move the failure later.
    renderSheet({ edrpou: '1234 5678' });

    expect(saveButton().props.accessibilityState.disabled).toBe(true);
  });

  it('treats an absent VAT as fine and a malformed one as not', () => {
    const { unmount } = renderSheet({ vatNumber: '' });
    expect(saveButton().props.accessibilityState.disabled).toBe(false);
    unmount();

    renderSheet({ vatNumber: '999999999' });
    expect(saveButton().props.accessibilityState.disabled).toBe(true);
    fireEvent(screen.getByDisplayValue('999999999'), 'blur');
    expect(screen.getByText('company.vatInvalidFormat')).toBeTruthy();
  });

  it('forgets a complaint when the sheet is reopened', () => {
    // The form is owned by the screen (a Modal does not mount children while hidden), so the sheet
    // is re-created on every open. State that outlived the sheet would greet the next person with an
    // error about a value they have not typed yet.
    const { rerender } = render(
      <EditCompanySheet
        visible
        form={{ ...VALID_FORM, edrpou: '123456789' }}
        onChange={jest.fn()}
        onClose={jest.fn()}
        onSave={jest.fn()}
        isSaving={false}
      />,
    );
    fireEvent(screen.getByDisplayValue('123456789'), 'blur');
    expect(screen.getByText('company.edrpouInvalidFormat')).toBeTruthy();

    rerender(
      <EditCompanySheet
        visible={false}
        form={{ ...VALID_FORM, edrpou: '123456789' }}
        onChange={jest.fn()}
        onClose={jest.fn()}
        onSave={jest.fn()}
        isSaving={false}
      />,
    );
    rerender(
      <EditCompanySheet
        visible
        form={{ ...VALID_FORM, edrpou: '123456789' }}
        onChange={jest.fn()}
        onClose={jest.fn()}
        onSave={jest.fn()}
        isSaving={false}
      />,
    );

    expect(screen.queryByText('company.edrpouInvalidFormat')).toBeNull();
  });
});