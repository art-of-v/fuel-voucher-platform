import { ReactNode, useState } from "react";
import { Archive, Building, Fuel, Package, ShoppingCart, Users, QrCode, Menu, Ticket, X, FileSignature, FileCheck } from "lucide-react";
import { cn } from "@/lib/utils";
import { useI18n } from "@/lib/i18n";
import { LanguageSwitcher } from "@/components/LanguageSwitcher";
import type { CurrentUser } from "@/lib/admin-auth";

interface SidebarProps {
    activeTab: string;
    onTabChange: (tab: string) => void;
    className?: string;
    onClose?: () => void;
    user?: CurrentUser | null;
}

const Sidebar = ({ activeTab, onTabChange, className, onClose, user }: SidebarProps) => {
    const { t } = useI18n();

    const navItems = [
        { id: "stations", label: t("nav.stations"), icon: Building },
        { id: "fueltypes", label: t("nav.fueltypes"), icon: Fuel },
        { id: "packages", label: t("nav.packages"), icon: Package },
        { id: "purchases", label: t("nav.purchases"), icon: ShoppingCart },
        { id: "users", label: t("nav.users"), icon: Users },
        { id: "qrcodes", label: t("nav.qrcodes"), icon: QrCode },
        { id: "vouchers", label: t("nav.vouchers"), icon: Ticket },
        { id: "imports", label: "Імпорти", icon: Archive },
        { id: "contracts", label: "Договори", icon: FileSignature },
        { id: "reconciliation", label: "Аудит", icon: FileCheck },
    ];

    return (
        <aside className={cn("w-64 bg-card border-r border-border flex flex-col h-full shrink-0", className)}>
            <div className="h-16 px-6 flex items-center justify-between border-b border-border text-primary">
                <h1 className="text-xl font-bold tracking-tight text-foreground flex items-center gap-2">
                    <div className="w-2 h-6 bg-primary rounded-sm"></div>
                    {t('app.title')} <span className="text-muted-foreground font-normal">{t('app.admin')}</span>
                </h1>
                {onClose && (
                    <button onClick={onClose} className="md:hidden p-2 text-muted-foreground hover:text-foreground">
                        <X className="w-5 h-5" />
                    </button>
                )}
            </div>

            <nav className="flex-1 p-4 space-y-1 overflow-y-auto">
                <div className="px-2 mb-2">
                    <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">{t('nav.management')}</span>
                </div>
                {navItems.map((item) => {
                    const isActive = activeTab === item.id;
                    return (
                        <button
                            key={item.id}
                            onClick={() => {
                                onTabChange(item.id);
                                if (onClose) onClose();
                            }}
                            className={cn(
                                "w-full flex items-center gap-3 px-3 py-2 text-sm font-medium rounded-md transition-colors duration-200",
                                isActive
                                    ? "bg-primary/10 text-primary"
                                    : "text-muted-foreground hover:bg-muted hover:text-foreground"
                            )}
                        >
                            <item.icon className={cn("w-4 h-4", isActive ? "text-primary" : "text-muted-foreground")} />
                            {item.label}
                        </button>
                    );
                })}
            </nav>

            <div className="p-4 border-t border-border mt-auto">
                <div className="flex items-center gap-3 px-3 py-2">
                    <div className="w-8 h-8 rounded-full bg-muted flex items-center justify-center border border-border">
                        <span className="text-xs font-bold text-muted-foreground">
                            {user ? user.phone.slice(-2) : 'AD'}
                        </span>
                    </div>
                    <div className="overflow-hidden">
                        <p className="text-sm font-medium text-foreground truncate">
                            {user ? user.phone : t('user.name')}
                        </p>
                        <p className="text-xs text-muted-foreground truncate">
                            {user ? user.userType : t('user.role')}
                        </p>
                    </div>
                </div>
            </div>
        </aside>
    );
};

interface LayoutProps {
    children: ReactNode;
    activeTab: string;
    onTabChange: (tab: string) => void;
    user?: CurrentUser | null;
}

export const Layout = ({ children, activeTab, onTabChange, user }: LayoutProps) => {
    const { t } = useI18n();
    const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

    const toggleMobileMenu = () => setIsMobileMenuOpen(!isMobileMenuOpen);

    return (
        <div className="flex h-screen w-screen overflow-hidden bg-background text-foreground relative">
            {/* Desktop Sidebar */}
            <Sidebar activeTab={activeTab} onTabChange={onTabChange} className="hidden md:flex" user={user} />

            {/* Mobile Sidebar Overlay */}
            {isMobileMenuOpen && (
                <div
                    className="fixed inset-0 bg-black/60 backdrop-blur-sm z-40 md:hidden"
                    onClick={() => setIsMobileMenuOpen(false)}
                />
            )}

            {/* Mobile Sidebar Drawer */}
            <div className={cn(
                "fixed inset-y-0 left-0 z-50 transform transition-transform duration-300 ease-in-out md:hidden",
                isMobileMenuOpen ? "translate-x-0" : "-translate-x-full"
            )}>
                <Sidebar
                    activeTab={activeTab}
                    onTabChange={onTabChange}
                    onClose={() => setIsMobileMenuOpen(false)}
                    user={user}
                />
            </div>

            {/* Main Content Area */}
            <div className="flex flex-1 flex-col overflow-hidden min-w-0">
                {/* Header */}
                <header className="h-16 border-b border-border bg-card/50 flex items-center justify-between px-4 md:px-6 shrink-0 backdrop-blur-sm">
                    <div className="flex items-center gap-2 md:gap-4">
                        <button
                            onClick={toggleMobileMenu}
                            className="p-2 -ml-2 text-muted-foreground hover:text-foreground md:hidden"
                        >
                            <Menu className="w-6 h-6" />
                        </button>
                        <span className="text-sm font-medium text-muted-foreground hidden md:inline-block">/ {t('header.dashboard')} / {activeTab}</span>
                        <span className="md:hidden font-bold truncate max-w-[150px]">{t('app.title')}</span>
                    </div>
                    <div className="flex items-center gap-2 md:gap-4">
                        <LanguageSwitcher />
                        <button className="hidden sm:inline-block text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
                            {t('header.docs')}
                        </button>
                        <button className="hidden sm:inline-block text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
                            {t('header.support')}
                        </button>
                    </div>
                </header>

                {/* Content */}
                <main className="flex-1 overflow-y-auto bg-background p-4 md:p-8">
                    {/* No max-w constraint, fully filling the available space */}
                    <div className="h-full w-full animate-in fade-in duration-300">
                        {children}
                    </div>
                </main>
            </div>
        </div>
    );
};

