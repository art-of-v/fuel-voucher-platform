
import { useState } from 'react';
import { useLocation, useSearch } from 'wouter';
import { completePurchase } from '../lib/api';
import { useStore } from '../lib/store';
import { Zap, CreditCard, ShieldCheck } from 'lucide-react';

export default function MockPayment() {
    // wouter's useSearch returns the query string (e.g. "?foo=bar")
    const searchString = useSearch();
    const parameters = new URLSearchParams(searchString);

    // Support multiple IDs (checkout) or single ID (legacy/backend redirect)
    const purchaseIdsParam = parameters.get('purchase_ids');
    const purchaseIdParam = parameters.get('purchase_id');

    const [, setLocation] = useLocation();
    const clearCart = useStore((state) => state.clearCart);

    const [step, setStep] = useState<'methods' | 'processing' | 'error'>('methods');
    const [selectedMethod, setSelectedMethod] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    const handleProcessPayment = async () => {
        if (!selectedMethod) return;
        setStep('processing');

        // Simulate payment processing delay based on method
        const delay = selectedMethod === 'card' ? 2000 : 1500;
        await new Promise(resolve => setTimeout(resolve, delay));

        try {
            const idsToProcess: number[] = [];
            if (purchaseIdsParam) {
                idsToProcess.push(...purchaseIdsParam.split(',').map(id => parseInt(id, 10)).filter(n => !isNaN(n)));
            }
            if (purchaseIdParam) {
                const pid = parseInt(purchaseIdParam, 10);
                if (!idsToProcess.includes(pid)) {
                    idsToProcess.push(pid);
                }
            }

            if (idsToProcess.length === 0) throw new Error("Missing transaction ID(s)");

            // Complete all purchases
            for (const id of idsToProcess) {
                await completePurchase(id);
            }

            // Clear the cart on success
            clearCart();

            // Redirect to success page
            setLocation(`/success?purchase_ids=${idsToProcess.join(',')}`);
        } catch (err: any) {
            console.error('Mock payment error:', err);
            setError(err.message || 'Payment failed');
            setStep('error');
        }
    };

    if (step === 'methods') {
        return (
            <div className="min-h-screen bg-black text-white p-6 flex flex-col pt-12 relative overflow-hidden">
                {/* Background effects */}
                <div className="absolute top-0 right-0 w-64 h-64 bg-primary/10 rounded-full blur-[100px]" />

                <header className="relative z-10 mb-8 border-b border-white/10 pb-4">
                    <h1 className="text-3xl font-black font-heading mb-2 uppercase text-center flex items-center justify-center gap-2">
                        <ShieldCheck className="w-8 h-8 text-primary" />
                        Checkout
                    </h1>
                    <p className="text-center text-gray-400 font-mono text-sm uppercase tracking-wider">Select Payment Method</p>
                </header>

                <div className="space-y-4 max-w-md mx-auto w-full flex-1 relative z-10">
                    {[
                        { id: 'card', name: 'Credit / Debit Card', icon: '💳' },
                        { id: 'apple', name: 'Apple Pay', icon: '' },
                        { id: 'google', name: 'Google Pay', icon: 'G' },
                    ].map(method => (
                        <button
                            key={method.id}
                            onClick={() => setSelectedMethod(method.id)}
                            className={`w-full p-6 border-2 flex items-center gap-6 transition-all uppercase font-bold tracking-wider group ${selectedMethod === method.id
                                    ? 'border-primary bg-primary/10 text-white shadow-[0_0_30px_rgba(0,255,128,0.2)]'
                                    : 'border-white/10 bg-white/5 text-gray-400 hover:border-white/30 hover:bg-white/10'
                                }`}
                        >
                            <span className="text-3xl group-hover:scale-110 transition-transform">{method.icon}</span>
                            <span className="font-heading text-lg">{method.name}</span>
                        </button>
                    ))}
                </div>

                <div className="mt-8 max-w-md mx-auto w-full relative z-10">
                    <button
                        disabled={!selectedMethod}
                        onClick={handleProcessPayment}
                        className="w-full bg-primary disabled:opacity-50 disabled:cursor-not-allowed text-black py-5 font-black text-xl uppercase tracking-wider shadow-[0_0_30px_rgba(0,255,128,0.4)] hover:shadow-[0_0_50px_rgba(0,255,128,0.6)] transition-all active:scale-[0.98] flex items-center justify-center gap-3"
                    >
                        <CreditCard className="w-6 h-6" />
                        PAY NOW
                    </button>
                    <button
                        onClick={() => setLocation('/basket')}
                        className="w-full mt-4 py-3 text-gray-500 font-mono text-sm uppercase tracking-widest hover:text-white transition-colors"
                    >
                        Cancel Transaction
                    </button>
                </div>
            </div>
        );
    }

    return (
        <div className="min-h-screen bg-black flex items-center justify-center p-6 relative">
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,_var(--tw-gradient-stops))] from-primary/5 via-black to-black" />

            <div className="w-full max-w-md bg-black/80 border-2 border-white/10 p-8 text-center relative overflow-hidden backdrop-blur-xl shadow-2xl z-10">
                {step === 'processing' ? (
                    <>
                        <div className="absolute top-0 left-0 w-full h-1 bg-primary/20">
                            <div className="h-full bg-primary animate-[loading_2s_ease-in-out_infinite]" style={{ width: '50%' }} />
                        </div>
                        <div className="w-20 h-20 border-4 border-white/10 border-t-primary rounded-full animate-spin mx-auto mb-8 shadow-[0_0_30px_rgba(0,255,128,0.2)]" />
                        <h2 className="text-3xl font-black text-white uppercase mb-4 font-heading tracking-tight animate-pulse">Processing</h2>
                        <div className="flex items-center justify-center gap-2 text-primary font-mono text-xs uppercase tracking-[0.2em] animate-pulse">
                            <Zap className="w-4 h-4" />
                            <span>Securing Assets</span>
                        </div>
                    </>
                ) : (
                    <>
                        <div className="w-20 h-20 bg-red-500/10 border-2 border-red-500 rounded-full flex items-center justify-center mx-auto mb-6 shadow-[0_0_30px_rgba(239,68,68,0.3)]">
                            <span className="text-4xl">❌</span>
                        </div>
                        <h2 className="text-3xl font-black text-red-500 uppercase mb-4 font-heading">Payment Failed</h2>
                        <p className="text-gray-300 mb-8 font-mono text-sm">{error}</p>
                        <button
                            onClick={() => setLocation('/basket')}
                            className="w-full py-4 border-2 border-white/20 hover:border-white hover:bg-white/10 text-white font-mono uppercase tracking-wider transition-all font-bold"
                        >
                            Return to Cart
                        </button>
                    </>
                )}
            </div>
            <style>{`
                @keyframes loading {
                    0% { width: 0%; left: 0; }
                    50% { width: 70%; left: 30%; }
                    100% { width: 0%; left: 100%; }
                }
            `}</style>
        </div>
    );
}
