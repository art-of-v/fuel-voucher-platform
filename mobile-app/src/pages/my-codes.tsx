
import { useState, useEffect } from "react";
import { X, Copy, QrCode, Zap, Skull, ShieldCheck, AlertTriangle, CheckCircle2, RotateCcw } from "lucide-react";
import { DialogContent } from "@/components/ui/dialog";
import * as DialogPrimitive from "@radix-ui/react-dialog";
import { getMyPurchases } from "@/lib/api";
import { toast } from "sonner"; // Removed unused useAuth

interface PurchaseWithQr {
  id: number;
  packageId: string;
  stationName: string;
  fuelName: string;
  liters: number;
  price: number;
  status: string;
  createdAt: string;
  qrCode?: {
    id: number;
    qrCodeUrl: string;
    stationId: string;
    fuelType: string;
    liters: number;
    status: string;
  };
}

export default function MyCodesScreen() {
  const [purchases, setPurchases] = useState<PurchaseWithQr[]>([]);
  const [loading, setLoading] = useState(true);
  const [usedCodes, setUsedCodes] = useState<Set<number>>(new Set());

  useEffect(() => {
    loadPurchases();
    // Load used state from local storage
    const savedUsed = localStorage.getItem('fuel_used_codes');
    if (savedUsed) {
      setUsedCodes(new Set(JSON.parse(savedUsed)));
    }
  }, []);

  const loadPurchases = async () => {
    try {
      const data = await getMyPurchases();
      // We accept both 'delivered' and legacy states if necessary, but strictly filtering for delivered & qr/voucher matches
      const deliveredPurchases = data.filter(p => p.status === 'delivered');
      setPurchases(deliveredPurchases);
    } catch (error: any) {
      console.error("Failed to load purchases:", error);
      if (error.message?.includes('401')) {
        setPurchases([]);
      } else {
        toast.error("Failed to load your codes");
      }
    } finally {
      setLoading(false);
    }
  };

  const toggleUsed = (id: number) => {
    const newUsed = new Set(usedCodes);
    if (newUsed.has(id)) {
      newUsed.delete(id);
      toast.success("Marked as active");
    } else {
      newUsed.add(id);
      toast.success("Marked as used");
    }
    setUsedCodes(newUsed);
    localStorage.setItem('fuel_used_codes', JSON.stringify(Array.from(newUsed)));
  };

  const copyToClipboard = (text: string) => {
    navigator.clipboard.writeText(text);
    toast.success("Code ID copied!");
  };

  if (loading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-screen">
        <div className="text-primary font-mono animate-pulse flex items-center gap-3">
          <Zap className="w-6 h-6 animate-spin" />
          LOADING ASSETS...
        </div>
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6 pt-10 relative pb-24">
      {/* Background effects */}
      <div className="absolute top-0 right-0 w-64 h-64 bg-primary/10 rounded-full blur-[100px]" />

      <header className="relative z-10">
        <div className="flex items-center gap-3 mb-2">
          <Skull className="w-8 h-8 text-red-500" />
          <h1 className="text-4xl font-black text-white font-heading tracking-tight uppercase">MY ASSETS</h1>
        </div>
        <div className="flex items-center gap-2 text-primary text-xs font-mono tracking-[0.2em] uppercase">
          <ShieldCheck className="w-4 h-4" />
          <span>SECURE VAULT</span>
        </div>
      </header>

      {purchases.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 text-gray-600 border-2 border-dashed border-white/10 bg-black/50 relative z-10">
          <QrCode className="w-16 h-16 mb-4 opacity-20" />
          <p className="font-mono text-lg uppercase tracking-wider">NO ACTIVE CODES</p>
          <p className="text-xs text-gray-500 mt-2 font-mono">Purchase a fuel package to get started</p>
        </div>
      ) : (
        <div className="space-y-3 relative z-10">
          {purchases.map((purchase) => {
            const isUsed = usedCodes.has(purchase.id);
            return (
              <DialogPrimitive.Root key={purchase.id}>
                <DialogPrimitive.Trigger asChild>
                  <button
                    data-testid={`code-${purchase.id}`}
                    className={`w-full bg-black/80 border-2 p-0 flex items-stretch group active:scale-[0.98] transition-all text-left overflow-hidden hover:box-glow relative ${isUsed ? 'border-gray-800 opacity-60 grayscale' : 'border-white/10 hover:border-primary'
                      }`}
                  >
                    {isUsed && (
                      <div className="absolute inset-0 z-20 flex items-center justify-center pointer-events-none">
                        <div className="border-4 border-white/20 text-white/20 font-black text-4xl uppercase -rotate-12 p-4 tracking-widest">
                          USED
                        </div>
                      </div>
                    )}

                    {/* QR Preview */}
                    <div className="w-24 bg-primary/5 border-r-2 border-primary/20 flex items-center justify-center p-3 relative overflow-hidden">
                      {purchase.qrCode && (
                        <img
                          src={purchase.qrCode.qrCodeUrl}
                          className="w-full opacity-60 filter grayscale contrast-200 group-hover:grayscale-0 group-hover:opacity-100 transition-all mix-blend-screen"
                          alt="QR Thumbnail"
                        />
                      )}
                    </div>

                    <div className="flex-1 p-4 flex flex-col justify-center relative">
                      {/* Operator & Fuel Info */}
                      <h3 className="font-black text-white text-2xl font-heading tracking-tight uppercase flex items-baseline gap-2">
                        {purchase.stationName}
                        <span className="text-primary text-sm font-mono tracking-wider ml-auto">{purchase.liters}L</span>
                      </h3>
                      <div className="flex items-center gap-2 mt-1">
                        <span className="text-gray-400 font-mono text-xs uppercase tracking-wider">{purchase.fuelName}</span>
                        {isUsed && <span className="text-[10px] bg-white/10 text-white/50 px-1 py-0.5 rounded ml-2">USED</span>}
                      </div>

                      {/* ID */}
                      <div className="mt-3 text-[10px] text-gray-600 font-mono tracking-widest uppercase truncate max-w-[150px]">
                        ID: {purchase.id}
                      </div>

                      <div className="absolute top-0 right-0 p-2 opacity-5 group-hover:opacity-20 transition-opacity">
                        <Zap className="w-12 h-12 text-primary" />
                      </div>
                    </div>
                  </button>
                </DialogPrimitive.Trigger>

                <DialogContent className="sm:max-w-md p-0 overflow-hidden bg-black border-2 border-primary shadow-[0_0_60px_rgba(0,255,128,0.3)] w-[95vw] h-[85vh] sm:h-auto flex flex-col">
                  <div className="relative flex-1 flex flex-col">
                    {/* Header */}
                    <div className="p-6 pb-4 relative overflow-hidden shrink-0">
                      <div className={`absolute inset-0 opacity-20 ${purchase.stationName === 'OKKO' ? 'bg-green-600' :
                          purchase.stationName === 'WOG' ? 'bg-emerald-500' :
                            purchase.stationName === 'UPG' ? 'bg-cyan-500' :
                              'bg-yellow-500'
                        }`} />
                      <div className="absolute inset-0 bg-gradient-to-b from-transparent to-black" />

                      <DialogPrimitive.Close className="absolute top-4 right-4 p-2 bg-black/60 hover:bg-primary hover:text-black transition-colors z-30 text-white border-2 border-white/20 rounded-full">
                        <X className="w-5 h-5" />
                      </DialogPrimitive.Close>

                      <div className="relative z-10 pt-4 text-center">
                        <h2 className="text-6xl font-black text-white font-heading uppercase tracking-tighter text-glow mb-2">{purchase.stationName}</h2>

                        <div className="inline-flex items-center justify-center gap-6 bg-white/5 border border-white/10 px-6 py-3 rounded-xl backdrop-blur-sm">
                          <div className="text-center">
                            <div className="text-[10px] text-gray-500 uppercase tracking-widest font-mono mb-1">Fuel</div>
                            <div className="text-2xl font-bold text-primary font-heading uppercase">{purchase.fuelName}</div>
                          </div>
                          <div className="w-[1px] h-8 bg-white/10" />
                          <div className="text-center">
                            <div className="text-[10px] text-gray-500 uppercase tracking-widest font-mono mb-1">Volume</div>
                            <div className="text-2xl font-bold text-white font-heading uppercase">{purchase.liters} L</div>
                          </div>
                        </div>
                      </div>
                    </div>

                    {/* QR Code Display section */}
                    <div className="flex-1 p-6 flex flex-col items-center justify-center gap-6 bg-black relative">
                      <div className={`relative transition-all duration-500 ${isUsed ? 'opacity-30 grayscale blur-sm scale-95' : 'opacity-100 scale-100'}`}>
                        <div className="absolute -inset-8 bg-primary/20 blur-3xl rounded-full opacity-50 animate-pulse" />
                        <div className="bg-white p-3 relative z-10 border-4 border-primary shadow-[0_0_40px_rgba(0,255,128,0.5)] rounded-lg">
                          {purchase.qrCode && (
                            <img
                              src={purchase.qrCode.qrCodeUrl}
                              className="w-56 h-56 object-contain" // Fixed size for consistency
                              alt="QR Code"
                            />
                          )}
                        </div>
                        {isUsed && (
                          <div className="absolute inset-0 z-20 flex items-center justify-center">
                            <RotateCcw className="w-12 h-12 text-white drop-shadow-[0_2px_10px_rgba(0,0,0,0.8)]" />
                          </div>
                        )}
                        {/* Scanner Line (only if active) */}
                        {!isUsed && <div className="absolute top-4 left-4 right-4 h-0.5 bg-red-500 shadow-[0_0_20px_red] z-20 animate-[scan_2s_ease-in-out_infinite]" />}
                      </div>

                      {/* Mark as Used Toggle */}
                      <button
                        onClick={() => toggleUsed(purchase.id)}
                        className={`w-full max-w-xs py-4 flex items-center justify-center gap-3 font-black text-lg uppercase tracking-wider transition-all border-2 ${isUsed
                            ? 'bg-transparent text-gray-500 border-gray-700 hover:border-white hover:text-white'
                            : 'bg-primary text-black border-primary hover:bg-primary/90 hover:shadow-[0_0_30px_rgba(0,255,128,0.4)]'
                          }`}
                      >
                        {isUsed ? (
                          <>
                            <RotateCcw className="w-5 h-5" />
                            Restore Code
                          </>
                        ) : (
                          <>
                            <CheckCircle2 className="w-5 h-5" />
                            Mark as Used
                          </>
                        )}
                      </button>

                    </div>

                    {/* Footer ID */}
                    <div className="p-4 bg-black/50 border-t border-white/5 text-center">
                      <div
                        onClick={() => copyToClipboard(purchase.id.toString())}
                        className="inline-flex items-center gap-2 text-xs text-gray-500 hover:text-primary cursor-pointer font-mono tracking-widest uppercase transition-colors"
                      >
                        <Copy className="w-3 h-3" />
                        ID: #{purchase.id}
                      </div>
                    </div>

                  </div>
                </DialogContent>
              </DialogPrimitive.Root>
            );
          })}
        </div>
      )}

      <style>{`
        @keyframes scan {
          0%, 100% { top: 1rem; opacity: 0; }
          50% { top: calc(100% - 1rem); opacity: 1; }
        }
      `}</style>
    </div>
  );
}

