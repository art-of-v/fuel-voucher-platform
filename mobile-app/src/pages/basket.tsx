
import { useState } from "react";
import { useStore } from "@/lib/store";
import { useLocation } from "wouter";
import { ChevronLeft, Minus, Plus, Trash2, Tag, Zap, ShoppingCart, X, Check, AlertTriangle } from "lucide-react";
import { toast } from "sonner";
import { useI18n } from "@/lib/i18n";

export default function BasketScreen() {
  const [, setLocation] = useLocation();
  const { t } = useI18n();
  const {
    cart,
    updateQuantity,
    removeFromCart,
    clearCart,
    promocode,
    discount,
    applyPromocode,
    clearPromocode,
    getCartTotal,
    getDiscountedTotal
  } = useStore();

  const [promoInput, setPromoInput] = useState("");
  const [promoError, setPromoError] = useState(false);

  const total = getCartTotal();
  const discountedTotal = getDiscountedTotal();
  const discountAmount = total - discountedTotal;

  const handleApplyPromo = () => {
    setPromoError(false);
    if (applyPromocode(promoInput)) {
      toast.success(`Promocode ${promoInput.toUpperCase()} applied!`);
      setPromoInput("");
    } else {
      setPromoError(true);
      toast.error("Invalid promocode");
    }
  };

  if (cart.length === 0) {
    return (
      <div className="min-h-screen bg-background flex flex-col">
        <div className="bg-black/90 p-4 flex items-center gap-4 border-b-2 border-primary/30">
          <button
            onClick={() => setLocation("/")}
            className="p-2 -ml-2 border-2 border-white/20 hover:border-primary transition-colors bg-black/50"
          >
            <ChevronLeft className="w-6 h-6 text-white" />
          </button>
          <h1 className="font-black text-xl text-white font-heading tracking-wider uppercase">{t('basket.title')}</h1>
        </div>

        <div className="flex-1 flex flex-col items-center justify-center p-8">
          <ShoppingCart className="w-20 h-20 text-gray-700 mb-4" />
          <h2 className="text-2xl font-black text-white font-heading uppercase mb-2">{t('basket.empty')}</h2>
          <p className="text-gray-500 font-mono text-sm mb-8">{t('basket.browseStations')}</p>
          <button
            onClick={() => setLocation("/")}
            className="bg-primary text-black px-8 py-4 font-black text-lg font-heading uppercase"
          >
            {t('basket.browseStations')}
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background relative">
      <div className="absolute top-0 right-0 w-64 h-64 bg-primary/10 rounded-full blur-[100px]" />

      <div className="bg-black/90 p-4 flex items-center gap-4 border-b-2 border-primary/30 sticky top-0 z-20">
        <button
          onClick={() => setLocation("/")}
          data-testid="button-back"
          className="p-2 -ml-2 border-2 border-white/20 hover:border-primary transition-colors bg-black/50"
        >
          <ChevronLeft className="w-6 h-6 text-white" />
        </button>
        <div className="flex-1">
          <h1 className="font-black text-xl text-white font-heading tracking-wider uppercase flex items-center gap-2">
            <ShoppingCart className="w-5 h-5 text-primary" />
            {t('basket.title')}
          </h1>
          <p className="text-xs text-gray-400 font-mono">{cart.length} {t('basket.cards')}</p>
        </div>
        <button
          onClick={() => {
            clearCart();
            toast.success(t('basket.empty'));
          }}
          className="text-red-500 text-xs font-mono uppercase tracking-wider hover:text-red-400 transition-colors"
        >
          {t('basket.remove')}
        </button>
      </div>

      <div className="p-4 space-y-3 relative z-10">
        {cart.map((item) => (
          <div
            key={item.id}
            data-testid={`cart-item-${item.id}`}
            className="bg-black/80 border-2 border-white/10 p-4"
          >
            <div className="flex items-start justify-between mb-3">
              <div>
                <div className="font-black text-white text-lg font-heading uppercase leading-tight">
                  {item.station.name} - {item.fuel.name}
                </div>
                <div className="text-primary font-mono text-xs mt-1 uppercase tracking-wider">{item.package.liters}L Card</div>
              </div>
              <button
                onClick={() => removeFromCart(item.id)}
                className="p-2 text-red-500 hover:bg-red-500/20 transition-colors"
                aria-label="Remove item"
              >
                <Trash2 className="w-5 h-5" />
              </button>
            </div>

            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3">
                <button
                  onClick={() => updateQuantity(item.id, item.quantity - 1)}
                  className="w-10 h-10 bg-white/5 border border-white/10 flex items-center justify-center active:scale-95 transition-all text-white"
                >
                  <Minus className="w-4 h-4" />
                </button>
                <span className="text-2xl font-black text-primary font-mono w-8 text-center">{item.quantity}</span>
                <button
                  onClick={() => updateQuantity(item.id, item.quantity + 1)}
                  className="w-10 h-10 bg-white/5 border border-white/10 flex items-center justify-center active:scale-95 transition-all text-white"
                >
                  <Plus className="w-4 h-4" />
                </button>
              </div>
              <div className="text-right">
                <div className="text-white font-black text-xl font-heading tracking-tight">
                  {item.package.price * item.quantity} ₴
                </div>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Fixed Bottom Checkout Section - Proper absolute positioning within viewport */}
      <div
        className="absolute left-0 right-0 bg-black/95 border-t border-primary/30 px-4 py-6 space-y-4 backdrop-blur-md"
        style={{
          bottom: 'var(--nav-total-height)',
          zIndex: 'var(--z-fixed-cta)',
          paddingBottom: 'calc(16px + var(--safe-area-bottom))'
        }}
      >
        <div className="space-y-4">
          <div className="flex justify-between items-end border-b border-white/5 pb-4">
            <span className="font-black text-gray-400 text-xs font-heading uppercase tracking-widest">{t('basket.totalToPay')}</span>
            <span className="text-4xl font-black text-white font-heading text-glow leading-none">{discountedTotal} ₴</span>
          </div>

          <button
            onClick={() => setLocation("/checkout")}
            className="w-full bg-primary text-black py-4 font-black flex items-center justify-center gap-3 shadow-[0_0_40px_rgba(0,255,128,0.3)] font-heading tracking-widest uppercase active:scale-[0.98] transition-all hover:brightness-110"
          >
            <Zap className="w-5 h-5 fill-black" />
            {t('basket.checkout')}
          </button>
        </div>
      </div>
    </div>
  );
}
