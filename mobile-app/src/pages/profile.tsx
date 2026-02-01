import { useState } from "react";
import { useAuth } from "@/hooks/useAuth";
import { User, LogIn, LogOut, Mail, Phone, Zap, Car, Gift, Bell, Check, Globe } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import type { User as UserType } from "@shared/schema";
import { useI18n, languages } from "@/lib/i18n";
import { PhoneAuth } from "@/components/phone-auth";
import { apiRequest } from "@/lib/utils";
import { useLocation } from "wouter";
import { PageLayout } from "@/components/page-layout";

export default function ProfileScreen() {
  const { user, isLoading, isAuthenticated, authType } = useAuth();
  const { t, language, setLanguage } = useI18n();
  const [showPhoneAuth, setShowPhoneAuth] = useState(false);
  const [referralInput, setReferralInput] = useState("");
  const queryClient = useQueryClient();
  const [, setLocation] = useLocation();

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
      queryClient.invalidateQueries({ queryKey: ["/api/auth/user"] });
      queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
      queryClient.invalidateQueries({ queryKey: ["/api/notifications"] });
      setReferralInput("");
      alert(t('profile.codeRedeemed'));
    },
    onError: (err: any) => {
      alert(err.message || t('profile.redeemFailed'));
    }
  });

  const handleLogout = async () => {
    try {
      if (authType === 'phone') {
        await apiRequest("POST", "/api/auth/phone/logout");
      } else {
        // For Replit/Standard auth
        window.location.href = "/api/logout";
        return;
      }

      // Clear all queries
      queryClient.clear();

      // Force redirect to home
      window.location.href = "/";
    } catch (err) {
      console.error("Logout failed:", err);
      // Force redirect anyway
      window.location.href = "/";
    }
  };

  const handlePhoneAuthSuccess = async () => {
    await queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
    await queryClient.invalidateQueries({ queryKey: ["/api/auth/user"] });
    setShowPhoneAuth(false);
    setLocation("/");
  };

  const backgroundBlob = (
    <div className="absolute top-0 right-0 w-64 h-64 bg-primary/10 rounded-full blur-[100px]" />
  );

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full min-h-[50vh]">
        <div className="text-primary font-mono animate-pulse flex items-center gap-3">
          <Zap className="w-6 h-6 animate-spin" />
          {t('common.loading')}
        </div>
      </div>
    );
  }

  // AuthRequired view - Phone Auth Only
  if (!isAuthenticated && !showPhoneAuth) {
    return (
      <PageLayout background={backgroundBlob}>
        <div className="flex flex-col items-center justify-center p-4 sm:p-8 relative min-h-[70vh] h-full">
          <div className="absolute top-1/3 left-1/2 -translate-x-1/2 w-64 h-64 bg-primary/20 rounded-full blur-[100px]" />

          <div className="relative z-10 text-center max-w-sm w-full">
            <div className="w-20 h-20 sm:w-24 sm:h-24 bg-primary/10 border-2 sm:border-4 border-primary/30 flex items-center justify-center mx-auto mb-6 sm:mb-8">
              <User className="w-10 h-10 sm:w-12 sm:h-12 text-primary" />
            </div>

            <h1 className="text-2xl sm:text-4xl font-black text-white font-heading uppercase mb-3 sm:mb-4">
              {t('profile.accessRequired')}
            </h1>
            <p className="text-gray-400 font-mono text-xs sm:text-sm mb-6 sm:mb-8 px-4">
              {t('profile.signInDesc')}
            </p>

            {/* Phone Auth - Primary Login Method */}
            <button
              onClick={() => setShowPhoneAuth(true)}
              data-testid="button-phone-login"
              className="inline-flex items-center gap-2 sm:gap-3 bg-primary text-black px-6 sm:px-8 py-3 sm:py-4 font-black text-base sm:text-lg font-heading uppercase shadow-[0_0_40px_rgba(0,255,128,0.5)] hover:shadow-[0_0_60px_rgba(0,255,128,0.7)] transition-all w-full sm:w-auto justify-center"
            >
              <Phone className="w-5 h-5 sm:w-6 sm:h-6" />
              {t('phoneAuth.title')}
            </button>

            <p className="text-[9px] sm:text-[10px] text-gray-600 font-mono mt-4 sm:mt-6 uppercase tracking-wider">
              {t('phoneAuth.verificationRequired')}
            </p>
          </div>
        </div>
      </PageLayout>
    );
  }

  // PhoneAuth view
  if (showPhoneAuth) {
    return (
      <PageLayout background={backgroundBlob} scrollClassName="p-4 sm:p-6 pt-6 sm:pt-10">
        <PhoneAuth onSuccess={handlePhoneAuthSuccess} />
        <button
          onClick={() => setShowPhoneAuth(false)}
          className="w-full text-gray-400 py-3 sm:py-4 text-xs sm:text-sm hover:text-white transition-colors mt-4"
        >
          {t('common.back')}
        </button>
      </PageLayout>
    );
  }

  const typedUser = user as UserType;

  const header = (
    <header className="p-6 pb-2 sm:pb-4 border-b border-white/5 bg-background/80 backdrop-blur-sm">
      <h1 className="text-2xl sm:text-4xl font-black text-white font-heading uppercase">{t('profile.title')}</h1>
      <p className="text-[10px] sm:text-xs text-primary font-mono tracking-[0.15em] sm:tracking-[0.2em] uppercase mt-1">
        {t('profile.subtitle')}
      </p>
    </header>
  );

  return (
    <PageLayout
      header={header}
      background={backgroundBlob}
      scrollClassName="p-4 sm:p-6 space-y-4 sm:space-y-6"
    >
      {/* Notifications */}
      {notifications.length > 0 && (
        <div className="bg-black/80 border-2 border-primary/30 p-6 space-y-4">
          <h3 className="text-xl font-black text-white font-heading uppercase flex items-center gap-2">
            <Bell className="w-5 h-5 text-primary" />
            {t('profile.updates')}
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
      <div className="bg-black/80 border-2 border-primary/30 p-4 sm:p-6">
        <div className="flex items-center gap-3 sm:gap-4 mb-4 sm:mb-6">
          <div className="w-16 h-16 sm:w-20 sm:h-20 bg-primary/20 border-2 border-primary flex items-center justify-center flex-shrink-0">
            {typedUser.profileImageUrl ? (
              <img src={typedUser.profileImageUrl} alt="Profile" className="w-full h-full object-cover" />
            ) : (
              <User className="w-10 h-10 text-primary" />
            )}
          </div>
          <div className="min-w-0">
            <h2 className="text-xl sm:text-2xl font-black text-white font-heading uppercase truncate">
              {typedUser.firstName || typedUser.phone || typedUser.email?.split('@')[0] || t('profile.operator')}
            </h2>
            {typedUser.lastName && (
              <p className="text-gray-400 font-heading uppercase">{typedUser.lastName}</p>
            )}
          </div>
        </div>

        {typedUser.phone && (
          <div className="flex items-center gap-3 text-gray-400 bg-white/5 p-3 border border-white/10 mb-2">
            <Phone className="w-5 h-5 text-primary" />
            <span className="font-mono text-sm">{typedUser.phone}</span>
          </div>
        )}
      </div>

      {/* Personal Information */}
      <div className="bg-black/80 border-2 border-primary/30 p-4 sm:p-6 space-y-3 sm:space-y-4">
        <h3 className="text-lg sm:text-xl font-black text-white font-heading uppercase flex items-center gap-2">
          <User className="w-5 h-5 text-primary" />
          {t('profile.personalInfo')}
        </h3>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 sm:gap-4">
          <div className="space-y-1">
            <label className="text-xs text-gray-500 font-bold uppercase">{t('profile.firstName')}</label>
            <input
              className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
              placeholder="John"
              defaultValue={typedUser?.firstName || ""}
              onBlur={(e) => apiRequest("POST", `/api/users/update`, { firstName: e.target.value })}
            />
          </div>
          <div className="space-y-1">
            <label className="text-xs text-gray-500 font-bold uppercase">{t('profile.lastName')}</label>
            <input
              className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
              placeholder="Doe"
              defaultValue={typedUser?.lastName || ""}
              onBlur={(e) => apiRequest("POST", `/api/users/update`, { lastName: e.target.value })}
            />
          </div>
          <div className="sm:col-span-2 space-y-1">
            <label className="text-xs text-gray-500 font-bold uppercase">{t('profile.email')}</label>
            <input
              type="email"
              className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
              placeholder="john.doe@example.com"
              defaultValue={typedUser?.email || ""}
              onBlur={(e) => apiRequest("POST", `/api/users/update`, { email: e.target.value })}
            />
          </div>
          <div className="col-span-2 space-y-1">
            <label className="text-xs text-gray-500 font-bold uppercase">{t('profile.birthdate')}</label>
            <input
              type="date"
              className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
              defaultValue={typedUser?.birthdate || ""}
              onBlur={(e) => apiRequest("POST", `/api/users/update`, { birthdate: e.target.value })}
            />
          </div>
        </div>
      </div>

      {/* Language Settings */}
      <div className="bg-black/80 border-2 border-primary/30 p-4 sm:p-6 space-y-3 sm:space-y-4">
        <h3 className="text-lg sm:text-xl font-black text-white font-heading uppercase flex items-center gap-2">
          <Globe className="w-5 h-5 text-primary" />
          {t('profile.language')}
        </h3>
        <div className="grid grid-cols-2 gap-3">
          {languages.map((lang) => (
            <button
              key={lang.code}
              onClick={() => useI18n.getState().setLanguage(lang.code)}
              className={`flex items-center gap-3 px-4 py-3 border rounded-lg transition-all ${useI18n.getState().language === lang.code
                ? "bg-primary/20 border-primary text-primary"
                : "bg-white/5 border-white/10 text-gray-400 hover:border-white/30"
                }`}
            >
              <span className="text-2xl">{lang.flag}</span>
              <span className="font-bold uppercase text-sm font-heading">{lang.name}</span>
            </button>
          ))}
        </div>
      </div>

      {/* Vehicle Information */}
      <div className="bg-black/80 border-2 border-primary/30 p-4 sm:p-6 space-y-3 sm:space-y-4">
        <h3 className="text-lg sm:text-xl font-black text-white font-heading uppercase flex items-center gap-2">
          <Car className="w-5 h-5 text-primary" />
          {t('profile.vehicleDetails')}
        </h3>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 sm:gap-4">
          <div className="space-y-1">
            <label className="text-xs text-gray-500 font-bold uppercase">{t('profile.make')}</label>
            <input
              className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
              placeholder="e.g. BMW"
              defaultValue={typedUser?.vehicleMake || ""}
              onBlur={(e) => apiRequest("POST", `/api/users/update`, { vehicleMake: e.target.value })}
            />
          </div>
          <div className="space-y-1">
            <label className="text-xs text-gray-500 font-bold uppercase">{t('profile.model')}</label>
            <input
              className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
              placeholder="e.g. X5"
              defaultValue={typedUser?.vehicleModel || ""}
              onBlur={(e) => apiRequest("POST", `/api/users/update`, { vehicleModel: e.target.value })}
            />
          </div>
          <div className="space-y-1">
            <label className="text-xs text-gray-500 font-bold uppercase">{t('profile.plate')}</label>
            <input
              className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
              placeholder="AA0000AA"
              defaultValue={typedUser?.vehiclePlate || ""}
              onBlur={(e) => apiRequest("POST", `/api/users/update`, { vehiclePlate: e.target.value })}
            />
          </div>
          <div className="space-y-1">
            <label className="text-xs text-gray-500 font-bold uppercase">{t('profile.fuel')}</label>
            <select
              className="w-full bg-white/5 border border-white/10 rounded px-3 py-2 text-white text-sm focus:border-primary/50 outline-none"
              defaultValue={typedUser?.vehicleFuelType || ""}
              onChange={(e) => apiRequest("POST", `/api/users/update`, { vehicleFuelType: e.target.value })}
            >
              <option value="">{t('profile.select')}</option>
              <option value="petrol">{t('profile.petrol')}</option>
              <option value="diesel">{t('profile.diesel')}</option>
              <option value="lpg">{t('profile.lpg')}</option>
              <option value="electric">{t('profile.electric')}</option>
            </select>
          </div>
        </div>
      </div>

      {/* Referral System */}
      <div className="bg-gradient-to-br from-primary/10 to-transparent border-2 border-primary/30 p-4 sm:p-6 space-y-3 sm:space-y-4">
        <h3 className="text-lg sm:text-xl font-black text-white font-heading uppercase flex items-center gap-2">
          <Gift className="w-5 h-5 text-primary" />
          {t('profile.referralProgram')}
        </h3>

        <div className="flex items-center justify-between bg-black/40 p-4 rounded-lg border border-primary/20">
          <div>
            <p className="text-xs text-gray-400 font-mono uppercase">{t('profile.bonusBalance')}</p>
            <p className="text-3xl font-black text-primary font-heading">{typedUser?.bonusBalance || 0} {t('common.uah')}</p>
          </div>
          <Gift className="w-10 h-10 text-primary/20" />
        </div>

        {typedUser?.referralCode ? (
          <div className="space-y-2">
            <label className="text-xs text-gray-500 font-bold uppercase">{t('profile.yourInviteCode')}</label>
            <div className="flex gap-2">
              <div className="flex-1 bg-white/5 border border-white/10 rounded-lg flex items-center justify-center p-3 font-mono text-xl tracking-widest text-primary border-dashed">
                {typedUser.referralCode}
              </div>
              <button
                onClick={() => navigator.clipboard.writeText(typedUser.referralCode!)}
                className="bg-white/10 hover:bg-white/20 text-white rounded-lg px-4 font-bold"
              >
                {t('profile.copy')}
              </button>
            </div>
          </div>
        ) : (
          <button
            onClick={async () => {
              const code = Math.random().toString(36).substring(2, 8).toUpperCase();
              await apiRequest("POST", "/api/referral/create", { code });
              queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
              queryClient.invalidateQueries({ queryKey: ["/api/auth/user"] });
            }}
            className="w-full bg-primary text-black font-black py-3 rounded-lg hover:bg-primary/90 transition-colors uppercase tracking-wide"
          >
            {t('profile.generateCode')}
          </button>
        )}

        {/* Redeem Code */}
        {!typedUser?.referredBy && (
          <div className="mt-4 pt-4 border-t border-primary/20">
            <label className="text-xs text-gray-500 font-bold uppercase block mb-2">{t('profile.haveCode')}</label>
            <div className="flex gap-2">
              <input
                value={referralInput}
                onChange={(e) => setReferralInput(e.target.value.toUpperCase())}
                placeholder={t('profile.inviteCode')}
                className="flex-1 bg-white/5 border border-white/10 rounded-lg px-3 py-2 text-white font-mono tracking-widest uppercase focus:border-primary/50 outline-none"
              />
              <button
                onClick={() => redeemMutation.mutate(referralInput)}
                disabled={!referralInput || redeemMutation.isPending}
                className="bg-primary/20 hover:bg-primary/30 text-primary border border-primary/50 rounded-lg px-4 font-bold uppercase text-xs"
              >
                {redeemMutation.isPending ? "..." : t('profile.redeem')}
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Actions */}
      <button
        onClick={handleLogout}
        data-testid="button-logout"
        className="w-full bg-red-500/20 border-2 border-red-500/50 text-red-400 py-3 sm:py-4 font-black text-base sm:text-lg flex items-center justify-center gap-2 sm:gap-3 font-heading uppercase hover:bg-red-500 hover:text-white transition-all"
      >
        <LogOut className="w-6 h-6" />
        {t('profile.signOut')}
      </button>
    </PageLayout>
  );
}
