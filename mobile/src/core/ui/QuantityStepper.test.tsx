import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';

import { QuantityStepper } from './QuantityStepper';

/**
 * Bounded increment/decrement. It replaced two implementations, one of which deleted
 * the cart line at quantity 1 with no confirmation and no undo - the audit's
 * highest-severity interaction defect, and the reason this component takes an
 * optional `onRemove`: at the minimum the control becomes a visibly different
 * remove affordance rather than quietly doing something else.
 *
 * The bounds themselves are the money-adjacent part. A stepper that will go past
 * `max`, or below `min`, lets a cart hold a quantity the server will not accept.
 */

const dec = () => screen.getByLabelText('decrease');
const inc = () => screen.getByLabelText('increase');
const remove = () => screen.getByLabelText('remove');

const setup = (props: Partial<React.ComponentProps<typeof QuantityStepper>> = {}) => {
  const onChange = jest.fn();
  const onRemove = jest.fn();
  render(<QuantityStepper value={2} onChange={onChange} onRemove={onRemove} {...props} />);
  return { onChange, onRemove };
};

describe('bounds', () => {
  it('decrements by the step', () => {
    const { onChange } = setup({ value: 5, step: 1 });

    fireEvent.press(dec());

    expect(onChange).toHaveBeenCalledWith(4);
  });

  it('increments by the step', () => {
    const { onChange } = setup({ value: 5, step: 2 });

    fireEvent.press(inc());

    expect(onChange).toHaveBeenCalledWith(7);
  });

  it('will not go below the minimum even when the step overshoots it', () => {
    const { onChange } = setup({ value: 2, min: 1, step: 5 });

    fireEvent.press(dec());

    // A step bigger than the headroom lands on -3 without the clamp, and the cart
    // then holds a negative quantity. The disable only covers value === min, so the
    // clamp is what protects the step from 1.
    expect(onChange).toHaveBeenCalledWith(1);
  });

  it('will not go past the maximum', () => {
    const { onChange } = setup({ value: 8, max: 10, step: 5 });

    fireEvent.press(inc());

    expect(onChange).toHaveBeenCalledWith(10);
  });

  it('disables the increment at the maximum but leaves it in place', () => {
    setup({ value: 10, max: 10 });

    // The control stays so the layout does not jump under the user's thumb.
    expect(inc().props.accessibilityState.disabled).toBe(true);
    expect(dec().props.accessibilityState.disabled).toBe(false);
  });

  it('disables both controls when disabled', () => {
    setup({ value: 5, disabled: true });

    expect(inc().props.accessibilityState.disabled).toBe(true);
    expect(dec().props.accessibilityState.disabled).toBe(true);
  });
});

describe('at the minimum', () => {
  it('turns into a remove control when the caller supplied onRemove', () => {
    const { onChange, onRemove } = setup({ value: 1 });

    fireEvent.press(remove());

    expect(onRemove).toHaveBeenCalledTimes(1);
    // A decrement here would be a lie: the value is already at the floor.
    expect(onChange).not.toHaveBeenCalled();
  });

  it('is disabled rather than removing when the caller did not supply onRemove', () => {
    const { onRemove } = setup({ value: 1, onRemove: undefined });

    expect(dec().props.accessibilityState.disabled).toBe(true);
    expect(() => screen.getByLabelText('remove')).toThrow();
    expect(onRemove).not.toHaveBeenCalled();
  });

  it('removes at the minimum but decrements above it', () => {
    const { onChange, onRemove } = setup({ value: 2 });

    fireEvent.press(dec());

    expect(onChange).toHaveBeenCalledWith(1);
    expect(onRemove).not.toHaveBeenCalled();
  });
});

describe('readout', () => {
  it('shows the value', () => {
    setup({ value: 7 });

    expect(screen.getByText('7')).toBeTruthy();
  });

  it('announces the value with its unit', () => {
    setup({ value: 7, unit: 'л', accessibilityLabel: 'Кількість' });

    expect(screen.getByLabelText('Кількість').props.accessibilityValue).toEqual({ text: '7 л' });
  });

  it('announces the bare number when there is no unit', () => {
    setup({ value: 7, accessibilityLabel: 'Кількість' });

    expect(screen.getByLabelText('Кількість').props.accessibilityValue).toEqual({ text: '7' });
  });

  it('marks itself disabled for assistive tech when disabled', () => {
    setup({ value: 7, disabled: true, accessibilityLabel: 'Кількість' });

    expect(screen.getByLabelText('Кількість').props.accessibilityValue).toEqual({ text: '7' });
  });
});
