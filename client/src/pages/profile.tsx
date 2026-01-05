
import { useState, useEffect } from "react";
import { useAuth } from "@/hooks/useAuth";
import { User, LogIn, LogOut, Mail, Phone, Zap, Car, Gift, Bell, Check } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import type { User as UserType } from "@shared/schema";
import { useI18n } from "@/lib/i18n";
import { PhoneAuth } from "@/components/phone-auth";
import { apiRequest } from "@/lib/utils";

export default function ProfileScreen() {
  const { user, isLoading, isAuthenticated } = useAuth();
  const { t } = useI18n();
  const [showPhoneAuth, setShowPhoneAuth] = useState(false);
  const [phoneUser, setPhoneUser] = useState<UserType | null>(null);
  const [phoneLoading, setPhoneLoading] = useState(true);
  const [referralInput, setReferralInput] = useState("");
  const queryClient = useQueryClient();

  const { data: notifications = [] } = useQuery<any[]>({
    queryKey: ["/api/notifications"],
    enabled: !!user,
  });

  const markReadMutation = useMutation({
    mutationFn: async (id: number) => {
      await apiRequest("POST", `/api/notifications/${id}/read`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/notifications"] });
    },
  });

  const redeemMutation = useMutation({
    mutationFn: async (code: string) => {
      const res = await apiRequest("POST", "/api/referral/redeem", { code });
      return res.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/auth/user"] }); // Refresh user for bonus balance
      queryClient.invalidateQueries({ queryKey: ["/api/notifications"] });
      setReferralInput("");
      alert("Referral code redeemed! Bonus applied.");
    },
    onError: (err: any) => {
      alert(err.message || "Failed to redeem code");
    }
  });

  // Check for phone auth user on mount
  useEffect(() => {
    const checkPhoneAuth = async () => {
      try {
        const res = await fetch("/api/auth/phone/user");
        if (res.ok) {
          const data = await res.json();
          setPhoneUser(data);
        }
      } catch (err) {
        // Not authenticated via phone
      } finally {
        setPhoneLoading(false);
      }
    };
    checkPhoneAuth();
  }, []);

  const handlePhoneLogout = async () => {
    try {
      await fetch("/api/auth/phone/logout", { method: "POST" });
      setPhoneUser(null);
    } catch (err) {
      console.error("Logout failed:", err);
    }
  };

  const handlePhoneAuthSuccess = async () => {
    // Re-check phone auth after successful login
    const res = await fetch("/api/auth/phone/user");
    if (res.ok) {
      const data = await res.json();
      setPhoneUser(data);
    }
    setShowPhoneAuth(false);
  };

  if (isLoading || phoneLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="text-primary font-mono animate-pulse flex items-center gap-3">
          <Zap className="w-6 h-6 animate-spin" />
          {t('common.loading')}
        </div>
      </div>
    );
  }

  // Show phone auth form
  if (showPhoneAuth) {
    return (
      <div className="min-h-screen p-6 pt-10 relative">
        <div className="absolute top-0 right-0 w-64 h-64 bg-primary/10 rounded-full blur-[100px]" />
        <PhoneAuth onSuccess={handlePhoneAuthSuccess} />
        <button
          onClick={() => setShowPhoneAuth(false)}
          className="w-full text-gray-400 py-4 text-sm hover:text-white transition-colors mt-4"
        >
          {t('common.back')}
        </button>
      </div>
    );
  }

  // If logged in via phone
  if (phoneUser) {
    return (
      <div className="min-h-screen p-6 pt-10 relative">
        <div className="absolute top-0 right-0 w-64 h-64 bg-primary/10 rounded-full blur-[100px]" />

        <header className="relative z-10 mb-8">
          <h1 className="text-4xl font-black text-white font-heading uppercase">{t('profile.title')}</h1>
          <p className="text-xs text-primary font-mono tracking-[0.2em] uppercase mt-1">
            {t('profile.subtitle')}
          </p>
        </header>

        <div className="relative z-10 space-y-6">
          <div className="bg-black/80 border-2 border-primary/30 p-6">
            <div className="flex items-center gap-4 mb-6">
              <div className="w-20 h-20 bg-primary/20 border-2 border-primary flex items-center justify-center">
                <Phone className="w-10 h-10 text-primary" />
              </div>
              <div>
                <h2 className="text-2xl font-black text-white font-heading uppercase">
                  {t('phoneAuth.title')}
                </h2>
                {phoneUser.phone && (
                  <p className="text-gray-400 font-mono">{phoneUser.phone}</p>
                )}
              </div>
            </div>

            <div className="flex items-center gap-3 text-gray-400 bg-white/5 p-3 border border-white/10">
              <Phone className="w-5 h-5 text-primary" />
              <span className="font-mono text-sm">{phoneUser.phone}</span>
            </div>
          </div>

          <button
            onClick={handlePhoneLogout}
            data-testid="button-phone-logout"
            className="w-full bg-red-500/20 border-2 border-red-500/50 text-red-400 py-4 font-black text-lg flex items-center justify-center gap-3 font-heading uppercase hover:bg-red-500 hover:text-white transition-all"
          >
            <LogOut className="w-6 h-6" />
            {t('profile.signOut')}
          </button>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center p-8 relative">
        <div className="absolute top-1/3 left-1/2 -translate-x-1/2 w-64 h-64 bg-primary/20 rounded-full blur-[100px]" />

        <div className="relative z-10 text-center">
          <div className="w-24 h-24 bg-primary/10 border-4 border-primary/30 flex items-center justify-center mx-auto mb-8">
            <User className="w-12 h-12 text-primary" />
          </div>

          <h1 className="text-4xl font-black text-white font-heading uppercase mb-4">
            {t('profile.accessRequired')}
          </h1>
          <p className="text-gray-400 font-mono text-sm mb-8 max-w-xs">
            {t('profile.signInDesc')}
          </p>

          <a
            href="/api/login"
            data-testid="button-login"
            className="inline-flex items-center gap-3 bg-primary text-black px-8 py-4 font-black text-lg font-heading uppercase shadow-[0_0_40px_rgba(0,255,128,0.5)] hover:shadow-[0_0_60px_rgba(0,255,128,0.7)] transition-all"
          >
            <LogIn className="w-6 h-6" />
            {t('profile.signIn')}
          </a>

          <p className="text-[10px] text-gray-600 font-mono mt-6 uppercase tracking-wider">
            {t('profile.authMethods')}
          </p>

          {/* Phone auth option */}
          <div className="mt-8 pt-8 border-t border-white/10">
            <p className="text-gray-500 text-sm mb-4">{t('phoneAuth.orPhone')}</p>
            <button
              onClick={() => setShowPhoneAuth(true)}
              data-testid="button-phone-login"
              className="inline-flex items-center gap-3 bg-white/10 border-2 border-white/20 text-white px-8 py-4 font-black text-lg font-heading uppercase hover:border-primary hover:text-primary transition-all"
            >
              <Phone className="w-6 h-6" />
              {t('phoneAuth.title')}
            </button>
          </div>

          {import.meta.env.DEV && (
            <div className="mt-8 pt-4 border-t border-white/5">
              <button
                onClick={async () => {
                  await fetch('/api/auth/dev-login', { method: 'POST' });
                  window.location.reload();
                }}
                className="text-xs text-gray-600 hover:text-primary font-mono uppercase tracking-widest"
              >
                [ DEV MODE: QUICK LOGIN ]
              </button>
            </div>
          )}
        </div>
      </div>
    );
  }

  const typedUser = user as UserType;

  return (
    <div className="min-h-screen p-6 pt-10 relative">
      <div className="absolute top-0 right-0 w-64 h-64 bg-primary/10 rounded-full blur-[100px]" />

      <header className="relative z-10 mb-8">
        <h1 className="text-4xl font-black text-white font-heading uppercase">{t('profile.title')}</h1>
        <p className="text-xs text-primary font-mono tracking-[0.2em] uppercase mt-1">
          {t('profile.subtitle')}
        </p>
      </header>

      <div className="relative z-10 space-y-6">

        {/* Notifications */}
        {notifications.length > 0 && (
          <div className="bg-black/80 border-2 border-primary/30 p-6 space-y-4">
            <h3 className="text-xl font-black text-white font-heading uppercase flex items-center gap-2">
              <Bell className="w-5 h-5 text-primary" />
              Updates
            </h3>
            <div className="space-y-2">
              {notifications.map((n) => (
                <div
                  key={n.id}
                  className={`p-3 border rounded-lg transition-all ${n.read ? 'bg-white/5 border-white/5 opacity-50' : 'bg-primary/5 border-primary/20'}`}
                  onClick={() => !n.read && markReadMutation.mutate(n.id)}
                >
                  <div className="flex justify-between items-start mb-1">
                    <h4 className={`font-bold text-sm uppercase ${n.read ? 'text-gray-400' : 'text-primary'}`}>{n.title}</h4>
                    {!n.read && <div className="w-2 h-2 rounded-full bg-primary animate-pulse" />}
                  </div>
                  <p className="text-xs text-gray-300 font-mono">{n.message}</p>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* User Card */}
        <div className="bg-black/80 border-2 border-primary/30 p-6">
          <div className="flex items-center gap-4 mb-6">
            {typedUser?.profileImageUrl ? (
              <img
                src={typedUser.profileImageUrl}
                alt="Profile"
                className="w-20 h-20 object-cover border-2 border-primary shadow-[0_0_20px_rgba(0,255,128,0.3)]"
              />
            ) : (
              <div className="w-20 h-20 bg-primary/20 border-2 border-primary flex items-center justify-center">
                <User className="w-10 h-10 text-primary" />
              </div>
            )}
            <div>
              <h2 className="text-2xl font-black text-white font-heading uppercase">
                {typedUser?.firstName || typedUser?.email?.split('@')[0] || 'Operator'}
              </h2>
              {typedUser?.lastName && (
                <p className="text-gray-400 font-heading uppercase">{typedUser.lastName}</p>
              )}
            </div>
          </div>

          {typedUser?.email && (
            <div className="flex items-center gap-3 text-gray-400 bg-white/5 p-3 border border-white/10">
              <Mail className="w-5 h-5 text-primary" />
              <span className="font-mono text-sm">{typedUser.email}</span>
            </div>
          )}
        </div>

        {/* Vehicle Information */}
        <div className="bg-black/80 border-2 border-primary/30 p-6 space-y-4">
          <h3 className="text-xl font-black text-white font-heading uppercase flex items-center gap-2">
            <Car className="w-5 h-5 text-primary" />
            Vehicle Details
          </h3>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1">
              <label className="text-xs text-gray-500 font-bold uppercase">Make</label>
              <input
                className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
                placeholder="e.g. BMW"
                defaultValue={typedUser?.vehicleMake || ""}
                onBlur={(e) => apiRequest("POST", `/api/users/update`, { vehicleMake: e.target.value })}
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-gray-500 font-bold uppercase">Model</label>
              <input
                className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
                placeholder="e.g. X5"
                defaultValue={typedUser?.vehicleModel || ""}
                onBlur={(e) => apiRequest("POST", `/api/users/update`, { vehicleModel: e.target.value })}
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-gray-500 font-bold uppercase">Plate</label>
              <input
                className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
                placeholder="AA0000AA"
                defaultValue={typedUser?.vehiclePlate || ""}
                onBlur={(e) => apiRequest("POST", `/api/users/update`, { vehiclePlate: e.target.value })}
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-gray-500 font-bold uppercase">Fuel</label>
              <select
                className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
                defaultValue={typedUser?.vehicleFuelType || ""}
                onChange={(e) => apiRequest("POST", `/api/users/update`, { vehicleFuelType: e.target.value })}
              >
                <option value="">Select</option>
                <option value="petrol">Petrol</option>
                <option value="diesel">Diesel</option>
                <option value="lpg">LPG</option>
                <option value="electric">Electric</option>
              </select>
            </div>
          </div>
        </div>

        {/* Referral System */}
        <div className="bg-gradient-to-br from-primary/10 to-transparent border-2 border-primary/30 p-6 space-y-4">
          <h3 className="text-xl font-black text-white font-heading uppercase flex items-center gap-2">
            <Gift className="w-5 h-5 text-primary" />
            Referral Program
          </h3>

          <div className="flex items-center justify-between bg-black/40 p-4 rounded-lg border border-primary/20">
            <div>
              <p className="text-xs text-gray-400 font-mono uppercase">Your Bonus Balance</p>
              <p className="text-3xl font-black text-primary font-heading">{typedUser?.bonusBalance || 0} UAH</p>
            </div>
            <Gift className="w-10 h-10 text-primary/20" />
          </div>

          {typedUser?.referralCode ? (
            <div className="space-y-2">
              <label className="text-xs text-gray-500 font-bold uppercase">Your Invite Code</label>
              <div className="flex gap-2">
                <div className="flex-1 bg-white/5 border border-white/10 rounded-lg flex items-center justify-center p-3 font-mono text-xl tracking-widest text-primary border-dashed">
                  {typedUser.referralCode}
                </div>
                <button
                  onClick={() => navigator.clipboard.writeText(typedUser.referralCode!)}
                  className="bg-white/10 hover:bg-white/20 text-white rounded-lg px-4 font-bold"
                >
                  COPY
                </button>
              </div>
            </div>
          ) : (
            <button
              onClick={async () => {
                const code = Math.random().toString(36).substring(2, 8).toUpperCase();
                await apiRequest("POST", "/api/referral/create", { code });
                window.location.reload();
              }}
              className="w-full bg-primary text-black font-black py-3 rounded-lg hover:bg-primary/90 transition-colors uppercase tracking-wide"
            >
              Generate Invite Code
            </button>
          )}

          {/* Redeem Code */}
          {!typedUser?.referredBy && (
            <div className="mt-4 pt-4 border-t border-primary/20">
              <label className="text-xs text-gray-500 font-bold uppercase block mb-2">Have a code?</label>
              <div className="flex gap-2">
                <input
                  value={referralInput}
                  onChange={(e) => setReferralInput(e.target.value.toUpperCase())}
                  placeholder="CODE"
                  className="flex-1 bg-white/5 border border-white/10 rounded-lg px-3 py-2 text-white font-mono tracking-widest uppercase focus:border-primary/50 outline-none"
                />
                <button
                  onClick={() => redeemMutation.mutate(referralInput)}
                  disabled={!referralInput || redeemMutation.isPending}
                  className="bg-primary/20 hover:bg-primary/30 text-primary border border-primary/50 rounded-lg px-4 font-bold uppercase text-xs"
                >
                  {redeemMutation.isPending ? "..." : "Redeem"}
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Actions */}
        <a
          href="/api/logout"
          data-testid="button-logout"
          className="w-full bg-red-500/20 border-2 border-red-500/50 text-red-400 py-4 font-black text-lg flex items-center justify-center gap-3 font-heading uppercase hover:bg-red-500 hover:text-white transition-all"
        >
          <LogOut className="w-6 h-6" />
          {t('profile.signOut')}
        </a>
      </div>
    </div>
  );
}
