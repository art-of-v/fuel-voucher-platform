import * as React from "react";
import * as DialogPrimitive from "@radix-ui/react-dialog";
import { X } from "lucide-react";

import { cn } from "@/lib/utils";

/**
 * A panel that slides in from the right edge.
 *
 * <p>
 * <b>Why this exists as a shared component.</b> The app had no side panel at all: every dialog was a
 * hand-rolled {@code <div className="fixed inset-0">} with a click-on-the-overlay to dismiss. That
 * shape has no focus trap, no Escape handling, no {@code role="dialog"} and no
 * {@code aria-modal} — so keyboard users could tab straight out of an open dialog into the page behind
 * it, and screen readers were not told a dialog was open at all. {@code @radix-ui/react-dialog} was
 * already a dependency and already unused, so this is the component the codebase was missing rather
 * than a new one to install.
 * </p>
 *
 * <p>
 * <b>Why not just another inline overlay.</b> A lifecycle timeline is long and scrollable, which is
 * exactly the case where losing focus or being unable to close with Escape costs the most. The
 * accessibility comes from the primitive; the panel-from-the-edge look comes from the classes.
 * </p>
 */
const Sheet = DialogPrimitive.Root;
const SheetTrigger = DialogPrimitive.Trigger;
const SheetClose = DialogPrimitive.Close;
const SheetTitle = DialogPrimitive.Title;
const SheetDescription = DialogPrimitive.Description;

const SheetContent = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Content> & {
    /** Hides the built-in close button, for a panel that supplies its own. */
    hideClose?: boolean;
  }
>(({ className, children, hideClose, ...props }, ref) => (
  <DialogPrimitive.Portal>
    <DialogPrimitive.Overlay
      className={cn(
        "fixed inset-0 z-50 bg-black/60 backdrop-blur-md data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0",
        className,
      )}
    />
    <DialogPrimitive.Content
      ref={ref}
      className={cn(
        // glass-chrome (opaque dark) not glass-panel, for the same reason alert-dialog.tsx records:
        // as a portaled sibling stacked over the backdrop-filter overlay, glass-panel's translucent
        // surface composites to an invisible rectangle in Chromium.
        "glass-chrome text-foreground fixed inset-y-0 right-0 z-50 flex h-full w-full flex-col gap-0 border-l border-border p-0 shadow-2xl duration-300 focus:outline-none data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right sm:max-w-2xl",
        className,
      )}
      {...props}
    >
      {children}
      {!hideClose && (
        <DialogPrimitive.Close
          className="absolute right-4 top-4 rounded-md p-1 text-muted-foreground opacity-70 transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
          aria-label="Close"
        >
          <X className="h-4 w-4" />
        </DialogPrimitive.Close>
      )}
    </DialogPrimitive.Content>
  </DialogPrimitive.Portal>
));
SheetContent.displayName = DialogPrimitive.Content.displayName;

export {
  Sheet,
  SheetTrigger,
  SheetClose,
  SheetContent,
  SheetTitle,
  SheetDescription,
};
