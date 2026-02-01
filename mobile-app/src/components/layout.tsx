
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
    <div className="h-screen bg-background flex flex-col max-w-md mx-auto shadow-2xl overflow-hidden relative border-x border-white/5">
      {/* Language selector in top right */}
      <div className="absolute top-4 right-4 z-50">
        <LanguageSelector />
      </div>

      {/* Background lion logo watermark */}
      <div className="fixed inset-0 pointer-events-none z-0 overflow-hidden max-w-md mx-auto">
        <img
          src={lionLogo}
          alt=""
          className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[min(500px,80vw)] h-[min(500px,80vw)] object-contain opacity-[0.08] saturate-0 contrast-200"
        />
        <div className="absolute bottom-0 right-0 w-48 h-48 sm:w-64 sm:h-64 opacity-[0.12]">
          <img src={lionLogo} alt="" className="w-full h-full object-contain saturate-0" />
        </div>
      </div>

      <main className="flex-1 overflow-y-auto pb-nav no-scrollbar relative z-10">
        {children}
      </main>

      {/* Floating Glass Navigation - Fixed to bottom with safe area */}
      <div className="fixed bottom-0 left-0 right-0 z-50 max-w-md mx-auto pb-safe">
        <div className="px-4 sm:px-6 pb-4 sm:pb-6">
          <nav className="glass rounded-2xl px-4 sm:px-6 py-3 sm:py-4 flex justify-between items-center shadow-2xl border border-white/10 backdrop-blur-xl bg-black/40">
            <Link href="/" className={cn("flex flex-col items-center gap-1 transition-all duration-300 min-w-[48px]", isActive("/") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
              <Home className={cn("w-5 h-5 sm:w-6 sm:h-6", isActive("/") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
            </Link>

            <Link href="/basket" className={cn("flex flex-col items-center gap-1 transition-all duration-300 relative min-w-[48px]", isActive("/basket") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
              <ShoppingCart className={cn("w-5 h-5 sm:w-6 sm:h-6", isActive("/basket") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
              {cartCount > 0 && (
                <span className="absolute -top-2 -right-2 w-5 h-5 bg-red-500 text-white text-[10px] font-bold rounded-full flex items-center justify-center shadow-[0_0_8px_rgba(255,50,50,0.5)]">
                  {cartCount > 9 ? '9+' : cartCount}
                </span>
              )}
            </Link>

            <Link href="/my-codes" className={cn("flex flex-col items-center gap-1 transition-all duration-300 min-w-[48px]", isActive("/my-codes") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
              <QrCode className={cn("w-5 h-5 sm:w-6 sm:h-6", isActive("/my-codes") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
            </Link>

            <Link href="/profile" className={cn("flex flex-col items-center gap-1 transition-all duration-300 min-w-[48px]", isActive("/profile") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
              <User className={cn("w-5 h-5 sm:w-6 sm:h-6", isActive("/profile") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
            </Link>
          </nav>
        </div>
      </div>
    </div>
  );
}
