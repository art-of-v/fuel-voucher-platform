import * as React from "react"
import { Slot } from "@radix-ui/react-slot"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-lg text-sm font-medium transition-all duration-200 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:size-4 [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        default: "bg-gradient-to-b from-primary to-emerald-500 text-primary-foreground border border-primary/60 shadow-[0_0_18px_rgba(0,224,116,0.3),inset_0_1px_0_rgba(255,255,255,0.35)] hover:brightness-110 hover:shadow-[0_0_26px_rgba(0,224,116,0.45)]",
        destructive: "bg-gradient-to-b from-rose-500 to-rose-600 text-destructive-foreground border border-rose-400/40 shadow-[0_0_16px_rgba(244,63,94,0.25),inset_0_1px_0_rgba(255,255,255,0.25)] hover:brightness-110",
        outline: "glass-input border bg-white/5 hover:bg-white/10 shadow-xs",
        secondary: "border border-white/12 bg-white/8 text-foreground backdrop-blur-md hover:bg-white/14",
        ghost: "border border-transparent hover:bg-white/8",
        link: "text-primary underline-offset-4 hover:underline",
      },
      size: {
        default: "min-h-9 px-4 py-2",
        sm: "min-h-8 rounded-lg px-3 text-xs",
        lg: "min-h-10 rounded-lg px-8",
        icon: "h-9 w-9",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean
}

const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, ...props }, ref) => {
    const Comp = asChild ? Slot : "button"
    return (
      <Comp
        className={cn(buttonVariants({ variant, size, className }))}
        ref={ref}
        {...props}
      />
    )
  }
)
Button.displayName = "Button"

export { Button, buttonVariants }
