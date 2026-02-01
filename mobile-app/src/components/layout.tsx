
import { Link, useLocation } from "wouter";
import { Home, QrCode, ShoppingCart, User } from "lucide-react";
import { cn } from "@/lib/utils";
import { useStore } from "@/lib/store";
import { LanguageSelector } from "@/components/language-selector";
import lionLogo from "@assets/generated_images/profile_cyberpunk_lion_logo.png";

export default function Layout({ children }: { children: React.ReactNode }) {
  const [location] = useLocation();
  const getCartItemCount = useStore((state) => state.getCartItemCount);
  const cartCount = getCartItemCount();

  const isActive = (path: string) => location === path;

  return (
    <div className="safe-viewport shadow-2xl border-x border-white/5">
      {/* Background & Effects Wrapper */}
      <div className="absolute inset-0 pointer-events-none overflow-hidden" style={{ zIndex: 'var(--z-background)' }}>
        <img
          src={lionLogo}
          alt=""
          className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[80vw] h-[80vw] max-w-[500px] object-contain opacity-[0.08] saturate-0 contrast-200"
        />
        <div className="absolute bottom-0 right-0 w-48 h-48 sm:w-64 sm:h-64 opacity-[0.12]">
          <img src={lionLogo} alt="" className="w-full h-full object-contain saturate-0" />
        </div>
        {/* Aggressive background glow */}
        <div className="absolute -left-20 -top-20 w-64 h-64 bg-primary/20 rounded-full blur-[100px]" />
      </div>

      {/* Language selector fixed to top of safe viewport */}
      <div className="absolute top-4 right-4" style={{ zIndex: 'var(--z-dropdown)' }}>
        <LanguageSelector />
      </div>

      <main className="main-content relative" style={{ zIndex: 'var(--z-main)' }}>
        {children}
      </main>

      {/* Navigation fixed within safe viewport */}
      <div className="absolute bottom-0 left-0 right-0 p-4" style={{ zIndex: 'var(--z-nav)' }}>
        <nav className="glass rounded-2xl p-4 flex justify-between items-center shadow-2xl relative overflow-hidden">
          <Link href="/" className={cn("flex flex-col items-center gap-1 transition-all duration-300 min-w-[48px]", isActive("/") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
            <Home className={cn("w-6 h-6", isActive("/") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
          </Link>

          <Link href="/basket" className={cn("flex flex-col items-center gap-1 transition-all duration-300 relative min-w-[48px]", isActive("/basket") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
            <ShoppingCart className={cn("w-6 h-6", isActive("/basket") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
            {cartCount > 0 && (
              <span className="absolute -top-2 -right-2 w-5 h-5 bg-red-500 text-white text-[10px] font-bold rounded-full flex items-center justify-center shadow-[0_0_8px_rgba(255,50,50,0.5)]">
                {cartCount > 9 ? '9+' : cartCount}
              </span>
            )}
          </Link>

          <Link href="/my-codes" className={cn("flex flex-col items-center gap-1 transition-all duration-300 min-w-[48px]", isActive("/my-codes") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
            <QrCode className={cn("w-6 h-6", isActive("/my-codes") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
          </Link>

          <Link href="/profile" className={cn("flex flex-col items-center gap-1 transition-all duration-300 min-w-[48px]", isActive("/profile") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
            <User className={cn("w-6 h-6", isActive("/profile") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
          </Link>
        </nav>
      </div>
    </div>
  );
}
