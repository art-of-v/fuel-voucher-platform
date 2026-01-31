
import { useStore } from "@/lib/store";
import { useLocation } from "wouter";
import { ChevronLeft, CreditCard, ShieldCheck, Zap, Skull, AlertTriangle, Tag } from "lucide-react";
import { useState } from "react";
import { createPurchase, completePurchase, simulatePayment, createStripeCheckout } from "@/lib/api";
import { useAuth } from "@/hooks/useAuth";
import { isUnauthorizedError } from "@/lib/authUtils";
import { toast } from "sonner";
import { useI18n } from "@/lib/i18n";

export default function CheckoutScreen() {
  const [, setLocation] = useLocation();
  const { user, isLoading: authLoading, isAuthenticated } = useAuth();
  const {
    cart,
    promocode,
    discount,
    getCartTotal,
    getDiscountedTotal,
    clearCart
  } = useStore();
  const { t } = useI18n();
  const [isProcessing, setIsProcessing] = useState(false);

  const total = getCartTotal();
  const discountedTotal = getDiscountedTotal();
  const discountAmount = total - discountedTotal;
  const totalCards = cart.reduce((sum, item) => sum + item.quantity, 0);

  if (authLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="text-primary font-mono animate-pulse flex items-center gap-3">
          <Zap className="w-6 h-6 animate-spin" />
          LOADING...
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="p-6 text-white flex flex-col items-center justify-center min-h-screen relative">
        <div className="absolute top-1/3 left-1/2 -translate-x-1/2 w-64 h-64 bg-primary/20 rounded-full blur-[100px]" />
        <div className="relative z-10 text-center">
          <Skull className="w-16 h-16 text-red-500 mx-auto mb-4" />
          <h2 className="text-2xl font-black text-white font-heading uppercase mb-2">ACCESS DENIED</h2>
          <p className="text-gray-400 font-mono mb-6 text-sm">Sign in to complete your purchase</p>
          <button
            onClick={() => setLocation("/profile")}
            className="inline-flex items-center gap-3 bg-primary text-black px-8 py-4 font-black text-lg font-heading uppercase shadow-[0_0_40px_rgba(0,255,128,0.5)] cursor-pointer hover:bg-primary/90 transition-colors"
          >
            SIGN IN
          </button>
        </div>
      </div>
    );
  }

  if (cart.length === 0) {
    return (
      <div className="p-6 text-white flex flex-col items-center justify-center min-h-screen">
        <p className="text-gray-500 font-mono mb-4">{t('checkout.emptyCart')}</p>
        <button
          onClick={() => setLocation("/")}
          className="bg-primary text-black px-6 py-3 font-bold"
        >
          {t('checkout.browseStations')}
        </button>
      </div>
    );
  }

  const handlePayment = async () => {
    setIsProcessing(true);
    try {
      // Navigate to embedded payment page
      setLocation("/payment");
    } catch (error: any) {
      console.error("Payment navigation error:", error);
      setIsProcessing(false);
      toast.error(error.message || "Failed to proceed to payment");
    }
  };

  return (
    <div className="min-h-screen bg-background flex flex-col relative">
      {/* Aggressive background */}
      <div className="absolute top-0 left-1/2 -translate-x-1/2 w-full h-64 bg-gradient-to-b from-primary/10 to-transparent" />
      <div className="absolute bottom-0 right-0 w-64 h-64 bg-red-500/10 rounded-full blur-[100px]" />

      <div className="bg-black/90 p-4 flex items-center gap-4 border-b-2 border-primary/30 relative z-10">
        <button
          onClick={() => setLocation("/basket")}
          data-testid="button-back"
          className="p-2 -ml-2 border-2 border-white/20 hover:border-primary transition-colors bg-black/50"
        >
          <ChevronLeft className="w-6 h-6 text-gray-400" />
        </button>
        <h1 className="font-black text-xl text-white font-heading tracking-wider uppercase flex items-center gap-2">
          <Skull className="w-5 h-5 text-red-500" />
          {t('checkout.title')}
        </h1>
      </div>

      <div className="p-6 flex-1 space-y-4 relative z-10 pb-48">
        {/* Order items */}
        <div className="space-y-2">
          <div className="flex items-center gap-2 text-xs text-red-400 font-mono uppercase tracking-wider mb-3">
            <AlertTriangle className="w-4 h-4" />
            {t('checkout.orderSummaryLabel')}
          </div>

          {cart.map((item) => (
            <div
              key={item.id}
              className="bg-black/60 border-2 border-white/10 p-4 flex items-center justify-between"
            >
              <div>
                <div className="font-black text-white font-heading uppercase">
                  {item.station.name}
                </div>
                <div className="text-sm text-gray-400 font-mono">
                  {item.fuel.name} • {item.package.liters}L x {item.quantity}
                </div>
              </div>
              <div className="text-right">
                <div className="font-black text-white text-xl font-heading">
                  {item.package.price * item.quantity} ₴
                </div>
              </div>
            </div>
          ))}
        </div>

        {/* Price breakdown */}
        <div className="bg-black border-2 border-primary/30 p-6 space-y-4">
          <div className="flex justify-between text-gray-400 font-mono">
            <span>{t('checkout.cards')} ({totalCards})</span>
            <span>{total} ₴</span>
          </div>

          {discount > 0 && (
            <div className="flex justify-between text-primary font-mono">
              <span className="flex items-center gap-2">
                <Tag className="w-4 h-4" />
                {promocode} (-{discount}%)
              </span>
              <span>-{discountAmount} ₴</span>
            </div>
          )}

          <div className="border-t-2 border-white/10 pt-4 flex justify-between items-end">
            <span className="font-black text-xl text-white font-heading uppercase">{t('checkout.total')}</span>
            <div className="text-right">
              {discount > 0 && (
                <div className="text-sm text-gray-500 line-through font-mono">{total} ₴</div>
              )}
              <span className="text-4xl font-black text-white tracking-tighter font-heading text-glow-intense">{discountedTotal} ₴</span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2 text-[10px] text-primary justify-center uppercase tracking-[0.2em] font-mono">
          <ShieldCheck className="w-4 h-4" />
          <span>{t('checkout.encryptedTransaction')}</span>
        </div>
      </div>

      <div className="fixed bottom-0 left-0 right-0 max-w-md mx-auto p-6 bg-black border-t-2 border-primary/30 z-50">
        <button
          onClick={handlePayment}
          disabled={isProcessing}
          data-testid="button-pay"
          className="w-full bg-primary hover:bg-primary/90 text-black py-5 font-black text-xl flex items-center justify-center gap-3 disabled:opacity-50 active:scale-[0.98] transition-all font-heading tracking-wider uppercase shadow-[0_0_40px_rgba(0,255,128,0.5)] hover:shadow-[0_0_60px_rgba(0,255,128,0.7)]"
        >
          {isProcessing ? (
            <span className="animate-pulse flex items-center gap-3">
              <Zap className="w-6 h-6 animate-spin" />
              PROCESSING...
            </span>
          ) : (
            <>
              <CreditCard className="w-6 h-6" />
              {t('checkout.pay')} {discountedTotal} ₴
            </>
          )}
        </button>
      </div>
    </div>
  );
}
