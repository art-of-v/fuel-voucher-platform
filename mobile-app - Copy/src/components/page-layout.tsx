import React from "react";
import { cn } from "@/lib/utils";

interface PageLayoutProps {
    children: React.ReactNode;
    header?: React.ReactNode;
    fixedFooter?: React.ReactNode;
    background?: React.ReactNode;
    className?: string;
    scrollClassName?: string;
    disableScroll?: boolean;
}

export function PageLayout({
    children,
    header,
    fixedFooter,
    background,
    className,
    scrollClassName,
    disableScroll = false
}: PageLayoutProps) {
    return (
        <div className={cn(
            "flex flex-col h-full w-full overflow-hidden relative",
            // Add padding bottom equal to nav height so the fixedFooter (absolute or flex end) 
            // is pushed up above the global nav.
            "pb-[var(--nav-height)]",
            className
        )}>
            {/* Fixed Background Region */}
            {background && (
                <div className="absolute inset-0 pointer-events-none z-0">
                    {background}
                </div>
            )}

            {/* Fixed Header Region */}
            {header && (
                <div className="shrink-0 z-40 relative">
                    {header}
                </div>
            )}

            {/* Scrollable Content Region */}
            <div
                className={cn(
                    "flex-1 relative",
                    disableScroll ? "overflow-hidden" : "overflow-y-auto overflow-x-hidden scroll-smooth",
                    scrollClassName
                )}
                style={{ WebkitOverflowScrolling: 'touch' }}
            >
                {children}
            </div>

            {/* Fixed Footer Region (e.g. Checkout Bar) */}
            {/* This will now sit at the bottom of the flex container, which ends ABOVE the global nav due to padding */}
            {fixedFooter && (
                <div className="shrink-0 z-50 relative bg-background shadow-up border-t border-white/10">
                    {fixedFooter}
                </div>
            )}
        </div>
    );
}
