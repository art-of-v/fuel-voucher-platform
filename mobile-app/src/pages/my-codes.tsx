
import { useState, useEffect } from "react";
import { X, Copy, QrCode, Zap, Skull, ShieldCheck, AlertTriangle } from "lucide-react";
import { DialogContent } from "@/components/ui/dialog";
import * as DialogPrimitive from "@radix-ui/react-dialog";
import { getMyPurchases } from "@/lib/api";
import { useAuth } from "@/hooks/useAuth";
import { toast } from "sonner";

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

  useEffect(() => {
    loadPurchases();
  }, []);

  const loadPurchases = async () => {
    try {
      const data = await getMyPurchases();
      const deliveredPurchases = data.filter(p => p.status === 'delivered' && p.qrCode);
      setPurchases(deliveredPurchases);
    } catch (error: any) {
      console.error("Failed to load purchases:", error);
      if (error.message?.includes('401')) {
        // User not logged in - show empty state
        setPurchases([]);
      } else {
        toast.error("Failed to load your codes");
      }
    } finally {
      setLoading(false);
    }
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
    <div className="p-6 space-y-6 pt-10 relative">
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
          {purchases.map((purchase) => (
            <DialogPrimitive.Root key={purchase.id}>
              <DialogPrimitive.Trigger asChild>
                <button 
                  data-testid={`code-${purchase.id}`}
                  className="w-full bg-black/80 border-2 border-white/10 hover:border-primary p-0 flex items-stretch group active:scale-[0.98] transition-all text-left overflow-hidden hover:box-glow"
                >
                  {/* QR Preview */}
                  <div className="w-24 bg-primary/10 border-r-2 border-primary/30 flex items-center justify-center p-3">
                    {purchase.qrCode && (
                      <img 
                        src={purchase.qrCode.qrCodeUrl} 
                        className="w-full opacity-60 filter grayscale contrast-200 group-hover:grayscale-0 group-hover:opacity-100 transition-all" 
                        alt="QR Thumbnail" 
                      />
                    )}
                  </div>
                  
                  <div className="flex-1 p-4 flex flex-col justify-center relative">
                    <div className="absolute top-0 right-0 p-2 opacity-10 group-hover:opacity-30 transition-opacity">
                      <Zap className="w-12 h-12 text-primary" />
                    </div>
                    <h3 className="font-black text-white text-2xl font-heading tracking-tight uppercase">{purchase.fuelName}</h3>
                    <p className="text-xs text-gray-400 font-mono mt-1 tracking-wider">{purchase.stationName} • {purchase.liters}L</p>
                    <div className="flex items-center gap-2 mt-2">
                      <div className="text-[10px] px-2 py-1 bg-primary/10 border border-primary/30 text-primary font-mono uppercase tracking-wider">
                        {purchase.status}
                      </div>
                    </div>
                  </div>
                </button>
              </DialogPrimitive.Trigger>

              <DialogContent className="sm:max-w-md p-0 overflow-hidden bg-black border-2 border-primary shadow-[0_0_60px_rgba(0,255,128,0.3)]">
                <div className="relative">
                  {/* Header */}
                  <div className="h-36 p-6 flex flex-col justify-end relative overflow-hidden">
                    <div className={`absolute inset-0 opacity-30 ${
                      purchase.stationName === 'OKKO' ? 'bg-green-600' :
                      purchase.stationName === 'WOG' ? 'bg-emerald-500' :
                      purchase.stationName === 'UPG' ? 'bg-cyan-500' :
                      'bg-yellow-500'
                    }`} />
                    <div className="absolute inset-0 bg-gradient-to-t from-black to-transparent" />
                    
                    <DialogPrimitive.Close className="absolute top-4 right-4 p-2 bg-black/60 hover:bg-primary hover:text-black transition-colors z-20 text-white border-2 border-white/20">
                      <X className="w-5 h-5" />
                    </DialogPrimitive.Close>
                    
                    <div className="relative z-10">
                      <div className="flex items-center gap-2 mb-1">
                        <Zap className="w-6 h-6 text-primary" />
                        <h2 className="text-5xl font-black text-white font-heading uppercase tracking-tighter text-glow">{purchase.stationName}</h2>
                      </div>
                      <p className="text-gray-300 font-mono text-sm tracking-wider">{purchase.fuelName} • {purchase.liters}L</p>
                    </div>
                  </div>
                  
                  {/* QR Code Display */}
                  <div className="p-8 flex flex-col items-center gap-6 bg-black">
                    <div className="relative">
                      <div className="absolute -inset-4 bg-primary/30 blur-2xl rounded-full opacity-50 animate-pulse" />
                      <div className="bg-white p-4 relative z-10 border-4 border-primary shadow-[0_0_40px_rgba(0,255,128,0.5)]">
                        {purchase.qrCode && (
                          <img 
                            src={purchase.qrCode.qrCodeUrl} 
                            className="w-56 h-56" 
                            alt="QR Code" 
                          />
                        )}
                      </div>
                      {/* Scanner Line */}
                      <div className="absolute top-4 left-4 right-4 h-1 bg-red-500 shadow-[0_0_20px_red] z-20 animate-[scan_2s_ease-in-out_infinite]" />
                    </div>
                    
                    <div className="text-center space-y-3 w-full">
                      <div className="flex items-center gap-2 justify-center text-[10px] text-gray-500 uppercase tracking-[0.2em] font-mono">
                        <AlertTriangle className="w-3 h-3 text-red-500" />
                        <span>IDENTIFICATION SEQUENCE</span>
                      </div>
                      <div 
                        onClick={() => copyToClipboard(purchase.id.toString())}
                        className="bg-primary/10 border-2 border-primary/50 p-4 font-mono font-black text-xl text-primary flex items-center justify-between gap-2 group cursor-pointer hover:bg-primary hover:text-black transition-all"
                      >
                        <span>#{purchase.id.toString().padStart(8, '0')}</span>
                        <Copy className="w-5 h-5 opacity-50 group-hover:opacity-100" />
                      </div>
                    </div>
                  </div>
                </div>
              </DialogContent>
            </DialogPrimitive.Root>
          ))}
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
