/**
 * The design system's public surface.
 *
 * Screens should import from `@/core/ui` and nowhere deeper. If a screen needs a
 * style that no component here provides, that is a gap in the system — extend the
 * component, do not write the style in the screen.
 *
 * Grouped by role rather than alphabetically so the shape of the system is
 * readable: layout, then typography, then controls, then surfaces, then data,
 * then feedback.
 */

/* Layout ------------------------------------------------------------------- */
export { PageLayout, useContentInsets } from './PageLayout';
export type { ContentInsets, PageLayoutProps } from './PageLayout';
export { GridPageLayout } from './GridPageLayout';
export { ScreenHeader } from './ScreenHeader';
export type { ScreenHeaderProps } from './ScreenHeader';
export { SectionHeader } from './SectionHeader';
export type { SectionHeaderProps } from './SectionHeader';
export { Divider } from './Divider';
export type { DividerProps } from './Divider';

/* Typography --------------------------------------------------------------- */
export { Text } from './Text';
export type { TextProps, TextTone } from './Text';

/* Controls ----------------------------------------------------------------- */
export { Button } from './Button';
export type { ButtonProps, ButtonSize, ButtonVariant } from './Button';
export { IconButton } from './IconButton';
export type { IconButtonProps, IconButtonSize, IconButtonVariant } from './IconButton';
export { Chip } from './Chip';
export type { ChipProps } from './Chip';
export { QuantityStepper } from './QuantityStepper';
export type { QuantityStepperProps } from './QuantityStepper';
export { PressableScale } from './PressableScale';
export type { PressableScaleProps } from './PressableScale';

/* Inputs ------------------------------------------------------------------- */
export { TextField, FieldShell } from './TextField';
export type { TextFieldProps } from './TextField';
export { Select } from './Select';
export type { SelectOption, SelectProps } from './Select';

/* Surfaces ----------------------------------------------------------------- */
export { Card } from './Card';
export type { CardProps, CardTone } from './Card';
export { ListItem } from './ListItem';
export type { ListItemProps } from './ListItem';
export { BottomSheet } from './BottomSheet';
export type { BottomSheetProps } from './BottomSheet';

/* Data display ------------------------------------------------------------- */
export { Badge } from './Badge';
export type { BadgeProps, BadgeStatus } from './Badge';
export { Price } from './Price';
export type { PriceProps, PriceSize } from './Price';
export { StatTile } from './StatTile';
export type { StatTileProps } from './StatTile';

/* Feedback ----------------------------------------------------------------- */
export { InlineFeedback } from './InlineFeedback';
export type { FeedbackKind, InlineFeedbackProps } from './InlineFeedback';
export { ConfirmDialog } from './ConfirmDialog';
export type { ConfirmDialogProps, ConfirmTone } from './ConfirmDialog';
export { ToastHost, toast, useToastStore } from './Toast';
export type { ToastHostProps } from './Toast';
export { EmptyState } from './EmptyState';
export type { EmptyStateProps } from './EmptyState';
export { LoadingState } from './LoadingState';
export type { LoadingStateProps } from './LoadingState';
export { ErrorState } from './ErrorState';
export type { ErrorStateProps } from './ErrorState';
export { ErrorBoundary } from './ErrorBoundary';

/* Deprecated --------------------------------------------------------------- */
/**
 * @deprecated A decorative hexagonal mesh drawn inside a card, behind its content.
 * `Card` has no pattern slot by design — a content surface is colour plus a
 * hairline. Five live call sites remain (`my-codes`, `VoucherCard`,
 * `VoucherDetailModal`, `FuelCard`, `PackageCard`); see the note on the component
 * itself. Do not add new ones.
 */
export { MeshBackground } from './MeshBackground';
/**
 * @deprecated A neon-glow + 40px grid backdrop. Ruled out by the redesign (it sits
 * behind money and contract screens). Still live as `GridPageLayout`'s default plus
 * four explicit call sites. Prefer the base `PageLayout` with no background.
 */
export { GridBackground } from './GridBackground';
