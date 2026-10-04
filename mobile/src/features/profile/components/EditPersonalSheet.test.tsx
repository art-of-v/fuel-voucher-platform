import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react-native';

import { EditPersonalSheet } from './EditPersonalSheet';

jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: (selector?: (s: any) => unknown) => {
    const state = { t: (key: string) => key, language: 'uk', setLanguage: jest.fn() };
    return selector ? selector(state) : state;
  },
}));

/**
 * The point of this suite is the seam that the refactor had to get right: the
 * form state is **owned by the screen**, not by the sheet, because a React Native
 * `Modal` does not mount its children while hidden. If that ever inverts, an
 * unsaved edit is silently discarded on dismiss — and nothing else would catch it.
 */
describe('EditPersonalSheet', () => {
  const form = { firstName: '', lastName: '', birthdate: '' };

  it('shows the values the screen passes in, not its own', () => {
    render(
      <EditPersonalSheet
        visible
        form={{ ...form, firstName: 'Іван', lastName: 'Петренко' }}
        onChange={jest.fn()}
        onClose={jest.fn()}
        onSave={jest.fn()}
        isSaving={false}
      />,
    );

    expect(screen.getByDisplayValue('Іван')).toBeTruthy();
    expect(screen.getByDisplayValue('Петренко')).toBeTruthy();
  });

  it('reports edits upward instead of holding them', () => {
    const onChange = jest.fn();
    // `lastName` is filled so that exactly one field is empty — both empty fields
    // would make `getByDisplayValue('')` ambiguous.
    render(
      <EditPersonalSheet
        visible
        form={{ ...form, lastName: 'Петренко' }}
        onChange={onChange}
        onClose={jest.fn()}
        onSave={jest.fn()}
        isSaving={false}
      />,
    );

    fireEvent.changeText(screen.getByDisplayValue(''), 'Іван');

    // The sheet must hand the whole form back, not a partial patch: the screen
    // replaces its state with whatever it receives.
    expect(onChange).toHaveBeenCalledWith({ ...form, lastName: 'Петренко', firstName: 'Іван' });
  });

  it('saves what the screen currently holds', () => {
    const onSave = jest.fn();
    render(
      <EditPersonalSheet
        visible
        form={{ ...form, firstName: 'Іван' }}
        onChange={jest.fn()}
        onClose={jest.fn()}
        onSave={onSave}
        isSaving={false}
      />,
    );

    fireEvent.press(screen.getByRole('button', { name: 'common.save' }));

    expect(onSave).toHaveBeenCalledTimes(1);
  });

  it('renders nothing at all while hidden, because the Modal is not mounted', () => {
    render(
      <EditPersonalSheet
        visible={false}
        form={form}
        onChange={jest.fn()}
        onClose={jest.fn()}
        onSave={jest.fn()}
        isSaving={false}
      />,
    );

    // This is the behaviour the whole state-ownership decision rests on. If React
    // Native ever mounts Modal children while hidden, form state moves into this
    // component and this assertion fails — which is the point.
    expect(screen.queryByRole('button', { name: 'common.save' })).toBeNull();
  });
});
