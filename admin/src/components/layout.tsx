import { ReactNode, useCallback, useEffect, useRef, useState } from "react";
import {
  Archive,
  BarChart3,
  Building,
  ShoppingCart,
  Users,
  Menu,
  Ticket,
  X,
  FileSignature,
  FileCheck,
  ScrollText,
  Bug,
  LogOut,
  Settings,
  MapPin,
  Replace,
  RefreshCw,
  Handshake,
  MessageSquare,
  Wallet,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useI18n } from "@/lib/i18n";
import { LanguageSwitcher } from "@/components/LanguageSwitcher";
import { ThemeSwitcher } from "@/components/ThemeSwitcher";
import { LionMark } from "@/components/LionMark";
import { useTheme } from "@/lib/theme-store";
import { themes } from "@/lib/themes";
import type { CurrentUser } from "@/lib/admin-auth";

// Sidebar width, in pixels. The default is sized so the longest label («Продовження оператором»)
// fits on ONE line in the app's real font — it needs about 206px, and the chrome around the text
// costs another 72, so anything under ~278px truncates it. The harness could not show this: its
// fallback font is narrower, and at w-64 the label fit there while it does not here.
const SIDEBAR_WIDTH_KEY = "fuelflow.sidebarWidth";
const SIDEBAR_DEFAULT_PX = 296;
const SIDEBAR_MIN_PX = 200;
const SIDEBAR_MAX_PX = 440;

const clampSidebarWidth = (px: number) =>
  Math.min(SIDEBAR_MAX_PX, Math.max(SIDEBAR_MIN_PX, Math.round(px)));

const readStoredSidebarWidth = () => {
  if (typeof window === "undefined") return SIDEBAR_DEFAULT_PX;
  const stored = Number(window.localStorage.getItem(SIDEBAR_WIDTH_KEY));
  return Number.isFinite(stored) &&
    stored >= SIDEBAR_MIN_PX &&
    stored <= SIDEBAR_MAX_PX
    ? stored
    : SIDEBAR_DEFAULT_PX;
};

interface SidebarProps {
  activeTab: string;
  onTabChange: (tab: string) => void;
  onLogout: () => void;
  className?: string;
  onClose?: () => void;
  user?: CurrentUser | null;
  badges?: Record<string, number>;
  /** Pixels. Undefined keeps the fixed width, which is what the mobile drawer wants. */
  width?: number;
  onWidthChange?: (px: number) => void;
}

const Sidebar = ({
  activeTab,
  onTabChange,
  onLogout,
  className,
  onClose,
  user,
  badges,
  width,
  onWidthChange,
}: SidebarProps) => {
  const { t } = useI18n();
  const theme = useTheme((s) => s.theme);
  const isForge = themes[theme].forge;

  // Drag-to-resize. Pointer capture keeps the drag alive when the cursor outruns the 8px handle,
  // which is the whole reason a mouse drag on a thin edge otherwise feels broken.
  const asideRef = useRef<HTMLElement | null>(null);
  const [isResizing, setIsResizing] = useState(false);
  const resizable = typeof onWidthChange === "function";

  const applyWidth = useCallback(
    (px: number) => onWidthChange?.(clampSidebarWidth(px)),
    [onWidthChange],
  );

  useEffect(() => {
    if (!isResizing) return;
    const onMove = (e: PointerEvent) => {
      const left = asideRef.current?.getBoundingClientRect().left ?? 0;
      applyWidth(e.clientX - left);
    };
    const stop = () => setIsResizing(false);
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", stop);
    window.addEventListener("pointercancel", stop);
    // Otherwise the drag selects the whole page behind the cursor.
    const previousUserSelect = document.body.style.userSelect;
    document.body.style.userSelect = "none";
    return () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", stop);
      window.removeEventListener("pointercancel", stop);
      document.body.style.userSelect = previousUserSelect;
    };
  }, [isResizing, applyWidth]);

  // Persist on change rather than on drag end, so a reload mid-drag keeps what was on screen.
  useEffect(() => {
    if (typeof width === "number" && !isResizing) {
      window.localStorage.setItem(
        SIDEBAR_WIDTH_KEY,
        String(clampSidebarWidth(width)),
      );
    }
  }, [width, isResizing]);

  const navItems = [
    { id: "brands", label: t("nav.brands"), icon: Building },
    { id: "purchases", label: t("nav.purchases"), icon: ShoppingCart },
    { id: "users", label: t("nav.users"), icon: Users },
    { id: "vouchers", label: t("nav.vouchers"), icon: Ticket },
    { id: "imports", label: t("nav.imports"), icon: Archive },
    { id: "voucherExchange", label: t("nav.voucherExchange"), icon: Replace },
    { id: "suppliers", label: t("nav.suppliers"), icon: Handshake },
    { id: "stationNodes", label: t("nav.stationNodes"), icon: MapPin },
    { id: "customerRenewal", label: t("nav.customerRenewal"), icon: RefreshCw },
    { id: "contracts", label: t("nav.contracts"), icon: FileSignature },
    { id: "reconciliation", label: t("nav.reconciliation"), icon: FileCheck },
    // Before the audit log on purpose: money that is held and unexplained is the most urgent
    // thing in this panel, and burying it under Logs invites nobody to open it.
    { id: "heldPayments", label: t("nav.heldPayments"), icon: Wallet },
    { id: "auditlog", label: t("nav.auditlog"), icon: ScrollText },
    { id: "errorlogs", label: t("nav.errorlogs"), icon: Bug },
    { id: "feedback", label: t("nav.feedback"), icon: MessageSquare },
    { id: "reports", label: t("nav.reports"), icon: BarChart3 },
    { id: "settings", label: t("nav.settings"), icon: Settings },
  ];

  return (
    <aside
      ref={asideRef}
      style={typeof width === "number" ? { width } : undefined}
      className={cn(
        "glass-chrome rounded-2xl flex flex-col h-full shrink-0 overflow-hidden relative z-10",
        typeof width === "number" ? "" : "w-64",
        isResizing && "select-none",
        className,
      )}
    >
      {/* The brand lockup is the one element allowed to be loud. The band is tall
                    enough to seat the mark on its own plate, so the lion reads as a
                    logo rather than an icon squeezed beside a caption. */}
      <div className="h-24 px-5 flex items-center justify-between border-b border-border">
        <div className="flex items-center gap-4 min-w-0">
          {isForge ? (
            /* The chrome lion replaces the accent bar on every forge
                               theme — it is the one mark the machined header suits.
                               The plate is what gives it presence: on forge themes it
                               picks up the same bevel as every other raised surface. */
            <div className="shrink-0 bg-card border border-border p-1.5">
              <LionMark size={56} />
            </div>
          ) : (
            <div className="w-2.5 h-10 bg-primary shadow-glow shrink-0"></div>
          )}
          <div className="min-w-0">
            <h1 className="text-xl font-bold tracking-tight leading-none text-foreground truncate">
              <span className="glass-text-gradient">{t("app.title")}</span>
            </h1>
            <div className="flex flex-col gap-1.5 mt-2">
              <p className="wordmark-sub leading-none">{t("app.admin")}</p>
              {/* One hit of brand colour, under the name and left-
                                    aligned with it: the eye lands on the lockup
                                    instead of sliding past it. */}
              <span
                className="h-[3px] w-12 bg-primary"
                aria-hidden="true"
              ></span>
            </div>
          </div>
        </div>
        {onClose && (
          <button
            onClick={onClose}
            className="md:hidden p-2 text-muted-foreground hover:text-foreground"
          >
            <X className="w-5 h-5" />
          </button>
        )}
      </div>

      <nav className="flex-1 p-4 space-y-1 overflow-y-auto">
        <div className="px-2 mb-2">
          <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
            {t("nav.management")}
          </span>
        </div>
        {navItems.map((item) => {
          const isActive = activeTab === item.id;
          const badgeCount = badges?.[item.id] ?? 0;
          return (
            <button
              key={item.id}
              onClick={() => {
                onTabChange(item.id);
                if (onClose) onClose();
              }}
              // Full label on hover, so a narrowed sidebar never hides a destination.
              title={item.label}
              className={cn(
                "w-full flex items-center gap-3 px-3 py-2 text-sm font-medium rounded-lg transition-all duration-200",
                isActive
                  ? "bg-primary/15 text-primary glass-glow border border-primary/25"
                  : "border border-transparent text-muted-foreground hover:bg-foreground/5 hover:text-foreground",
              )}
            >
              <item.icon
                className={cn(
                  "w-4 h-4 shrink-0",
                  isActive ? "text-primary" : "text-muted-foreground",
                )}
              />
              {/* `min-w-0` so the flex item may actually shrink (its default
                                min-width:auto refuses to), and `break-words` so a word longer than
                                a narrowed sidebar breaks instead of overflowing. Deliberately NOT
                                truncated: the default width fits every label on one line, and if the
                                reader narrows it themselves, wrapping to a second line is a better
                                answer than a menu item that reads «Продовження операто…». */}
              <span className="min-w-0 flex-1 text-left break-words">
                {item.label}
              </span>
              {badgeCount > 0 && (
                <span className="ml-auto min-w-[1.25rem] shrink-0 px-1.5 py-0.5 text-[10px] font-bold leading-none text-center rounded-full bg-destructive text-destructive-foreground tabular-nums">
                  {badgeCount > 99 ? "99+" : badgeCount}
                </span>
              )}
            </button>
          );
        })}
      </nav>

      <div className="p-4 border-t border-border mt-auto">
        <div className="flex items-center gap-3 px-3 py-2">
          <div className="w-8 h-8 rounded-full bg-primary/20 flex items-center justify-center border border-border">
            <span className="text-xs font-bold text-foreground">
              {user
                ? (user.firstName?.[0] ??
                  user.lastName?.[0] ??
                  user.phone.slice(-2))
                : "AD"}
            </span>
          </div>
          <div className="overflow-hidden flex-1">
            <p className="text-sm font-medium text-foreground truncate">
              {user
                ? [user.firstName, user.lastName].filter(Boolean).join(" ") ||
                  user.phone
                : t("user.name")}
            </p>
            <p className="text-xs text-muted-foreground truncate">
              {user ? user.userType : t("user.role")}
            </p>
          </div>
          {user && (
            <button
              onClick={onLogout}
              className="p-1.5 text-muted-foreground hover:text-destructive transition-colors rounded-md hover:bg-destructive/10"
              title="Logout"
            >
              <LogOut className="w-4 h-4" />
            </button>
          )}
        </div>
      </div>

      {resizable && (
        <div
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize sidebar"
          aria-valuenow={typeof width === "number" ? width : undefined}
          aria-valuemin={SIDEBAR_MIN_PX}
          aria-valuemax={SIDEBAR_MAX_PX}
          tabIndex={0}
          onPointerDown={(e) => {
            // Only the primary button starts a resize, so a right-click on the edge
            // still opens the context menu.
            if (e.button !== 0) return;
            e.preventDefault();
            setIsResizing(true);
          }}
          // Keyboard resize, because a mouse-only handle is unreachable without one.
          onKeyDown={(e) => {
            const current =
              typeof width === "number" ? width : SIDEBAR_DEFAULT_PX;
            const step = e.shiftKey ? 32 : 8;
            if (e.key === "ArrowLeft") {
              applyWidth(current - step);
              e.preventDefault();
            }
            if (e.key === "ArrowRight") {
              applyWidth(current + step);
              e.preventDefault();
            }
          }}
          className={cn(
            "absolute right-0 top-0 bottom-0 w-2 cursor-col-resize z-20 touch-none",
            "hover:bg-primary/20 focus-visible:bg-primary/30 focus-visible:outline-none",
            isResizing && "bg-primary/30",
          )}
        />
      )}
    </aside>
  );
};

interface LayoutProps {
  children: ReactNode;
  activeTab: string;
  onTabChange: (tab: string) => void;
  onLogout: () => void;
  user?: CurrentUser | null;
  badges?: Record<string, number>;
}

export const Layout = ({
  children,
  activeTab,
  onTabChange,
  onLogout,
  user,
  badges,
}: LayoutProps) => {
  const { t } = useI18n();
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const [sidebarWidth, setSidebarWidth] = useState(readStoredSidebarWidth);

  const toggleMobileMenu = () => setIsMobileMenuOpen(!isMobileMenuOpen);

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-background text-foreground relative">
      {/* Ambient aurora behind all glass surfaces */}
      <div className="aurora-bg" aria-hidden="true">
        <div className="aurora-blob aurora-blob--green" />
        <div className="aurora-blob aurora-blob--cyan" />
        <div className="aurora-blob aurora-blob--teal" />
      </div>

      {/* Desktop Sidebar */}
      <Sidebar
        activeTab={activeTab}
        onTabChange={onTabChange}
        onLogout={onLogout}
        className="hidden md:flex my-3 ml-3"
        user={user}
        badges={badges}
        width={sidebarWidth}
        onWidthChange={setSidebarWidth}
      />

      {/* Mobile Sidebar Overlay */}
      {isMobileMenuOpen && (
        <div
          className="fixed inset-0 bg-black/60 backdrop-blur-sm z-40 md:hidden"
          onClick={() => setIsMobileMenuOpen(false)}
        />
      )}

      {/* Mobile Sidebar Drawer */}
      <div
        className={cn(
          "fixed inset-y-0 left-0 z-50 transform transition-transform duration-300 ease-in-out md:hidden",
          isMobileMenuOpen ? "translate-x-0" : "-translate-x-full",
        )}
      >
        <Sidebar
          activeTab={activeTab}
          onTabChange={onTabChange}
          onLogout={onLogout}
          onClose={() => setIsMobileMenuOpen(false)}
          user={user}
          badges={badges}
        />
      </div>

      {/* Main Content Area */}
      <div className="flex flex-1 flex-col overflow-hidden min-w-0 relative z-10 md:py-3 md:pr-3 md:pl-0">
        {/* Header */}
        <header className="h-16 glass-chrome md:rounded-2xl flex items-center justify-between px-4 md:px-6 shrink-0">
          <div className="flex items-center gap-2 md:gap-4">
            <button
              onClick={toggleMobileMenu}
              className="p-2 -ml-2 text-muted-foreground hover:text-foreground md:hidden"
            >
              <Menu className="w-6 h-6" />
            </button>
            <span className="text-sm font-medium text-muted-foreground hidden md:inline-block">
              / {t("header.dashboard")} / {activeTab}
            </span>
            <span className="md:hidden font-bold truncate max-w-[150px]">
              {t("app.title")}
            </span>
          </div>
          <div className="flex items-center gap-2 md:gap-4">
            <ThemeSwitcher />
            <LanguageSwitcher />
            <button className="hidden sm:inline-block text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
              {t("header.docs")}
            </button>
            <button className="hidden sm:inline-block text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
              {t("header.support")}
            </button>
          </div>
        </header>

        {/* Content */}
        <main className="flex-1 overflow-y-auto p-4 md:p-6 relative z-10">
          {/* No max-w constraint, fully filling the available space */}
          <div className="h-full w-full animate-in fade-in duration-300">
            {children}
          </div>
        </main>
      </div>
    </div>
  );
};
