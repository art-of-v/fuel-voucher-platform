import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DecimalSettingInput } from './DecimalSettingInput';

describe('DecimalSettingInput', () => {
  it('lets the field be emptied and retyped instead of snapping back to a digit', async () => {
    // The bug this component exists for: with `onChange={e => onCommit(parseFloat(e.target.value) || 0)}`,
    // deleting the last character yields "", parseFloat("") is NaN, `|| 0` turns it into 0, and the field
    // snaps back to a digit before the replacement can be typed. Changing 5 to 3 then means first typing
    // through 0.
    const user = userEvent.setup();
    const onCommit = vi.fn();
    render(<DecimalSettingInput value={5} onCommit={onCommit} aria-label="знижка" />);

    const field = screen.getByLabelText('знижка');
    await user.clear(field);
    expect(field).toHaveValue('');

    await user.type(field, '3');
    expect(field).toHaveValue('3');

    await user.tab();
    expect(onCommit).toHaveBeenCalledWith(3);
  });

  it('mirrors the committed value while it is not being edited', () => {
    const onCommit = vi.fn();
    render(<DecimalSettingInput value={2.5} onCommit={onCommit} aria-label="знижка" />);

    expect(screen.getByLabelText('знижка')).toHaveValue('2.5');
  });

  it('starts from empty on focus so the first keystroke replaces the number', async () => {
    // The trap this avoids: focusing a field showing "5" and typing "3" leaves "53", which parses to
    // 53 UAH per litre - a silent, entirely plausible-looking misconfiguration.
    const user = userEvent.setup();
    const onCommit = vi.fn();
    render(<DecimalSettingInput value={5} onCommit={onCommit} aria-label="знижка" />);

    await user.click(screen.getByLabelText('знижка'));
    await user.type(screen.getByLabelText('знижка'), '3');
    await user.tab();

    expect(onCommit).toHaveBeenCalledWith(3);
  });

  it('never commits an empty field, so a NaN discount cannot reach the server', async () => {
    // Since editing starts from empty on focus, "left empty" and "never touched" are the same state — and
    // both mean "no change". That is the safe direction: a discount of NaN would poison the pricing
    // comparison server-side, whereas an untouched field simply keeps its value.
    const user = userEvent.setup();
    const onCommit = vi.fn();
    render(<DecimalSettingInput value={5} onCommit={onCommit} aria-label="знижка" />);

    await user.click(screen.getByLabelText('знижка'));
    await user.tab();

    expect(onCommit).not.toHaveBeenCalled();
  });

  it('commits an explicit zero', async () => {
    // A zero discount is a real choice: it leaves the tier enabled but unsellable, so a manager can arm a
    // term before pricing it.
    const user = userEvent.setup();
    const onCommit = vi.fn();
    render(<DecimalSettingInput value={5} onCommit={onCommit} aria-label="знижка" />);

    await user.click(screen.getByLabelText('знижка'));
    await user.type(screen.getByLabelText('знижка'), '0');
    await user.tab();

    expect(onCommit).toHaveBeenCalledWith(0);
  });

  it('accepts a comma as the decimal separator', async () => {
    // The admin panel is used in Ukrainian and German locales where the keyboard offers a comma, and
    // parseFloat("0,5") is NaN — so an unfound locale would silently store 0 instead of 0.5.
    const user = userEvent.setup();
    const onCommit = vi.fn();
    render(<DecimalSettingInput value={0} onCommit={onCommit} aria-label="знижка" />);

    await user.type(screen.getByLabelText('знижка'), '0,5');
    await user.tab();

    expect(onCommit).toHaveBeenCalledWith(0.5);
  });

  it('commits 0 for a negative value rather than storing it', async () => {
    // The server clamps a negative discount to zero as well; doing it here keeps the field from showing a
    // number that will not survive a save.
    const user = userEvent.setup();
    const onCommit = vi.fn();
    render(<DecimalSettingInput value={5} onCommit={onCommit} aria-label="знижка" />);

    await user.type(screen.getByLabelText('знижка'), '-3');
    await user.tab();

    expect(onCommit).toHaveBeenCalledWith(0);
  });

  it('does not commit anything when the field was never touched', async () => {
    // Saving the settings form must not rewrite every tier it merely displayed.
    const user = userEvent.setup();
    const onCommit = vi.fn();
    render(<DecimalSettingInput value={5} onCommit={onCommit} aria-label="знижка" />);

    await user.click(screen.getByLabelText('знижка'));
    await user.tab();

    expect(onCommit).not.toHaveBeenCalled();
  });
});
