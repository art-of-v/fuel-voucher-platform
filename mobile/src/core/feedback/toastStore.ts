import { create } from 'zustand';
import type { FeedbackKind } from '../ui/InlineFeedback';

export interface ToastAction {
  label: string;
  onPress: () => void;
}

export interface ToastPayload {
  kind: FeedbackKind;
  message: string;
  /** Milliseconds on screen. Defaults by kind — see `DEFAULT_DURATION`. */
  duration?: number;
  /** A single action, typically "Undo" or "View". */
  action?: ToastAction;
}

interface ToastState {
  current: (ToastPayload & { id: number }) | null;
  show: (payload: ToastPayload) => void;
  hide: () => void;
}

/**
 * Default dwell times. Errors stay longer because they carry information the user
 * has to act on; success is a confirmation of something they just did and can be
 * brief.
 */
export const DEFAULT_DURATION: Record<FeedbackKind, number> = {
  success: 2600,
  info: 3000,
  warning: 4000,
  danger: 5000,
};

let counter = 0;

/**
 * Transient feedback queue.
 *
 * A store rather than pure React context so that non-component code — the API
 * layer, a zustand action, a deep-link handler — can report an outcome without
 * threading a hook through. That was the practical reason `Alert.alert` spread
 * through this codebase: it was the only thing callable from anywhere.
 *
 * One toast at a time. A new toast replaces the current one rather than stacking,
 * because a stack of toasts covers the content it is describing and the user
 * cannot read them in the time they are given.
 */
export const useToastStore = create<ToastState>((set) => ({
  current: null,
  show: (payload) => set({ current: { ...payload, id: ++counter } }),
  hide: () => set({ current: null }),
}));

/**
 * Report a transient outcome from anywhere.
 *
 * Choose the mechanism deliberately:
 * - `toast.success` — something visibly completed and needs no follow-up.
 * - `toast.error`   — something failed but the screen is still usable. If the
 *                     failure belongs to a field or a section, use
 *                     `InlineFeedback` there instead.
 * - `ConfirmDialog` — you must block until the user decides.
 * - `InlineFeedback`— the message must persist or be re-readable.
 *
 * Do not toast every interaction. Adding an item to the cart should update the
 * cart, not announce itself.
 */
export const toast = {
  show: (payload: ToastPayload) => useToastStore.getState().show(payload),
  success: (message: string, action?: ToastAction) =>
    useToastStore.getState().show({ kind: 'success', message, action }),
  error: (message: string, action?: ToastAction) =>
    useToastStore.getState().show({ kind: 'danger', message, action }),
  warning: (message: string, action?: ToastAction) =>
    useToastStore.getState().show({ kind: 'warning', message, action }),
  info: (message: string, action?: ToastAction) =>
    useToastStore.getState().show({ kind: 'info', message, action }),
  hide: () => useToastStore.getState().hide(),
};
