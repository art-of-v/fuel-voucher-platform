import { ReactNode } from "react";
import { Building, Fuel, Package, ShoppingCart, Users, QrCode, Menu, Ticket } from "lucide-react";
import { cn } from "@/lib/utils";
import { useI18n } from "@/lib/i18n";
import { LanguageSwitcher } from "@/components/LanguageSwitcher";

interface SidebarProps {
    activeTab: string;
    onTabChange: (tab: string) => void;
    className?: string;
}

const Sidebar = ({ activeTab, onTabChange, className }: SidebarProps) => {
    const { t } = useI18n();

    const navItems = [
        { id: "stations", label: t("nav.stations"), icon: Building },
        { id: "fueltypes", label: t("nav.fueltypes"), icon: Fuel },
        { id: "packages", label: t("nav.packages"), icon: Package },
        { id: "purchases", label: t("nav.purchases"), icon: ShoppingCart },
        { id: "users", label: t("nav.users"), icon: Users },
        { id: "qrcodes", label: t("nav.qrcodes"), icon: QrCode },
        { id: "vouchers", label: t("nav.vouchers"), icon: Ticket },
    ];

    return (
        <aside className={cn("w-64 bg-card border-r border-border flex flex-col h-full shrink-0", className)}>
            <div className="h-16 px-6 flex items-center border-b border-border">
                <h1 className="text-xl font-bold tracking-tight text-foreground flex items-center gap-2">
                    <div className="w-2 h-6 bg-primary rounded-sm"></div>
                    {t('app.title')} <span className="text-muted-foreground font-normal">{t('app.admin')}</span>
                </h1>
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
                            onClick={() => onTabChange(item.id)}
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
                        <span className="text-xs font-bold text-muted-foreground">AD</span>
                    </div>
                    <div className="overflow-hidden">
                        <p className="text-sm font-medium text-foreground truncate">{t('user.name')}</p>
                        <p className="text-xs text-muted-foreground truncate">{t('user.role')}</p>
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
}

export const Layout = ({ children, activeTab, onTabChange }: LayoutProps) => {
    const { t } = useI18n();

    return (
        <div className="flex h-screen w-screen overflow-hidden bg-background text-foreground">
            {/* Desktop Sidebar */}
            <Sidebar activeTab={activeTab} onTabChange={onTabChange} className="hidden md:flex" />

            {/* Main Content Area */}
            <div className="flex flex-1 flex-col overflow-hidden min-w-0">
                {/* Header */}
                <header className="h-16 border-b border-border bg-card/50 flex items-center justify-between px-6 shrink-0 backdrop-blur-sm">
                    <div className="flex items-center gap-4">
                        <span className="text-sm font-medium text-muted-foreground hidden md:inline-block">/ {t('header.dashboard')} / {activeTab}</span>
                        <span className="md:hidden font-bold">{t('app.title')} {t('app.admin')}</span>
                    </div>
                    <div className="flex items-center gap-4">
                        <LanguageSwitcher />
                        <button className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
                            {t('header.docs')}
                        </button>
                        <button className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors">
                            {t('header.support')}
                        </button>
                    </div>
                </header>

                {/* Content */}
                <main className="flex-1 overflow-y-auto bg-background p-8">
                    {/* No max-w constraint, fully filling the available space */}
                    <div className="h-full w-full animate-in fade-in duration-300">
                        {children}
                    </div>
                </main>
            </div>
        </div>
    );
};

