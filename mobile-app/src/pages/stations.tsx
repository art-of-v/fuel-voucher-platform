
import { STATIONS } from "@/lib/mock-data";
import { useStore } from "@/lib/store";
import { useLocation, Link } from "wouter";
import { ArrowRight, Zap, Skull, AlertTriangle, MapPin } from "lucide-react";
import lionLogo from "@assets/generated_images/profile_cyberpunk_lion_logo.png";
import { useI18n } from "@/lib/i18n";

export default function StationsScreen() {
  const [, setLocation] = useLocation();
  const selectStation = useStore((state) => state.selectStation);
  const { t } = useI18n();

  const handleSelect = (station: typeof STATIONS[0]) => {
    selectStation(station);
    setLocation(`/station/${station.id}`);
  };

  return (
    <div className="relative overflow-hidden">
      <div className="mx-auto p-4 pt-6 space-y-6 relative">
        {/* Aggressive background effects */}
        <div className="absolute -left-10 -top-10 w-48 h-48 bg-primary/20 rounded-full blur-[80px] animate-pulse" />
        <div className="absolute -right-10 top-1/2 w-40 h-40 bg-red-500/10 rounded-full blur-[60px]" />

        <header className="relative z-10">
          {/* AGGRESSIVE Branding Section */}
          <div className="flex items-center gap-3 mb-8 relative">
            <div className="w-20 h-20 bg-black border-2 border-primary flex items-center justify-center relative overflow-hidden animate-pulse-glow flex-shrink-0">
              {/* Intense glow background */}
              <div className="absolute inset-0 bg-gradient-to-br from-primary/30 via-transparent to-red-500/10" />
              <img src={lionLogo} alt="Lemberg Fuel Corp." className="w-16 h-16 object-contain drop-shadow-[0_0_20px_rgba(0,255,128,0.8)] relative z-10" />

              {/* Corner accents */}
              <div className="absolute top-0 left-0 w-3 h-3 border-t-2 border-l-2 border-primary" />
              <div className="absolute top-0 right-0 w-3 h-3 border-t-2 border-r-2 border-primary" />
              <div className="absolute bottom-0 left-0 w-3 h-3 border-b-2 border-l-2 border-red-500" />
              <div className="absolute bottom-0 right-0 w-3 h-3 border-b-2 border-r-2 border-red-500" />
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-primary font-heading font-black uppercase tracking-[0.1em] text-2xl leading-none mb-1 text-glow animate-flicker">
                LEMBERG
              </h2>
              <h3 className="text-white font-heading font-bold uppercase tracking-[0.15em] text-sm leading-none">
                FUEL CORP.
              </h3>
              <div className="flex items-center gap-2 mt-2">
                <div className="h-px flex-1 bg-gradient-to-r from-primary to-transparent" />
                <span className="text-[8px] text-primary font-mono tracking-widest uppercase whitespace-nowrap">{t('stations.dominate')}</span>
                <div className="h-px flex-1 bg-gradient-to-l from-primary to-transparent" />
              </div>
            </div>
          </div>

          {/* Aggressive Title */}
          <div className="relative mb-6">
            <div className="absolute -left-4 top-0 bottom-0 w-1 bg-primary box-glow" />
            <h1 className="text-3xl font-black text-white leading-[0.9] tracking-tighter uppercase font-heading pl-3">
              {t('stations.title')}<br />
              <span className="text-primary text-glow">{t('stations.title2')}</span>
            </h1>
          </div>

          {/* Action Row - Forced Stack on Mobile width */}
          <div className="flex flex-col gap-3 mb-6">
            <div className="flex items-center gap-2 text-red-500 font-mono text-[10px] tracking-widest uppercase bg-red-500/10 border border-red-500/20 px-3 py-2 rounded">
              <AlertTriangle className="w-3 h-3 animate-pulse flex-shrink-0" />
              <span className="truncate">// {t('stations.authorized')}</span>
            </div>
            <Link href="/map">
              <button className="flex items-center justify-center gap-2 bg-primary/10 border border-primary/50 text-primary px-3 py-3 rounded font-mono text-[10px] tracking-widest uppercase hover:bg-primary hover:text-black transition-all w-full">
                <MapPin className="w-3 h-3 flex-shrink-0" />
                <span>{t('map.view')}</span>
              </button>
            </Link>
          </div>
        </header>

        <div className="grid grid-cols-1 gap-3 relative z-10">
          {STATIONS.map((station) => (
            <button
              key={station.id}
              onClick={() => handleSelect(station)}
              className="group relative overflow-hidden rounded-lg bg-black/80 border-2 border-white/10 p-0 transition-all hover:border-primary hover:box-glow text-left flex items-stretch active:scale-[0.98]"
            >
              <div className={`w-2 ${station.id === 'okko' ? 'bg-green-500' :
                station.id === 'wog' ? 'bg-emerald-400' :
                  station.id === 'upg' ? 'bg-cyan-400' :
                    'bg-yellow-400'
                }`} />
              <div className="flex-1 p-5 flex items-center justify-between">
                <div>
                  <div className="flex items-center gap-3">
                    <span className={`text-4xl font-black tracking-tighter uppercase font-heading ${station.id === 'okko' ? 'text-green-500' :
                      station.id === 'wog' ? 'text-emerald-400' :
                        station.id === 'upg' ? 'text-cyan-400' :
                          'text-yellow-400'
                      }`}>
                      {station.logoText}
                    </span>
                    <Zap className="w-5 h-5 opacity-50" />
                  </div>
                  <div className="flex items-center gap-2 mt-2 font-mono text-[10px] text-gray-500 uppercase tracking-widest">
                    <div className="w-2 h-2 rounded-full bg-primary animate-pulse" />
                    <span>{t('stations.online')}</span>
                  </div>
                </div>
                <div className="w-12 h-12 bg-primary/10 border-2 border-primary/50 flex items-center justify-center text-primary group-hover:bg-primary group-hover:text-black transition-all">
                  <ArrowRight className="w-6 h-6" />
                </div>
              </div>
            </button>
          ))}
        </div>

        {/* Bottom warning */}
        <div className="text-center py-4 border-t border-white/5">
          <p className="text-[10px] text-gray-600 font-mono tracking-[0.3em] uppercase">
            [ ENCRYPTED TRANSACTION PROTOCOL v2.4 ]
          </p>
        </div>
      </div>
    </div>
  );
}
