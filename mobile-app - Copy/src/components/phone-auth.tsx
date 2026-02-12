import { useState } from "react";
import { Phone, ArrowRight, Lock, Loader2, Check } from "lucide-react";
import { useI18n } from "@/lib/i18n";
import { apiRequest } from "@/lib/utils";

type AuthStep = "phone" | "code" | "success";

interface PhoneAuthProps {
  onSuccess: () => void;
}

export function PhoneAuth({ onSuccess }: PhoneAuthProps) {
  const { t } = useI18n();
  const [step, setStep] = useState<AuthStep>("phone");
  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const handleSendCode = async () => {
    if (!phone.trim()) {
      setError(t('phoneAuth.enterPhone'));
      return;
    }

    setLoading(true);
    setError("");

    try {
      const res = await apiRequest("POST", "/api/auth/phone/send-code", { phone });
      const data = await res.json();
      setStep("code");
    } catch (err: any) {
      setError(err.message || t('phoneAuth.networkError'));
    } finally {
      setLoading(false);
    }
  };

  const handleVerifyCode = async () => {
    if (!code.trim() || code.length !== 6) {
      setError(t('phoneAuth.enterCode'));
      return;
    }

    setLoading(true);
    setError("");

    try {
      const res = await apiRequest("POST", "/api/auth/phone/verify", { phone, code });
      const data = await res.json();
      setStep("success");
      setTimeout(() => {
        onSuccess();
      }, 1500);
    } catch (err: any) {
      setError(err.message || t('phoneAuth.networkError'));
    } finally {
      setLoading(false);
    }
  };

  const handleCodeChange = (value: string) => {
    const digitsOnly = value.replace(/\D/g, "").slice(0, 6);
    setCode(digitsOnly);
  };

  return (
    <div className="space-y-6">
      {step === "phone" && (
        <>
          <div className="text-center mb-6">
            <div className="w-16 h-16 mx-auto bg-primary/20 border-2 border-primary flex items-center justify-center mb-4">
              <Phone className="w-8 h-8 text-primary" />
            </div>
            <h3 className="text-xl font-black text-white font-heading uppercase">
              {t('phoneAuth.title')}
            </h3>
            <p className="text-gray-400 text-sm mt-2">
              {t('phoneAuth.subtitle')}
            </p>
          </div>

          <div className="space-y-4">
            <div>
              <label className="block text-[10px] text-gray-500 font-mono uppercase tracking-wider mb-2">
                {t('phoneAuth.phoneLabel')}
              </label>
              <input
                type="tel"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                placeholder="+380XXXXXXXXX"
                data-testid="input-phone"
                className="w-full bg-black/50 border-2 border-white/20 px-4 py-4 text-white font-mono text-lg focus:border-primary focus:outline-none transition-colors"
              />
            </div>

            {error && (
              <p className="text-red-500 text-sm font-mono">{error}</p>
            )}

            <button
              onClick={handleSendCode}
              disabled={loading}
              data-testid="btn-send-code"
              className="w-full bg-primary text-black py-4 font-black text-lg flex items-center justify-center gap-3 hover:shadow-[0_0_40px_rgba(0,255,128,0.5)] transition-all disabled:opacity-50 font-heading uppercase"
            >
              {loading ? (
                <>
                  <Loader2 className="w-5 h-5 animate-spin" />
                  {t('phoneAuth.sending')}
                </>
              ) : (
                <>
                  {t('phoneAuth.sendCode')}
                  <ArrowRight className="w-5 h-5" />
                </>
              )}
            </button>
          </div>
        </>
      )}

      {step === "code" && (
        <>
          <div className="text-center mb-6">
            <div className="w-16 h-16 mx-auto bg-primary/20 border-2 border-primary flex items-center justify-center mb-4">
              <Lock className="w-8 h-8 text-primary" />
            </div>
            <h3 className="text-xl font-black text-white font-heading uppercase">
              {t('phoneAuth.enterCodeTitle')}
            </h3>
            <p className="text-gray-400 text-sm mt-2">
              {t('phoneAuth.codeSentTo')} <span className="text-primary">{phone}</span>
            </p>
          </div>

          <div className="space-y-4">
            <div>
              <label className="block text-[10px] text-gray-500 font-mono uppercase tracking-wider mb-2">
                {t('phoneAuth.codeLabel')}
              </label>
              <input
                type="text"
                inputMode="numeric"
                value={code}
                onChange={(e) => handleCodeChange(e.target.value)}
                placeholder="------"
                maxLength={6}
                data-testid="input-code"
                className="w-full bg-black/50 border-2 border-white/20 px-4 py-4 text-white font-mono text-3xl text-center tracking-[0.5em] focus:border-primary focus:outline-none transition-colors"
              />
            </div>

            {error && (
              <p className="text-red-500 text-sm font-mono">{error}</p>
            )}

            <button
              onClick={handleVerifyCode}
              disabled={loading || code.length !== 6}
              data-testid="btn-verify-code"
              className="w-full bg-primary text-black py-4 font-black text-lg flex items-center justify-center gap-3 hover:shadow-[0_0_40px_rgba(0,255,128,0.5)] transition-all disabled:opacity-50 font-heading uppercase"
            >
              {loading ? (
                <>
                  <Loader2 className="w-5 h-5 animate-spin" />
                  {t('phoneAuth.verifying')}
                </>
              ) : (
                <>
                  {t('phoneAuth.verify')}
                  <ArrowRight className="w-5 h-5" />
                </>
              )}
            </button>

            <button
              onClick={() => {
                setStep("phone");
                setCode("");
                setError("");
              }}
              data-testid="btn-back-phone"
              className="w-full text-gray-400 py-2 text-sm hover:text-white transition-colors"
            >
              {t('phoneAuth.changePhone')}
            </button>
          </div>
        </>
      )}

      {step === "success" && (
        <div className="text-center py-8">
          <div className="w-20 h-20 mx-auto bg-green-500/20 border-2 border-green-500 flex items-center justify-center mb-4 animate-pulse">
            <Check className="w-10 h-10 text-green-500" />
          </div>
          <h3 className="text-2xl font-black text-white font-heading uppercase">
            {t('phoneAuth.success')}
          </h3>
          <p className="text-gray-400 text-sm mt-2">
            {t('phoneAuth.redirecting')}
          </p>
        </div>
      )}
    </div>
  );
}
