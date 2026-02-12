import { useLocation } from "wouter";
import { XCircle, Home, ShoppingCart } from "lucide-react";
import { useI18n } from "@/lib/i18n";
import { PageLayout } from "@/components/page-layout";

export default function PaymentCancelPage() {
    const [, setLocation] = useLocation();
    const { t } = useI18n();

    const background = (
        <div className="absolute top-1/4 left-1/2 -translate-x-1/2 w-96 h-96 bg-red-500/20 rounded-full blur-[120px]" />
    );

    return (
        <PageLayout background={background} scrollClassName="flex flex-col items-center justify-center p-6 h-full min-h-[50vh]">
            <div className="relative z-10 text-center max-w-md w-full">
                {/* Cancel Icon */}
                <div className="w-24 h-24 bg-gradient-to-br from-orange-500 to-red-500 rounded-full flex items-center justify-center mx-auto mb-6 animate-scale-in shadow-[0_0_60px_rgba(255,128,0,0.5)]">
                    <XCircle className="w-12 h-12 text-white" />
                </div>

                {/* Title */}
                <h1 className="text-4xl font-black text-white font-heading uppercase mb-4 tracking-wider">
                    {t('paymentCancel.title')}
                </h1>

                {/* Subtitle */}
                <p className="text-gray-400 font-mono mb-8 text-sm">
                    {t('paymentCancel.subtitle')}
                </p>

                {/* Info Box */}
                <div className="bg-black/40 border border-white/10 p-6 rounded-lg mb-8">
                    <p className="text-gray-300 text-sm">
                        {t('paymentCancel.message')}
                    </p>
                </div>

                {/* Action Buttons */}
                <div className="space-y-3">
                    <button
                        onClick={() => setLocation("/basket")}
                        className="w-full bg-primary hover:bg-primary/90 text-black py-4 font-black text-lg flex items-center justify-center gap-3 transition-all font-heading tracking-wider uppercase shadow-[0_0_40px_rgba(0,255,128,0.5)]"
                    >
                        <ShoppingCart className="w-5 h-5" />
                        {t('paymentCancel.backToCart')}
                    </button>

                    <button
                        onClick={() => setLocation("/")}
                        className="w-full bg-white/10 hover:bg-white/20 text-white py-4 font-bold text-lg flex items-center justify-center gap-3 transition-all font-heading tracking-wider uppercase border border-white/20"
                    >
                        <Home className="w-5 h-5" />
                        {t('paymentCancel.backHome')}
                    </button>
                </div>
            </div>
        </PageLayout>
    );
}
