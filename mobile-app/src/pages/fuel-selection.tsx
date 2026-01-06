
import { FUELS } from "@/lib/mock-data";
import { useStore } from "@/lib/store";
import { useLocation, useRoute } from "wouter";
import { ChevronLeft, Droplets, TrendingDown, Zap, AlertTriangle } from "lucide-react";

export default function FuelSelectionScreen() {
  const [match, params] = useRoute("/station/:id");
  const [, setLocation] = useLocation();
  const { selectedStation, selectFuel } = useStore();

  const fuels = FUELS.filter(f => f.stationId === params?.id);

  const handleSelect = (fuel: typeof FUELS[0]) => {
    selectFuel(fuel);
    setLocation("/packages");
  };

  if (!selectedStation) {
    return <div className="p-6 text-white">Please select a station first.</div>;
  }

  return (
    <div className="min-h-screen bg-background text-foreground relative">
      {/* Background glow */}
      <div className="absolute top-0 left-1/2 -translate-x-1/2 w-96 h-96 bg-primary/20 rounded-full blur-[150px]" />
      
      {/* Dynamic Header */}
      <div className={`h-64 relative p-6 flex flex-col justify-end overflow-hidden`}>
        <div className={`absolute inset-0 opacity-30 ${
          selectedStation.id === 'okko' ? 'bg-green-600' :
          selectedStation.id === 'wog' ? 'bg-emerald-500' :
          selectedStation.id === 'upg' ? 'bg-cyan-500' :
          'bg-yellow-500'
        }`} />
        <div className="absolute inset-0 bg-gradient-to-b from-transparent via-transparent to-background" />
        
        <button 
          onClick={() => setLocation("/")}
          data-testid="button-back"
          className="absolute top-6 left-6 p-2 bg-black/60 backdrop-blur-md border-2 border-white/20 hover:border-primary transition-colors z-20"
        >
          <ChevronLeft className="w-6 h-6 text-white" />
        </button>

        <div className="relative z-10">
          <div className="flex items-center gap-3 mb-2">
            <Zap className="w-8 h-8 text-primary animate-pulse" />
            <h1 className={`text-6xl font-black tracking-tighter font-heading uppercase ${
              selectedStation.id === 'okko' ? 'text-green-500' :
              selectedStation.id === 'wog' ? 'text-emerald-400' :
              selectedStation.id === 'upg' ? 'text-cyan-400' :
              'text-yellow-400'
            } text-glow-intense`}>
              {selectedStation.logoText}
            </h1>
          </div>
          <div className="flex items-center gap-3">
            <div className="h-px flex-1 bg-gradient-to-r from-primary to-transparent" />
            <span className="px-3 py-1 bg-red-500/20 text-red-400 text-[10px] font-mono tracking-[0.2em] border border-red-500/30 flex items-center gap-2">
              <AlertTriangle className="w-3 h-3" />
              LIVE RATES
            </span>
          </div>
        </div>
      </div>

      <div className="p-4 -mt-6 space-y-3 relative z-10">
        {fuels.map((fuel, index) => (
          <button
            key={fuel.id}
            onClick={() => handleSelect(fuel)}
            data-testid={`fuel-${fuel.id}`}
            className="w-full bg-black/80 border-2 border-white/10 hover:border-primary p-0 flex items-stretch group active:scale-[0.99] transition-all relative overflow-hidden hover:box-glow"
          >
            {/* Side accent */}
            <div className="w-1.5 bg-primary group-hover:shadow-[0_0_20px_rgba(0,255,128,0.8)]" />
            
            <div className="flex-1 p-5 flex items-center justify-between">
              <div className="flex items-center gap-4 relative z-10">
                <div className="w-14 h-14 bg-primary/10 border-2 border-primary/30 flex items-center justify-center text-primary group-hover:bg-primary group-hover:text-black transition-all">
                  <Droplets className="w-7 h-7" />
                </div>
                <div className="text-left">
                  <h3 className="font-black text-white text-2xl tracking-tight uppercase font-heading">{fuel.name}</h3>
                  <div className="flex items-center gap-3 text-sm mt-1">
                    <span className="text-gray-600 line-through font-mono">{fuel.basePrice.toFixed(2)}</span>
                    <span className="text-primary font-black font-mono text-xl text-glow">
                      {fuel.discountPrice.toFixed(2)} ₴
                    </span>
                  </div>
                </div>
              </div>
              
              <div className="text-right relative z-10">
                <div className="bg-primary text-black font-black text-sm px-4 py-2 flex items-center gap-2 shadow-[0_0_20px_rgba(0,255,128,0.5)] animate-savings-pulse">
                  <TrendingDown className="w-4 h-4" />
                  -{(fuel.basePrice - fuel.discountPrice).toFixed(2)} ₴/L
                </div>
              </div>
            </div>
          </button>
        ))}
      </div>
      
      <div className="p-4 text-center">
        <p className="text-[10px] text-gray-600 font-mono tracking-[0.2em] uppercase">
          [ BULK DISCOUNT RATES ACTIVE ]
        </p>
      </div>
    </div>
  );
}
