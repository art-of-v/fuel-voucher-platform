
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

      <div className="p-4 space-y-3 relative z-10 pb-[420px]">
        {cart.map((item) => (
          <div
            key={item.id}
            data-testid={`cart-item-${item.id}`}
            className="bg-black/80 border-2 border-white/10 p-4"
          >
            <div className="flex items-start justify-between mb-3">
              <div>
                <div className="font-black text-white text-lg font-heading uppercase">
                  {item.station.name} - {item.fuel.name}
                </div>
                <div className="text-primary font-mono text-sm">{item.package.liters}L Card</div>
              </div>
              <button
                onClick={() => removeFromCart(item.id)}
                className="p-2 text-red-500 hover:bg-red-500/20 transition-colors"
              >
                <Trash2 className="w-5 h-5" />
              </button>
            </div>

            <div className="flex items-center justify-between">
              <div className="flex items-center gap-4">
                <button
                  onClick={() => updateQuantity(item.id, item.quantity - 1)}
                  data-testid={`btn-minus-${item.id}`}
                  className="w-10 h-10 bg-white/10 border-2 border-white/20 flex items-center justify-center hover:bg-red-500/20 hover:border-red-500 transition-all active:scale-95"
                >
                  <Minus className="w-5 h-5 text-white" />
                </button>
                <span className="text-3xl font-black text-primary font-mono w-14 text-center">{item.quantity}</span>
                <button
                  onClick={() => updateQuantity(item.id, item.quantity + 1)}
                  data-testid={`btn-plus-${item.id}`}
                  className="w-10 h-10 bg-white/10 border-2 border-white/20 flex items-center justify-center hover:bg-primary/20 hover:border-primary transition-all active:scale-95"
                >
                  <Plus className="w-5 h-5 text-white" />
                </button>
              </div>
              <div className="text-right">
                <div className="text-[10px] text-gray-500 font-mono uppercase">
                  {item.quantity} x {item.package.price} ₴
                </div>
                <div className="text-white font-black text-xl font-heading">
                  {item.package.price * item.quantity} ₴
                </div>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Fixed Bottom Checkout Section - Above Navigation */}
      <div className="fixed bottom-0 left-0 right-0 max-w-md mx-auto bg-black border-t-2 border-primary/30 p-4 space-y-4 z-[60]">
        {/* Promocode input */}
        <div className="space-y-2">
          <div className="flex items-center gap-2 text-xs text-gray-400 font-mono uppercase tracking-wider">
            <Tag className="w-4 h-4 text-primary" />
            {t('basket.promocode')}
          </div>

          {promocode ? (
            <div className="flex items-center justify-between bg-primary/10 border-2 border-primary/30 p-3">
              <div className="flex items-center gap-2">
                <Check className="w-5 h-5 text-primary" />
                <span className="font-black text-primary font-mono">{promocode}</span>
                <span className="text-gray-400 text-sm">(-{discount}%)</span>
              </div>
              <button
                onClick={clearPromocode}
                className="text-red-500 hover:text-red-400 transition-colors"
              >
                <X className="w-5 h-5" />
              </button>
            </div>
          ) : (
            <div className="flex gap-2">
              <input
                type="text"
                value={promoInput}
                onChange={(e) => {
                  setPromoInput(e.target.value);
                  setPromoError(false);
                }}
                placeholder={t('basket.enterCode')}
                data-testid="input-promocode"
                className={`flex-1 bg-black/50 border-2 ${promoError ? 'border-red-500' : 'border-white/20'} px-4 py-3 text-white font-mono uppercase tracking-wider focus:border-primary focus:outline-none`}
              />
              <button
                onClick={handleApplyPromo}
                disabled={!promoInput}
                className="bg-primary/20 border-2 border-primary/50 px-6 font-black text-primary hover:bg-primary hover:text-black transition-all disabled:opacity-50"
              >
                {t('basket.apply')}
              </button>
            </div>
          )}
        </div>

        {/* Price summary */}
        <div className="space-y-2 border-t-2 border-white/10 pt-4">
          <div className="flex justify-between text-gray-400 font-mono text-sm">
            <span>{t('basket.subtotal')}</span>
            <span>{total} ₴</span>
          </div>

          {discount > 0 && (
            <div className="flex justify-between text-primary font-mono text-sm">
              <span>{t('basket.discount')} ({discount}%)</span>
              <span>-{discountAmount} ₴</span>
            </div>
          )}

          <div className="flex justify-between items-end pt-2">
            <span className="font-black text-white text-lg font-heading uppercase">{t('basket.totalToPay')}</span>
            <span className="text-4xl font-black text-white font-heading text-glow">{discountedTotal} ₴</span>
          </div>
        </div>

        {/* Checkout button */}
        <button
          onClick={() => setLocation("/checkout")}
          data-testid="button-checkout"
          className="w-full bg-primary text-black py-5 font-black text-xl flex items-center justify-center gap-3 shadow-[0_0_40px_rgba(0,255,128,0.5)] font-heading tracking-wider uppercase active:scale-[0.98] transition-all"
        >
          <Zap className="w-6 h-6" />
          {t('basket.checkout')}
        </button>
      </div>
    </div>
  );
}
