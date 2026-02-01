import { useEffect, useState } from "react";
import { useLocation } from "wouter";
import { loadStripe, Stripe } from "@stripe/stripe-js";
import { Elements, PaymentElement, useStripe, useElements } from "@stripe/react-stripe-js";
import { ChevronLeft, CreditCard, Lock, Loader2 } from "lucide-react";
import { useStore } from "@/lib/store";
import { getStripeConfig, createStripeCheckout } from "@/lib/api";
import { toast } from "sonner";
import { useI18n } from "@/lib/i18n";

let stripePromise: Promise<Stripe | null> | null = null;

const getStripe = async () => {
    if (!stripePromise) {
        const { publishableKey } = await getStripeConfig();
        stripePromise = loadStripe(publishableKey);
    }
    return stripePromise;
};

function PaymentForm({ clientSecret, amount }: { clientSecret: string; amount: number }) {
    const stripe = useStripe();
    const elements = useElements();
    const [, setLocation] = useLocation();
    const [isProcessing, setIsProcessing] = useState(false);
    const { clearCart } = useStore();
    const { t } = useI18n();

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        if (!stripe || !elements) {
            return;
        }

        setIsProcessing(true);

        try {
            const { error } = await stripe.confirmPayment({
                elements,
                confirmParams: {
                    return_url: `${window.location.origin}/payment-success`,
                },
            });

            if (error) {
                toast.error(error.message || "Payment failed");
                setIsProcessing(false);
            } else {
                // Redirect to success page which will handle order creation and cart clearing
                setLocation("/payment-success");
            }
        } catch (err: any) {
            toast.error(err.message || "Payment failed");
            setIsProcessing(false);
        }
    };

    return (
        <form onSubmit={handleSubmit} className="space-y-6">
            {/* Payment Element */}
            <div className="bg-black/40 border border-white/10 p-6 rounded-lg">
                <PaymentElement
                    options={{
                        layout: "tabs",
                    }}
                />
            </div>

            {/* Security Notice */}
            <div className="flex items-center gap-2 text-xs text-gray-400 justify-center">
                <Lock className="w-4 h-4 text-primary" />
                <span>{t('payment.securedBy')}</span>
            </div>

            {/* Pay Button */}
            <button
                type="submit"
                disabled={!stripe || isProcessing}
                className="w-full bg-primary hover:bg-primary/90 text-black py-5 font-black text-xl flex items-center justify-center gap-3 disabled:opacity-50 active:scale-[0.98] transition-all font-heading tracking-wider uppercase shadow-[0_0_40px_rgba(0,255,128,0.5)]"
            >
                {isProcessing ? (
                    <span className="animate-pulse flex items-center gap-3">
                        <Loader2 className="w-6 h-6 animate-spin" />
                        {t('payment.processing')}
                    </span>
                ) : (
                    <>
                        <CreditCard className="w-6 h-6" />
                        {t('checkout.pay')} {amount} ₴
                    </>
                )}
            </button>
        </form>
    );
}

export default function PaymentPage() {
    const [, setLocation] = useLocation();
    const { cart, getDiscountedTotal, clearCart } = useStore();
    const [clientSecret, setClientSecret] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const { t } = useI18n();

    const total = getDiscountedTotal();

    useEffect(() => {
        if (cart.length === 0) {
            setLocation("/");
            return;
        }

        const initializePayment = async () => {
            try {
                setIsLoading(true);

                // Create payment intent
                const itemsSummary = cart.map(item =>
                    `${item.station.name} - ${item.fuel.name} ${item.package.liters}L x${item.quantity}`
                ).join(', ');

                const response = await fetch('/api/payments/create-payment-intent', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'include',
                    body: JSON.stringify({
                        amount: total,
                        metadata: {
                            items: itemsSummary,
                            cartCount: cart.length.toString(),
                        }
                    }),
                });

                const data = await response.json();

                if (data.error) {
                    throw new Error(data.error);
                }

                setClientSecret(data.clientSecret);
            } catch (err: any) {
                console.error('Payment initialization error:', err);
                setError(err.message || 'Failed to initialize payment');
                toast.error('Failed to initialize payment');
            } finally {
                setIsLoading(false);
            }
        };

        initializePayment();
    }, [cart, total]);

    if (cart.length === 0) {
        return null;
    }

    return (
        <div className="min-h-screen bg-background flex flex-col relative">
            {/* Background effects */}
            <div className="absolute top-0 left-1/2 -translate-x-1/2 w-full h-64 bg-gradient-to-b from-primary/10 to-transparent" />
            <div className="absolute bottom-0 right-0 w-64 h-64 bg-primary/10 rounded-full blur-[100px]" />

            {/* Header */}
            <div className="bg-black/90 p-4 flex items-center gap-4 border-b-2 border-primary/30 relative z-10">
                <button
                    onClick={() => setLocation("/checkout")}
                    className="p-2 -ml-2 border-2 border-white/20 hover:border-primary transition-colors bg-black/50"
                >
                    <ChevronLeft className="w-6 h-6 text-gray-400" />
                </button>
                <h1 className="font-black text-xl text-white font-heading tracking-wider uppercase flex items-center gap-2">
                    <CreditCard className="w-5 h-5 text-primary" />
                    {t('payment.title')}
                </h1>
            </div>

            {/* Content */}
            <div className="p-6 flex-1 relative z-10">
                {isLoading ? (
                    <div className="flex flex-col items-center justify-center min-h-[400px]">
                        <Loader2 className="w-12 h-12 text-primary animate-spin mb-4" />
                        <p className="text-gray-400 font-mono">{t('payment.initializing')}</p>
                    </div>
                ) : error ? (
                    <div className="flex flex-col items-center justify-center min-h-[400px]">
                        <div className="text-red-500 text-center mb-4">
                            <p className="font-bold text-lg mb-2">{t('payment.error')}</p>
                            <p className="text-sm text-gray-400">{error}</p>
                        </div>
                        <button
                            onClick={() => setLocation("/checkout")}
                            className="bg-white/10 hover:bg-white/20 text-white px-6 py-3 font-bold rounded"
                        >
                            {t('payment.backToCheckout')}
                        </button>
                    </div>
                ) : clientSecret ? (
                    <Elements
                        stripe={getStripe()}
                        options={{
                            clientSecret,
                            appearance: {
                                theme: 'night',
                                variables: {
                                    colorPrimary: '#00ff80',
                                    colorBackground: '#1a1a1a',
                                    colorText: '#ffffff',
                                    colorDanger: '#ff4444',
                                    fontFamily: 'system-ui, sans-serif',
                                    borderRadius: '8px',
                                },
                            },
                        }}
                    >
                        <div className="max-w-md mx-auto">
                            {/* Order Summary */}
                            <div className="bg-black/60 border border-primary/30 p-6 rounded-lg mb-6">
                                <h2 className="text-white font-bold text-lg mb-4 uppercase tracking-wider">{t('payment.orderSummary')}</h2>
                                {cart.map((item) => (
                                    <div key={item.id} className="flex justify-between text-sm mb-2">
                                        <span className="text-gray-400">
                                            {item.station.name} - {item.fuel.name} {item.package.liters}L x{item.quantity}
                                        </span>
                                        <span className="text-white font-bold">
                                            {item.package.price * item.quantity} ₴
                                        </span>
                                    </div>
                                ))}
                                <div className="border-t border-white/10 mt-4 pt-4 flex justify-between">
                                    <span className="text-white font-black text-xl uppercase">{t('checkout.total')}</span>
                                    <span className="text-primary font-black text-2xl">{total} ₴</span>
                                </div>
                            </div>

                            {/* Payment Form */}
                            <PaymentForm clientSecret={clientSecret} amount={total} />
                        </div>
                    </Elements>
                ) : null}
            </div>
        </div>
    );
}
