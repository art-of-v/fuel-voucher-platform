
import { useState, useEffect } from "react";
import { X, Copy, QrCode, Zap, Wallet, ShieldCheck, AlertTriangle, CheckCircle2, RotateCcw, Clock, Loader2 } from "lucide-react";
import { DialogContent } from "@/components/ui/dialog";
import * as DialogPrimitive from "@radix-ui/react-dialog";
import { getMyVouchers, Voucher, markVoucherAsUsed, restoreVoucher, getMyOrders, Order } from "@/lib/api";
import { toast } from "sonner";
import { useI18n } from "@/lib/i18n";
import { QRCodeCanvas } from "qrcode.react";

export default function MyCodesScreen() {
  const [vouchers, setVouchers] = useState<Voucher[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const { t } = useI18n();

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      const [vouchersData, ordersData] = await Promise.all([
        getMyVouchers(),
        getMyOrders()
      ]);
      console.log("DEBUG: Vouchers received:", vouchersData);
      console.log("DEBUG: Orders received:", ordersData);
      setVouchers(Array.isArray(vouchersData) ? vouchersData : []);
      setOrders(Array.isArray(ordersData) ? ordersData : []);
    } catch (error: any) {
      console.error("Failed to load data:", error);
      if (error.message?.includes('401')) {
        setVouchers([]);
        setOrders([]);
      } else {
        toast.error("Failed to load your vouchers");
      }
    } finally {
      setLoading(false);
    }
  };

  const toggleUsed = async (voucher: Voucher) => {
    try {
      const isCurrentlyUsed = voucher.status === 'used';

      if (isCurrentlyUsed) {
        // Restore voucher
        await restoreVoucher(voucher.id);
        toast.success(t('codes.restoreCode'));
      } else {
        // Mark as used
        await markVoucherAsUsed(voucher.id);
        toast.success(t('codes.markAsUsed'));
      }

      // Reload data to get updated status from server
      await loadData();
    } catch (error: any) {
      console.error('Failed to toggle voucher status:', error);
      toast.error(error.message || 'Failed to update voucher status');
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
    <div className="p-6 space-y-6 pt-10 relative pb-24">
      {/* Background effects */}
      <div className="absolute top-0 right-0 w-64 h-64 bg-primary/10 rounded-full blur-[100px]" />

      <header className="relative z-10">
        <div className="flex items-center gap-3 mb-2">
          <Wallet className="w-8 h-8 text-primary" />
          <h1 className="text-4xl font-black text-white font-heading tracking-tight uppercase">{t('codes.myAssets')}</h1>
        </div>
        <div className="flex items-center gap-2 text-primary text-xs font-mono tracking-[0.2em] uppercase">
          <ShieldCheck className="w-4 h-4" />
          <span>{t('codes.secureVault')}</span>
        </div>
        <button
          onClick={async () => {
            console.log("Triggering manual loadData...");
            await loadData();
          }}
          className="mt-2 px-2 py-1 bg-primary/20 text-primary text-[10px] font-mono rounded relative z-20"
        >
          DEBUG: FORCE RELOAD
        </button>
      </header>

      {vouchers.length === 0 && orders.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 text-gray-600 border-2 border-dashed border-white/10 bg-black/50 relative z-10">
          <QrCode className="w-16 h-16 mb-4 opacity-20" />
          <p className="font-mono text-lg uppercase tracking-wider">{t('codes.noActiveCodes')}</p>
          <p className="text-xs text-gray-500 mt-2 font-mono">{t('codes.purchaseToStart')}</p>
        </div>
      ) : (
        <div className="space-y-6 relative z-10">
          {/* Pending Orders Section */}
          {orders.filter(o => o.status === 'PENDING_FULFILLMENT').length > 0 && (
            <div className="space-y-3">
              <div className="flex items-center gap-2 text-yellow-500">
                <Clock className="w-4 h-4" />
                <span className="text-xs font-mono uppercase tracking-wider">Pending Fulfillment</span>
              </div>
              {orders.filter(o => o.status === 'PENDING_FULFILLMENT').map((order) => (
                <div
                  key={order.id}
                  className="w-full bg-black/80 border-2 border-yellow-500/30 p-4 relative overflow-hidden"
                >
                  <div className="absolute top-0 right-0 left-0 h-1 bg-gradient-to-r from-yellow-500/50 to-transparent" />
                  <div className="flex items-center justify-between">
                    <div>
                      <h3 className="font-black text-white text-xl font-heading tracking-tight uppercase flex items-center gap-2">
                        {order.provider}
                        <Loader2 className="w-4 h-4 text-yellow-500 animate-spin" />
                      </h3>
                      <div className="flex items-center gap-2 mt-1">
                        <span className="text-gray-400 font-mono text-xs uppercase tracking-wider">{order.fuelType}</span>
                        <span className="text-primary text-sm font-mono">{order.liters}L</span>
                        {order.quantity > 1 && <span className="text-gray-500 text-xs">x{order.quantity}</span>}
                      </div>
                    </div>
                    <div className="text-right">
                      <span className="inline-flex items-center gap-1 px-2 py-1 bg-yellow-500/20 text-yellow-500 text-xs font-mono uppercase rounded">
                        <Clock className="w-3 h-3" />
                        Pending
                      </span>
                      <p className="text-[10px] text-gray-600 mt-1 font-mono">
                        {new Date(order.createdAt).toLocaleDateString()}
                      </p>
                    </div>
                  </div>
                  <p className="text-xs text-gray-500 mt-2 font-mono">
                    Your voucher will appear here once processed.
                  </p>
                </div>
              ))}
            </div>
          )}

          {/* Fulfilled Vouchers */}
          {vouchers.length > 0 && (
            <div className="space-y-3">
              {vouchers.map((voucher) => {
                const isUsed = voucher.status === 'used';
                const providerName = voucher.provider || "UNKNOWN";

                return (
                  <DialogPrimitive.Root key={voucher.id}>
                    <DialogPrimitive.Trigger asChild>
                      <button
                        data-testid={`code-${voucher.id}`}
                        className={`w-full bg-black/80 border-2 p-0 flex items-stretch group active:scale-[0.98] transition-all text-left overflow-hidden hover:box-glow relative ${isUsed ? 'border-gray-800 opacity-60 grayscale' : 'border-white/10 hover:border-primary'
                          }`}
                      >
                        {isUsed && (
                          <div className="absolute inset-0 z-20 flex items-center justify-center pointer-events-none">
                            <div className="border-4 border-white/20 text-white/20 font-black text-4xl uppercase -rotate-12 p-4 tracking-widest">
                              {t('codes.used')}
                            </div>
                          </div>
                        )}

                        {/* QR Preview (Optional: show icon if no URL yet, or generic) */}
                        <div className="w-24 bg-primary/5 border-r-2 border-primary/20 flex items-center justify-center p-3 relative overflow-hidden">
                          {voucher.qrCodeUrl ? (
                            <img
                              src={voucher.qrCodeUrl}
                              className="w-full opacity-60 filter grayscale contrast-200 group-hover:grayscale-0 group-hover:opacity-100 transition-all mix-blend-screen"
                              alt="QR Thumbnail"
                            />
                          ) : (
                            <QrCode className="w-8 h-8 text-primary/40" />
                          )}
                        </div>

                        <div className="flex-1 p-4 flex flex-col justify-center relative">
                          {/* Operator & Fuel Info */}
                          <h3 className="font-black text-white text-2xl font-heading tracking-tight uppercase flex items-baseline gap-2">
                            {providerName}
                            <span className="text-primary text-sm font-mono tracking-wider ml-auto">{voucher.amount}L</span>
                          </h3>
                          <div className="flex items-center gap-2 mt-1">
                            <span className="text-gray-400 font-mono text-xs uppercase tracking-wider">{voucher.fuelType}</span>
                            {isUsed && <span className="text-[10px] bg-white/10 text-white/50 px-1 py-0.5 rounded ml-2">{t('codes.used')}</span>}
                          </div>

                          {/* ID */}
                          <div className="mt-3 text-[10px] text-gray-600 font-mono tracking-widest uppercase truncate max-w-[150px]">
                            ID: {voucher.externalId || voucher.id.substring(0, 8)}
                          </div>

                          <div className="absolute top-0 right-0 p-2 opacity-5 group-hover:opacity-20 transition-opacity">
                            <Zap className="w-12 h-12 text-primary" />
                          </div>
                        </div>
                      </button>
                    </DialogPrimitive.Trigger>

                    <DialogContent className="sm:max-w-md p-0 bg-black border-2 border-primary shadow-[0_0_60px_rgba(0,255,128,0.3)] w-[95vw] max-h-[92vh] sm:h-auto flex flex-col overflow-y-auto no-scrollbar pb-safe">
                      <div className="relative flex-1 flex flex-col">
                        {/* Header */}
                        <div className="p-6 pb-4 relative overflow-hidden shrink-0">
                          <div className={`absolute inset-0 opacity-20 ${providerName.toUpperCase() === 'OKKO' ? 'bg-green-600' :
                            providerName.toUpperCase() === 'WOG' ? 'bg-emerald-500' :
                              providerName.toUpperCase() === 'UPG' ? 'bg-cyan-500' :
                                'bg-yellow-500'
                            }`} />
                          <div className="absolute inset-0 bg-gradient-to-b from-transparent to-black" />

                          <DialogPrimitive.Close className="absolute top-4 right-4 p-2 bg-black/60 hover:bg-primary hover:text-black transition-colors z-30 text-white border-2 border-white/20 rounded-full">
                            <X className="w-5 h-5" />
                          </DialogPrimitive.Close>

                          <div className="relative z-10 pt-4 text-center">
                            <h2 className="text-4xl sm:text-6xl font-black text-white font-heading uppercase tracking-tighter text-glow mb-2">{providerName}</h2>

                            <div className="inline-flex items-center justify-center gap-6 bg-white/5 border border-white/10 px-6 py-3 rounded-xl backdrop-blur-sm">
                              <div className="text-center">
                                <div className="text-[10px] text-gray-500 uppercase tracking-widest font-mono mb-1">Fuel</div>
                                <div className="text-2xl font-bold text-primary font-heading uppercase">{voucher.fuelType}</div>
                              </div>
                              <div className="w-[1px] h-8 bg-white/10" />
                              <div className="text-center">
                                <div className="text-[10px] text-gray-500 uppercase tracking-widest font-mono mb-1">Volume</div>
                                <div className="text-2xl font-bold text-white font-heading uppercase">{voucher.amount} L</div>
                              </div>
                            </div>
                          </div>
                        </div>

                        {/* QR Code Display section */}
                        <div className="flex-1 p-4 sm:p-6 flex flex-col items-center justify-center gap-4 sm:gap-6 bg-black relative">
                          <div className={`relative transition-all duration-500 w-full flex flex-col items-center ${isUsed ? 'opacity-30 grayscale blur-sm scale-95' : 'opacity-100 scale-100'}`}>
                            <div className="absolute -inset-8 bg-primary/20 blur-3xl rounded-full opacity-50 animate-pulse" />
                            <div className="bg-white p-2 sm:p-3 relative z-10 border-4 border-primary shadow-[0_0_40px_rgba(0,255,128,0.5)] rounded-lg max-w-[280px] w-full aspect-square flex items-center justify-center">
                              {voucher.qrCodeData ? (
                                <div className="bg-white p-2 rounded-lg">
                                  <QRCodeCanvas
                                    value={voucher.qrCodeData}
                                    size={1024} // Internal resolution high
                                    style={{ width: '100%', height: '100%' }} // Responsive display
                                    level="L"
                                  />
                                </div>
                              ) : voucher.qrCodeUrl ? (
                                <img
                                  src={voucher.qrCodeUrl}
                                  className="w-full h-full object-contain"
                                  alt="QR Code"
                                />
                              ) : (
                                <div className="w-full h-full flex items-center justify-center bg-gray-200">
                                  <p className="text-black font-mono text-center text-[10px]">NO QR DATA</p>
                                </div>
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
                            onClick={() => toggleUsed(voucher)}
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
                        <div className="p-4 pb-8 bg-black/50 border-t border-white/5 text-center shrink-0">
                          <div
                            onClick={() => copyToClipboard(voucher.externalId || voucher.id)}
                            className="inline-flex items-center gap-2 text-[10px] text-gray-500 hover:text-primary cursor-pointer font-mono tracking-widest uppercase transition-colors"
                          >
                            <Copy className="w-3 h-3" />
                            ID: {voucher.externalId || `#${voucher.id.substring(0, 8)}...`}
                          </div>
                        </div>

                      </div>
                    </DialogContent>
                  </DialogPrimitive.Root>
                );
              })}
            </div>
          )}

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
