
import { useLocation } from "wouter";
import { CheckCircle2, QrCode, Zap, Skull, ArrowRight } from "lucide-react";
import { useEffect } from "react";
import confetti from "canvas-confetti";

export default function SuccessScreen() {
  const [, setLocation] = useLocation();

  useEffect(() => {
    const colors = ['#00ff80', '#00ffcc', '#ffffff', '#ff0000'];
    
    const end = Date.now() + 2000;

    (function frame() {
      confetti({
        particleCount: 5,
        angle: 60,
        spread: 70,
        origin: { x: 0 },
        colors: colors
      });
      confetti({
        particleCount: 5,
        angle: 120,
        spread: 70,
        origin: { x: 1 },
        colors: colors
      });

      if (Date.now() < end) {
        requestAnimationFrame(frame);
      }
    }());
  }, []);

  return (
    <div className="min-h-screen bg-background flex flex-col items-center justify-center p-8 text-center relative overflow-hidden">
      {/* Aggressive background */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-primary/20 blur-[150px] rounded-full animate-pulse" />
      <div className="absolute top-0 left-0 w-full h-full bg-[radial-gradient(circle_at_center,transparent_0%,rgba(0,0,0,0.8)_100%)]" />

      <div className="relative z-10">
        <div className="w-32 h-32 bg-primary/20 border-4 border-primary flex items-center justify-center mx-auto mb-8 shadow-[0_0_60px_rgba(0,255,128,0.6)] animate-pulse-glow">
          <CheckCircle2 className="w-16 h-16 text-primary drop-shadow-[0_0_20px_rgba(0,255,128,1)]" />
        </div>

        <h1 className="text-6xl font-black mb-2 text-primary font-heading tracking-tighter uppercase text-glow-intense animate-flicker">
          SUCCESS
        </h1>
        <h2 className="text-3xl font-bold mb-6 text-white font-heading tracking-tight uppercase">
          TRANSACTION COMPLETE
        </h2>
        <div className="flex items-center justify-center gap-3 text-gray-400 text-xs font-mono tracking-[0.2em] uppercase mb-12">
          <Zap className="w-4 h-4 text-primary" />
          <span>ASSETS SECURED IN WALLET</span>
          <Zap className="w-4 h-4 text-primary" />
        </div>

        <button
          onClick={() => setLocation("/my-codes")}
          data-testid="button-view-codes"
          className="w-full bg-primary text-black py-5 font-black text-xl flex items-center justify-center gap-3 shadow-[0_0_40px_rgba(0,255,128,0.5)] hover:shadow-[0_0_60px_rgba(0,255,128,0.7)] transition-all font-heading tracking-wider uppercase active:scale-[0.98]"
        >
          <QrCode className="w-6 h-6" />
          ACCESS WALLET
          <ArrowRight className="w-6 h-6" />
        </button>

        <button
          onClick={() => setLocation("/")}
          data-testid="button-home"
          className="mt-8 text-gray-500 text-xs font-mono uppercase tracking-[0.2em] hover:text-primary transition-colors flex items-center gap-2 mx-auto"
        >
          <Skull className="w-4 h-4" />
          RETURN TO DASHBOARD
        </button>
      </div>
    </div>
  );
}
