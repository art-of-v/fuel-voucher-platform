
import { useState, useEffect } from "react";
import { useStore } from "@/lib/store";
import { useLocation } from "wouter";
import { ChevronLeft, Zap, Flame, Skull, Minus, Plus, ShoppingCart, Check, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { useI18n } from "@/lib/i18n";
import { getPackages, getInventory, FuelPackage } from "@/lib/api";
import { normalizeFuelName } from "@/lib/utils";

export default function PackagesScreen() {
  const [, setLocation] = useLocation();
  const { t } = useI18n();
  const { selectedStation, selectedFuel, addToCart, getCartItemCount } = useStore();
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const [addedItems, setAddedItems] = useState<Set<string>>(new Set());

  const [packages, setPackages] = useState<FuelPackage[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (selectedStation && selectedFuel) {
      loadData();
    }
  }, [selectedStation, selectedFuel]);

  const loadData = async () => {
    try {
      setLoading(true);
      const [allPkgs, inventory] = await Promise.all([getPackages(), getInventory()]);

      const filtered = allPkgs.filter(pkg => {
        // Match Station
        if (pkg.stationId !== selectedStation!.id) return false;

        // Match Fuel - use normalization
        if (normalizeFuelName(pkg.fuelName) !== normalizeFuelName(selectedFuel!.name)) return false;

        // Inventory Check
        return inventory.some(item =>
          item.provider.toLowerCase() === pkg.stationId.toLowerCase() &&
          normalizeFuelName(item.fuelType) === normalizeFuelName(pkg.fuelName) &&
          item.liters === pkg.liters &&
          item.availableCount > 0
        );
      });

      // Sort by liters ascending
      filtered.sort((a, b) => a.liters - b.liters);

      setPackages(filtered);
    } catch (e) {
      console.error("Failed to load packages", e);
      toast.error("Failed to load packages");
    } finally {
      setLoading(false);
    }
  };

  if (!selectedStation || !selectedFuel) {
    return <div className="p-6 text-white">Missing selection data. Please start from home.</div>;
  }

  const cartCount = getCartItemCount();

  const getQuantity = (pkgId: string) => quantities[pkgId] || 1;

  const updateQuantity = (pkgId: string, delta: number) => {
    setQuantities((prev) => ({
      ...prev,
      [pkgId]: Math.max(1, Math.min(99, (prev[pkgId] || 1) + delta)),
    }));
  };

  const handleAddToCart = (pkg: FuelPackage) => {
    const qty = getQuantity(pkg.id);
    addToCart({
      package: pkg,
      station: selectedStation,
      fuel: selectedFuel,
      quantity: qty,
    });

    setAddedItems((prev) => new Set(prev).add(pkg.id));
    toast.success(`Added ${qty}x ${pkg.liters}L to cart`);

    setTimeout(() => {
      setAddedItems((prev) => {
        const next = new Set(prev);
        next.delete(pkg.id);
        return next;
      });
    }, 2000);
  };

  if (loading) {
    return <div className="min-h-screen bg-background flex items-center justify-center text-white"><Loader2 className="animate-spin w-8 h-8" /></div>;
  }

  return (
    <div className="min-h-screen bg-background flex flex-col relative">
      {/* Background effects */}
      <div className="absolute top-1/4 right-0 w-64 h-64 bg-primary/20 rounded-full blur-[120px]" />

      <div className="bg-black/90 backdrop-blur-md p-6 pb-4 border-b-2 border-primary/30 z-10 sticky top-0">
        <div className="flex items-center gap-4">
          <button
            onClick={() => setLocation(`/station/${selectedStation.id}`)}
            data-testid="button-back"
            className="p-2 -ml-2 border-2 border-white/20 hover:border-primary transition-colors bg-black/50"
          >
            <ChevronLeft className="w-6 h-6 text-white" />
          </button>
          <div className="flex-1">
            <h2 className="font-black text-2xl text-white leading-none tracking-tight font-heading uppercase flex items-center gap-2">
              <Zap className="w-5 h-5 text-primary" />
              {selectedFuel.name}
            </h2>
            <p className="text-xs text-red-400 font-mono tracking-[0.2em] uppercase mt-1 flex items-center gap-2">
              <Skull className="w-3 h-3" />
              {t('packages.selectCards')}
            </p>
          </div>

          {/* Cart button */}
          <button
            onClick={() => setLocation("/basket")}
            data-testid="button-cart"
            className="relative p-3 bg-primary/20 border-2 border-primary/50 hover:bg-primary hover:text-black transition-all"
          >
            <ShoppingCart className="w-6 h-6 text-primary" />
            {cartCount > 0 && (
              <span className="absolute -top-2 -right-2 w-6 h-6 bg-red-500 text-white text-xs font-bold rounded-full flex items-center justify-center shadow-[0_0_10px_rgba(255,50,50,0.5)]">
                {cartCount}
              </span>
            )}
          </button>
        </div>
      </div>

      <div className="p-4 space-y-4 flex-1 relative z-10 pb-40">
        {packages.length === 0 ? (
          <div className="text-center text-gray-500 font-mono py-10">NO PACKAGES AVAILABLE</div>
        ) : packages.map((pkg) => {
          const savings = pkg.originalPrice - pkg.price;
          const qty = getQuantity(pkg.id);
          const totalPrice = pkg.price * qty;
          const totalOriginal = pkg.originalPrice * qty;
          const totalSavings = totalOriginal - totalPrice;
          const isAdded = addedItems.has(pkg.id);

          return (
            <div
              key={pkg.id}
              data-testid={`package-${pkg.liters}L`}
              className="bg-black/80 border-2 border-white/10 overflow-hidden"
            >
              {/* Header with savings */}
              <div className="flex items-center justify-between p-4 border-b-2 border-white/10">
                <div className="flex items-center gap-4">
                  <div className="w-20 h-20 bg-primary/10 border-2 border-primary/30 flex flex-col items-center justify-center">
                    <span className="text-4xl font-black text-white font-heading">{pkg.liters}</span>
                    <span className="text-xs text-primary font-mono">{t('packages.liters')}</span>
                  </div>
                  <div>
                    <div className="text-2xl font-black text-white font-heading">{pkg.price} ₴</div>
                    <div className="text-sm text-gray-500 line-through font-mono">{pkg.originalPrice} ₴</div>
                  </div>
                </div>
                <div className="bg-primary text-black font-black text-sm px-4 py-2 flex items-center gap-2 font-heading shadow-[0_0_20px_rgba(0,255,128,0.5)]">
                  <Flame className="w-4 h-4" />
                  -{savings} ₴
                </div>
              </div>

              {/* Quantity selector and summary */}
              <div className="p-4 space-y-4">
                <div className="flex items-center justify-between">
                  <span className="text-gray-400 font-mono text-sm uppercase tracking-wider">{t('packages.quantity')}</span>
                  <div className="flex items-center gap-3">
                    <button
                      onClick={() => updateQuantity(pkg.id, -1)}
                      data-testid={`btn-minus-${pkg.id}`}
                      className="w-10 h-10 bg-white/10 border-2 border-white/20 flex items-center justify-center hover:bg-red-500/20 hover:border-red-500 transition-all"
                    >
                      <Minus className="w-5 h-5 text-white" />
                    </button>
                    <span className="text-3xl font-black text-primary font-mono w-16 text-center">{qty}</span>
                    <button
                      onClick={() => updateQuantity(pkg.id, 1)}
                      data-testid={`btn-plus-${pkg.id}`}
                      className="w-10 h-10 bg-white/10 border-2 border-white/20 flex items-center justify-center hover:bg-primary/20 hover:border-primary transition-all"
                    >
                      <Plus className="w-5 h-5 text-white" />
                    </button>
                  </div>
                </div>

                {/* Price summary */}
                <div className="bg-white/5 border-2 border-white/10 p-4">
                  <div className="flex justify-between text-sm text-gray-400 font-mono mb-2">
                    <span>{qty}x {pkg.liters}L {t('packages.cards')}</span>
                    <span className="text-gray-500 line-through">{totalOriginal} ₴</span>
                  </div>
                  <div className="flex justify-between items-end">
                    <div>
                      <div className="text-[10px] text-gray-500 uppercase tracking-wider font-mono">{t('packages.totalSavings')}</div>
                      <div className="text-primary font-black text-lg">{totalSavings} ₴</div>
                    </div>
                    <div className="text-right">
                      <div className="text-[10px] text-gray-500 uppercase tracking-wider font-mono">{t('packages.pay')}</div>
                      <div className="text-white font-black text-3xl font-heading">{totalPrice} ₴</div>
                    </div>
                  </div>
                </div>

                {/* Add to cart button */}
                <button
                  onClick={() => handleAddToCart(pkg)}
                  data-testid={`btn-add-${pkg.id}`}
                  disabled={isAdded}
                  className={`w-full py-4 font-black text-lg flex items-center justify-center gap-3 transition-all font-heading tracking-wider uppercase ${isAdded
                    ? 'bg-green-500 text-white'
                    : 'bg-primary text-black hover:shadow-[0_0_40px_rgba(0,255,128,0.5)]'
                    }`}
                >
                  {isAdded ? (
                    <>
                      <Check className="w-6 h-6" />
                      {t('packages.addedToCart')}
                    </>
                  ) : (
                    <>
                      <ShoppingCart className="w-6 h-6" />
                      {t('packages.addToCart')}
                    </>
                  )}
                </button>
              </div>
            </div>
          );
        })}
      </div>

      {/* Floating cart summary */}
      {cartCount > 0 && (
        <div className="fixed bottom-28 left-0 right-0 max-w-md mx-auto px-4 z-40">
          <button
            onClick={() => setLocation("/basket")}
            className="w-full bg-primary text-black py-4 font-black text-lg flex items-center justify-center gap-3 shadow-[0_0_40px_rgba(0,255,128,0.5)] font-heading tracking-wider uppercase"
          >
            <ShoppingCart className="w-6 h-6" />
            {t('packages.viewCart')} ({cartCount})
          </button>
        </div>
      )}
    </div>
  );
}
