
import { Link, useLocation } from "wouter";
import { Home, QrCode, ShoppingCart, User } from "lucide-react";
import { cn } from "@/lib/utils";
import { useStore } from "@/lib/store";
import lionLogo from "@assets/generated_images/profile_cyberpunk_lion_logo.png";

export default function Layout({ children }: { children: React.ReactNode }) {
  const [location] = useLocation();
  const getCartItemCount = useStore((state) => state.getCartItemCount);
  const cartCount = getCartItemCount();

  const isActive = (path: string) => location === path;

  return (
    <div className="h-[100dvh] bg-background flex flex-col max-w-md mx-auto shadow-2xl overflow-hidden relative border-x border-white/5">
      {/* Cyber Grid Background */}
      <div className="fixed inset-0 pointer-events-none z-0">
        <div className="absolute inset-0 bg-[linear-gradient(to_right,#80808008_1px,transparent_1px),linear-gradient(to_bottom,#80808008_1px,transparent_1px)] bg-[size:24px_24px]" />
        <div className="absolute inset-0 bg-[radial-gradient(circle_800px_at_50%_200px,rgba(0,255,128,0.03),transparent)]" />
      </div>

      {/* Background lion logo watermark */}
      <div className="fixed inset-0 pointer-events-none z-0 overflow-hidden max-w-md mx-auto">
        <img
          src={lionLogo}
          alt=""
          className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[500px] h-[500px] object-contain opacity-[0.05] saturate-0 contrast-200 mix-blend-screen"
        />
        <div className="absolute bottom-0 right-0 w-64 h-64 opacity-[0.12]">
          <img src={lionLogo} alt="" className="w-full h-full object-contain saturate-0" />
        </div>
      </div>

      <main className="flex-1 overflow-hidden relative z-10 h-full">
        {children}
      </main>

      {/* Floating Glass Navigation */}
      <div className="absolute bottom-6 left-6 right-6 z-50">
        <nav className="flex justify-between items-center px-6 py-4">
          <Link href="/" className={cn("flex flex-col items-center gap-1 transition-all duration-300", isActive("/") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
            <Home className={cn("w-6 h-6", isActive("/") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
          </Link>

          <Link href="/basket" className={cn("flex flex-col items-center gap-1 transition-all duration-300 relative", isActive("/basket") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
            <ShoppingCart className={cn("w-6 h-6", isActive("/basket") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
            {cartCount > 0 && (
              <span className="absolute -top-2 -right-2 w-5 h-5 bg-red-500 text-white text-[10px] font-bold rounded-full flex items-center justify-center shadow-[0_0_8px_rgba(255,50,50,0.5)]">
                {cartCount > 9 ? '9+' : cartCount}
              </span>
            )}
          </Link>

          <Link href="/my-codes" className={cn("flex flex-col items-center gap-1 transition-all duration-300", isActive("/my-codes") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
            <QrCode className={cn("w-6 h-6", isActive("/my-codes") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
          </Link>

          <Link href="/profile" className={cn("flex flex-col items-center gap-1 transition-all duration-300", isActive("/profile") ? "text-primary scale-110" : "text-gray-500 hover:text-gray-300")}>
            <User className={cn("w-6 h-6", isActive("/profile") && "drop-shadow-[0_0_8px_rgba(0,255,128,0.6)]")} />
          </Link>
        </nav>
      </div>
    </div>
  );
}
