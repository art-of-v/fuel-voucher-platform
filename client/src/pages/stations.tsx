
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
    <div className="p-6 pt-8 space-y-6 relative overflow-hidden">
      {/* Aggressive background effects */}
      <div className="absolute -left-20 -top-20 w-64 h-64 bg-primary/30 rounded-full blur-[100px] animate-pulse" />
      <div className="absolute -right-20 top-1/2 w-48 h-48 bg-red-500/20 rounded-full blur-[80px]" />

      <header className="relative z-10">
        {/* AGGRESSIVE Branding Section */}
        <div className="flex items-center gap-4 mb-8 relative">
          <div className="w-28 h-28 bg-black border-4 border-primary flex items-center justify-center relative overflow-hidden animate-pulse-glow">
            {/* Intense glow background */}
            <div className="absolute inset-0 bg-gradient-to-br from-primary/30 via-transparent to-red-500/20" />
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(0,255,128,0.3)_0%,transparent_70%)]" />

            {/* Logo with intense effects */}
            <img src={lionLogo} alt="Lemberg Fuel Corp." className="w-24 h-24 object-contain drop-shadow-[0_0_30px_rgba(0,255,128,1)] relative z-10 saturate-150 contrast-125" />

            {/* Corner accents - sharper */}
            <div className="absolute top-0 left-0 w-4 h-4 border-t-4 border-l-4 border-primary shadow-[0_0_10px_rgba(0,255,128,0.8)]" />
            <div className="absolute top-0 right-0 w-4 h-4 border-t-4 border-r-4 border-primary shadow-[0_0_10px_rgba(0,255,128,0.8)]" />
            <div className="absolute bottom-0 left-0 w-4 h-4 border-b-4 border-l-4 border-red-500 shadow-[0_0_10px_rgba(255,50,50,0.8)]" />
            <div className="absolute bottom-0 right-0 w-4 h-4 border-b-4 border-r-4 border-red-500 shadow-[0_0_10px_rgba(255,50,50,0.8)]" />

            {/* Scan line effect */}
            <div className="absolute inset-0 bg-[linear-gradient(transparent_50%,rgba(0,0,0,0.3)_50%)] bg-[length:100%_4px] pointer-events-none opacity-30" />
          </div>
          <div className="flex-1">
            <h2 className="text-primary font-heading font-black uppercase tracking-[0.15em] text-4xl leading-none mb-1 text-glow-intense animate-flicker">
              LEMBERG
            </h2>
            <h3 className="text-white font-heading font-bold uppercase tracking-[0.3em] text-xl leading-none">
              FUEL CORP.
            </h3>
            <div className="flex items-center gap-2 mt-2">
              <div className="h-px flex-1 bg-gradient-to-r from-primary to-transparent" />
              <span className="text-[10px] text-primary font-mono tracking-[0.3em] uppercase">{t('stations.dominate')}</span>
              <div className="h-px flex-1 bg-gradient-to-l from-primary to-transparent" />
            </div>
          </div>
        </div>

        {/* Aggressive Title */}
        <div className="relative mb-6">
          <div className="absolute -left-6 top-0 bottom-0 w-1 bg-primary box-glow" />
          <h1 className="text-5xl font-black text-white leading-[0.9] tracking-tighter uppercase font-heading pl-4">
            {t('stations.title')}<br />
            <span className="text-primary text-glow-intense">{t('stations.title2')}</span>
          </h1>
        </div>

        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-3 text-red-500 font-mono text-xs tracking-widest uppercase bg-red-500/10 border border-red-500/30 px-4 py-2 rounded">
            <AlertTriangle className="w-4 h-4 animate-pulse" />
            <span>// {t('stations.authorized')}</span>
          </div>
          <Link href="/map">
            <button className="flex items-center gap-2 bg-primary/10 border border-primary/50 text-primary px-4 py-2 rounded font-mono text-xs tracking-widest uppercase hover:bg-primary hover:text-black transition-all">
              <MapPin className="w-4 h-4" />
              <span>MAP VIEW</span>
            </button>
          </Link>
        </div>
      </header>

      <div className="grid grid-cols-1 gap-3 relative z-10">
        {STATIONS.map((station, index) => (
          <button
            key={station.id}
            onClick={() => handleSelect(station)}
            data-testid={`station-${station.id}`}
            className="group relative overflow-hidden rounded-lg bg-black/80 border-2 border-white/10 p-0 transition-all duration-300 hover:border-primary hover:box-glow text-left flex items-stretch active:scale-[0.98]"
          >
            {/* Side accent bar */}
            <div className={`w-2 ${station.id === 'okko' ? 'bg-green-500' :
              station.id === 'wog' ? 'bg-emerald-400' :
                station.id === 'upg' ? 'bg-cyan-400' :
                  'bg-yellow-400'
              } group-hover:shadow-[0_0_20px_currentColor]`} />

            {/* Content */}
            <div className="flex-1 p-5 flex items-center justify-between">
              <div className="relative z-10">
                <div className="flex items-center gap-3">
                  <span className={`text-4xl font-black tracking-tighter uppercase font-heading ${station.id === 'okko' ? 'text-green-500' :
                    station.id === 'wog' ? 'text-emerald-400' :
                      station.id === 'upg' ? 'text-cyan-400' :
                        'text-yellow-400'
                    } group-hover:text-glow transition-all`}>
                    {station.logoText}
                  </span>
                  <Zap className={`w-5 h-5 ${station.id === 'okko' ? 'text-green-500' :
                    station.id === 'wog' ? 'text-emerald-400' :
                      station.id === 'upg' ? 'text-cyan-400' :
                        'text-yellow-400'
                    } opacity-50 group-hover:opacity-100 group-hover:animate-pulse`} />
                </div>
                <div className="flex items-center gap-2 mt-2">
                  <div className="w-2 h-2 rounded-full bg-primary animate-pulse shadow-[0_0_10px_rgba(0,255,128,0.8)]" />
                  <span className="text-[10px] text-gray-400 font-mono uppercase tracking-[0.2em]">{t('stations.online')} • {t('stations.ready')}</span>
                </div>
              </div>

              <div className="relative z-10 w-12 h-12 bg-primary/10 border-2 border-primary/50 flex items-center justify-center text-primary group-hover:bg-primary group-hover:text-black transition-all group-hover:shadow-[0_0_30px_rgba(0,255,128,0.5)]">
                <ArrowRight className="w-6 h-6" />
              </div>
            </div>

            {/* Hover overlay */}
            <div className="absolute inset-0 bg-gradient-to-r from-primary/5 to-transparent opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none" />
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
  );
}
