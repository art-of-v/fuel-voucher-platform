import { useLocation } from "wouter";
import { CheckCircle, Home, Receipt } from "lucide-react";
import { useEffect, useState } from "react";
import { useI18n } from "@/lib/i18n";

export default function PaymentSuccessPage() {
    const [, setLocation] = useLocation();
    const [sessionId, setSessionId] = useState<string | null>(null);
    const { t } = useI18n();

    useEffect(() => {
        const params = new URLSearchParams(window.location.search);
        const id = params.get('session_id');
        setSessionId(id);
    }, []);

    return (
        <div className="min-h-screen bg-background flex flex-col items-center justify-center p-6 relative">
            {/* Background effects */}
            <div className="absolute top-1/4 left-1/2 -translate-x-1/2 w-96 h-96 bg-primary/20 rounded-full blur-[120px]" />

            <div className="relative z-10 text-center max-w-md">
                {/* Success Icon */}
                <div className="w-24 h-24 bg-gradient-to-br from-primary to-green-400 rounded-full flex items-center justify-center mx-auto mb-6 animate-scale-in shadow-[0_0_60px_rgba(0,255,128,0.5)]">
                    <CheckCircle className="w-12 h-12 text-black" />
                </div>

                {/* Title */}
                <h1 className="text-4xl font-black text-white font-heading uppercase mb-4 tracking-wider">
                    {t('paymentSuccess.title')}
                </h1>

                {/* Subtitle */}
                <p className="text-gray-400 font-mono mb-8 text-sm">
                    {t('paymentSuccess.subtitle')}
                </p>

                {/* Session Info */}
                {sessionId && (
                    <div className="bg-black/60 border border-primary/30 p-4 rounded-lg mb-8">
                        <div className="text-xs text-gray-500 uppercase tracking-wider mb-2">{t('paymentSuccess.sessionId')}</div>
                        <div className="text-primary font-mono text-xs break-all">{sessionId}</div>
                    </div>
                )}

                {/* Info Box */}
                <div className="bg-black/40 border border-white/10 p-6 rounded-lg mb-8">
                    <div className="flex items-start gap-3 text-left">
                        <Receipt className="w-5 h-5 text-primary mt-1 flex-shrink-0" />
                        <div className="text-sm text-gray-300">
                            <p className="mb-2">{t('paymentSuccess.confirmed')}</p>
                            <p className="text-gray-500">
                                {t('paymentSuccess.checkVouchers')}
                            </p>
                        </div>
                    </div>
                </div>

                {/* Action Buttons */}
                <div className="space-y-3">
                    <button
                        onClick={() => setLocation("/my-codes")}
                        className="w-full bg-primary hover:bg-primary/90 text-black py-4 font-black text-lg flex items-center justify-center gap-3 transition-all font-heading tracking-wider uppercase shadow-[0_0_40px_rgba(0,255,128,0.5)]"
                    >
                        <Receipt className="w-5 h-5" />
                        {t('paymentSuccess.viewVouchers')}
                    </button>

                    <button
                        onClick={() => setLocation("/")}
                        className="w-full bg-white/10 hover:bg-white/20 text-white py-4 font-bold text-lg flex items-center justify-center gap-3 transition-all font-heading tracking-wider uppercase border border-white/20"
                    >
                        <Home className="w-5 h-5" />
                        {t('paymentSuccess.backHome')}
                    </button>
                </div>
            </div>
        </div>
    );
}
